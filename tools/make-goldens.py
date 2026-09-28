#!/usr/bin/env python3
# SPDX-FileCopyrightText: Boutap contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Fabrique les valeurs de reference du portage C# de l'analyse audio.

Ce que ce script produit
------------------------
Un fichier JSON de reference que les tests C# de Boutap.Gen comparent a leur
propre sortie. Les valeurs stockees ne sont PAS celles que librosa a calculees :
elles sont produites par une transcription de ses formules en double precision.
Chaque fonction de transcription indique le nom de la fonction librosa qu'elle
recopie. Le bloc ``agreement`` du fichier mesure l'ecart entre les deux, de sorte
que le fichier lui-meme prouve que la transcription est fidele, et qu'un librosa
qui changerait un jour de comportement se verrait ici au lieu de faire echouer
un test C# sans explication.

Pourquoi ne pas stocker directement la sortie de librosa
--------------------------------------------------------
Parce que librosa travaille en simple precision par defaut, et que le digest
servirait alors de rien : le C# ne peut pas etre identique au bit pres. Avec
une transcription en double precision, le C# et ce fichier se rejoignent a
1e-12 pres, ce qui autorise un test tolerant a 1e-6 — assez strict pour
attraper une transposition d'axe ou un decalage de trame, assez large pour
survivre a une difference d'ordre des flottants entre deux machines.

Ce que ce script n'est pas
--------------------------
Un script de CI. Il demande librosa et numpy, ce qui serait trop lourd et non
deterministe d'une machine a l'autre. Les fichiers produits sont versionnes,
comme les fixtures ``.btp``.

Usage :
    tools/make-goldens.py <chemin .wav> <dossier de sortie>

Parametres du signal, identiques cote C# (Boutap.Gen/Dsp/AnalysisSettings.cs) :
    n_fft      = 2048
    hop_length = 512
    fenetre    = Hann periodique
    centrage   = actif, remplissage par reflexion
    n_mels     = 128, echelle mel de Slaney, normalisation de Slaney
    n_chroma   = 12, norme 2

Choix volontairement absents : pas de CQT, pas de HPSS, pas de tempogramme.
Ils ne sont pas necessaires pour les primitives que la V0.1 porte.
"""

from __future__ import annotations

import hashlib
import json
import os
import sys
import wave

N_FFT = 2048
HOP_LENGTH = 512
N_MELS = 128
N_CHROMA = 12
TUNING = 0.0

# Cadence de contraction des vecteurs longs : on ne compare pas 862 nombres
# dans un fichier de test, on compare des tranches representatives.
ROW_COUNT = 6           # nombre de trames mel conservees
ENV_FRAMES = 861        # nombre de valeurs d'enveloppe conservees
ROW_POWER_FLOOR = 1e-6  # en dessous, une trame est silencieuse et n'apprend rien

SIGNIFICANT_DIGITS = 12   # arrondi des valeurs stockees
DIGEST_DIGITS = 6         # empreinte grossiere : 7 chiffres significatifs
AGREEMENT_TOLERANCE = 1e-9  # ecart maximal admis face a librosa lui-meme


def fail(message: str) -> "None":
    sys.stderr.write(f"make-goldens: {message}\n")
    raise SystemExit(1)


# --------------------------------------------------------------------------
# Transcription des formules de librosa 0.10.2
# --------------------------------------------------------------------------

def read_wav_mono(path: str):
    """Lit un WAV PCM 16 bits et renvoie (echantillons float64, frequence).

    Meme conversion que Boutap.Audio.WavDecoder : un echantillon entier divise
    par 32768, donc la meme valeur que soundfile, donc la meme que librosa.
    """
    import numpy as np

    with wave.open(path, "rb") as handle:
        if handle.getsampwidth() != 2:
            fail(f"{path} : seules les echelles 16 bits sont gerees ici.")
        if handle.getnchannels() != 1:
            fail(f"{path} : seul le mono est gere ici.")
        rate = handle.getframerate()
        raw = handle.readframes(handle.getnframes())
    samples = np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768.0
    return np.ascontiguousarray(samples), int(rate)


def hz_to_mel(freq):
    """core.convert.hz_to_mel, variante Slaney (htk=False)."""
    import numpy as np

    frequencies = np.atleast_1d(np.asarray(freq, dtype=np.float64))
    f_sp = 200.0 / 3.0
    mels = frequencies / f_sp
    min_log_hz = 1000.0
    min_log_mel = min_log_hz / f_sp
    logstep = np.log(6.4) / 27.0
    mask = frequencies >= min_log_hz
    if mask.any():
        mels[mask] = min_log_mel + np.log(frequencies[mask] / min_log_hz) / logstep
    return mels


def mel_to_hz(mels):
    """core.convert.mel_to_hz, variante Slaney (htk=False)."""
    import numpy as np

    mels = np.atleast_1d(np.asarray(mels, dtype=np.float64))
    f_sp = 200.0 / 3.0
    freqs = f_sp * mels
    min_log_hz = 1000.0
    min_log_mel = min_log_hz / f_sp
    logstep = np.log(6.4) / 27.0
    mask = mels >= min_log_mel
    if mask.any():
        freqs[mask] = min_log_hz * np.exp(logstep * (mels[mask] - min_log_mel))
    return freqs


def mel_frequencies(n_mels: int, fmin: float, fmax: float):
    """core.convert.mel_frequencies : n_mels valeurs reparties sur l'echelle mel."""
    import numpy as np

    return mel_to_hz(np.linspace(hz_to_mel(fmin)[0], hz_to_mel(fmax)[0], n_mels))


def fft_frequencies(sr: float, n_fft: int):
    """core.convert.fft_frequencies : les 1 + n_fft/2 bins de la vraie transformee."""
    import numpy as np

    return np.fft.rfftfreq(n_fft, d=1.0 / sr)


def stft_frames(samples, n_fft: int, hop: int):
    """core.spectrum.stft : 1 + n/hop trames centrees, Hann periodique.

    librosa.complete le signal par reflexion de n_fft/2 echantillons de chaque
    cote, puis pose une fenetre de Hann periodique. Son optimisation interne
    evite de materialiser le signal complete, mais les trames produites sont
    celles de cette construction la ; c'est ce que le test verifie.
    """
    import numpy as np

    pad = n_fft // 2
    padded = np.pad(samples, (pad, pad), mode="reflect")
    n_frames = 1 + len(samples) // hop
    index = np.arange(n_frames)[:, None] * hop + np.arange(n_fft)[None, :]
    n = np.arange(n_fft, dtype=np.float64)
    window = 0.5 - 0.5 * np.cos(2.0 * np.pi * n / n_fft)
    return padded[index] * window, n_frames


def magnitude(power):
    """La racine de la puissance spectrale."""
    import numpy as np

    return np.sqrt(power)


def mel_filterbank(sr: float, n_fft: int, n_mels: int, fmin: float, fmax: float):
    """filters.mel : triangles sur l'echelle mel, normalises a energie constante.

    La normalisation dite de Slaney divise chaque bande par la moitie de sa
    largeur en frequence, ce qui rend l'energie par bande a peu pres constante.
    """
    import numpy as np

    freqs = fft_frequencies(sr, n_fft)
    edges = mel_frequencies(n_mels + 2, fmin, fmax)
    widths = np.diff(edges)
    ramps = edges[:, None] - freqs[None, :]
    weights = np.zeros((n_mels, 1 + n_fft // 2), dtype=np.float64)
    for i in range(n_mels):
        lower = -ramps[i] / widths[i]
        upper = ramps[i + 2] / widths[i + 1]
        weights[i] = np.maximum(0.0, np.minimum(lower, upper))
    weights *= (2.0 / (edges[2:n_mels + 2] - edges[:n_mels]))[:, None]
    return weights, edges


def hz_to_octaves(freq, tuning: float, bins_per_octave: int):
    """core.convert.hz_to_octs : hauteur en octaves, A4 = 440 Hz par defaut."""
    import numpy as np

    a440 = 440.0 * 2.0 ** (tuning / bins_per_octave)
    return np.log2(np.asarray(freq, dtype=np.float64) / (a440 / 16.0))


def chroma_filterbank(sr: float, n_fft: int, n_chroma: int, tuning: float):
    """filters.chroma : bosses gaussiennes par classe de hauteur, base sur C.

    Trois etapes successives, dans cet ordre exact : construction des bosses,
    normalisation en norme 2 par colonne, puis fenetre de dominance en octaves.
    Le decalage final de trois tons rend la premiere colonne un do.
    """
    import numpy as np

    ctroct, octwidth, base_c = 5.0, 2.0, True
    freqs = np.linspace(0, sr, n_fft, endpoint=False)[1:]
    bins = n_chroma * hz_to_octaves(freqs, tuning, n_chroma)
    # Le bin de continuite n'existe pas : on lui invente une valeur 1,5 octave
    # plus bas que le premier, pour que le chroma commence pile sur un do.
    bins = np.concatenate(([bins[0] - 1.5 * n_chroma], bins))
    widths = np.concatenate((np.maximum(bins[1:] - bins[:-1], 1.0), [1.0]))
    deltas = np.subtract.outer(bins, np.arange(0, n_chroma, dtype=np.float64)).T
    half = np.round(float(n_chroma) / 2.0)
    deltas = np.remainder(deltas + half + 10 * n_chroma, n_chroma) - half
    weights = np.exp(-0.5 * (2.0 * deltas / np.tile(widths, (n_chroma, 1))) ** 2)
    weights = weights / np.sqrt((weights ** 2).sum(axis=0, keepdims=True))
    weights = weights * np.tile(
        np.exp(-0.5 * (((bins / n_chroma - ctroct) / octwidth) ** 2)), (n_chroma, 1))
    if base_c:
        weights = np.roll(weights, -3 * (n_chroma // 12), axis=0)
    return np.ascontiguousarray(weights[:, :1 + n_fft // 2])


def normalize_l2(matrix, axis: int):
    """util.normalize(norm=2) : divise chaque tranche par sa norme euclidienne."""
    import numpy as np

    norm = np.sqrt((np.abs(matrix) ** 2).sum(axis=axis, keepdims=True))
    tiny = np.finfo(np.float64).tiny
    return matrix / np.where(norm < tiny, 1.0, norm)


def mel_spectrogram(power, filterbank):
    """feature.spectral.melspectrogram : produit de la puissance par le banc.

    La sortie est lineaire. La conversion en decibels est un autre choix, et
    l'enveloppe d'onsets la fait explicitement.
    """
    return power @ filterbank.T


def chroma_spectrogram(mag, filterbank):
    """feature.spectral.chroma_stft : magnitude par le banc, puis norme 2.

    Attention : quand on fournit S a librosa, il ignore power et utilise S tel
    quel. C'est donc la magnitude, et non la puissance, qui entre dans le
    chroma de ce script.

    La normalisation se fait sur le dernier axe, qui porte les douze classes de
    hauteur. librosa travaille en orientation opposee (bandes, trames) et
    ecrit axis=-2 : meme operation, autre index. Se tromper d'axe donnerait une
    enveloppe chromatique constante, c'est-a-dire aucune information du tout.
    """
    return normalize_l2(mag @ filterbank.T, axis=1)


def power_to_db(matrix, amin: float = 1e-10, top_db: float = 80.0, ref: float = 1.0):
    """core.spectrum.power_to_db : decibels, plancher, et plafond relatif."""
    import numpy as np

    out = 10.0 * np.log10(np.maximum(amin, matrix))
    out -= 10.0 * np.log10(np.maximum(amin, ref))
    if top_db is not None:
        out = np.maximum(out, out.max() - top_db)
    return out


def onset_envelope(mel):
    """onset.onset_strength : mel en decibels, difference decalee, moyenne.

    Ordre exact, tire de onset.onset_strength_multi :
      1. mel lineaire puis passage en decibels (ref = 1, top_db = 80) ;
      2. ref = S lui-meme, car max_size = 1 rendrait le filtrage neutre ;
      3. difference S[1:] - ref[:-1] ;
      4. les valeurs negatives tombent a zero ;
      5. moyenne sur les bandes de frequence ;
      6. on rebouche lag + n_fft/2/hop valeurs a zero en tete ;
      7. on rogne a la longueur du spectrogramme.
    """
    import numpy as np

    decibels = power_to_db(mel.T)          # (bandes, trames)
    lag = 1
    flux = decibels[..., lag:] - decibels[..., :-lag]
    flux = np.maximum(0.0, flux)
    envelope = flux.mean(axis=0)           # moyenne sur les bandes
    pad_width = lag + N_FFT // (2 * HOP_LENGTH)
    envelope = np.pad(envelope, (int(pad_width), 0), mode="constant")
    return envelope[:mel.shape[0]]


# --------------------------------------------------------------------------
# Comparaison avec librosa, et ecriture
# --------------------------------------------------------------------------

def relative(a, b) -> float:
    """Ecart relatif maximal, avec un plancher pour ne pas diviser par zero."""
    import numpy as np

    a = np.asarray(a, dtype=np.float64)
    b = np.asarray(b, dtype=np.float64)
    if a.shape != b.shape:
        return float("inf")
    scale = np.maximum(np.abs(b), 1e-300)
    return float(np.max(np.abs(a - b) / scale)) if a.size else 0.0


def rounded(values):
    """Arrondi a un nombre de chiffres significatifs, et non decimaux.

    Une valeur mel de l'ordre de 1e-08 n'a que deux chiffres significatifs a
    une decimale pres : un arrondi decimal la reduirait a 5,9e-08 et
    masquerait tout ecart avec le C#. L'arrondi se fait donc sur la mantisse.
    """
    import numpy as np

    return [float(f"{float(v):.{SIGNIFICANT_DIGITS}g}") for v in np.ravel(values)]


def rounded_rows(matrix):
    """Comme rounded, mais sans aplatir : une ligne par trame conservee."""
    import numpy as np

    return [rounded(row) for row in np.atleast_2d(matrix)]


def digest(values) -> str:
    """Empreinte grossiere d'un tableau, stable entre plateformes.

    Sept chiffres significatifs suffisent : elle doit attraper une erreur de
    structure (axe inverse, trame decalee, banc faux) et rien d'autre. Une
    empreinte au bit pres serait impossible a satisfaire depuis un autre
    langage, donc inutile.
    """
    import numpy as np

    payload = ",".join(f"{float(v):.{DIGEST_DIGITS}e}" for v in np.ravel(values))
    return hashlib.sha256(payload.encode("ascii")).hexdigest()


def load_samples(wav_path: str):
    import numpy as np

    try:
        import librosa
    except ImportError as exc:  # pragma: no cover - dependance de maintenance
        fail(f"librosa et numpy sont requis ({exc}). Utiliser tools/probe/requirements.txt.")
    y, sr = librosa.load(wav_path, sr=None, mono=True)
    if y.dtype != np.float32:
        y = y.astype(np.float32)
    return np.ascontiguousarray(y), int(sr)


def agreement(y, sr: int, mel_fb, chroma_fb, frames, mel, mag, chroma, env):
    """Ecart entre la transcription et librosa, section par section."""
    import numpy as np
    import librosa

    complex_stft = librosa.stft(
        y, n_fft=N_FFT, hop_length=HOP_LENGTH, win_length=N_FFT, window="hann",
        center=True, pad_mode="reflect", dtype=np.complex128)
    power_ref = np.abs(complex_stft) ** 2
    power_ours = (np.fft.rfft(frames, axis=1).real ** 2
                  + np.fft.rfft(frames, axis=1).imag ** 2)

    mel_ref = librosa.feature.melspectrogram(
        y=y, sr=sr, n_fft=N_FFT, hop_length=HOP_LENGTH, win_length=N_FFT,
        window="hann", center=True, pad_mode="reflect", power=2.0, n_mels=N_MELS,
        fmin=0.0, fmax=sr / 2, htk=False, norm="slaney", dtype=np.float64)
    mel_fb_ref = librosa.filters.mel(sr=sr, n_fft=N_FFT, n_mels=N_MELS, fmin=0.0,
                                     fmax=sr / 2, htk=False, norm="slaney",
                                     dtype=np.float64)
    chroma_fb_ref = librosa.filters.chroma(sr=sr, n_fft=N_FFT, n_chroma=N_CHROMA,
                                          tuning=TUNING, dtype=np.float64)
    chroma_ref = librosa.feature.chroma_stft(
        S=np.abs(complex_stft).astype(np.float64), sr=sr, n_chroma=N_CHROMA,
        n_fft=N_FFT, hop_length=HOP_LENGTH, tuning=TUNING, norm=2, dtype=np.float64)
    env_ref = librosa.onset.onset_strength(
        y=y, sr=sr, n_fft=N_FFT, hop_length=HOP_LENGTH, n_mels=N_MELS, fmin=0.0,
        fmax=sr / 2, htk=False, norm="slaney", power=2.0, center=True,
        pad_mode="reflect", dtype=np.float64)

    return {
        "note": ("Ecart relatif maximal entre la transcription ci-dessus et "
                 "librosa 0.10.2. Un libroas qui change de comportement se voit "
                 "ici, pas dans un test C# sans explication."),
        "stft_power": relative(power_ours.T, power_ref),
        "mel_filterbank": relative(mel_fb, mel_fb_ref),
        "chroma_filterbank": relative(chroma_fb, chroma_fb_ref),
        "mel": relative(mel.T, mel_ref),
        "chroma": relative(chroma.T, chroma_ref),
        "onset": relative(env, env_ref),
        "tolerance": AGREEMENT_TOLERANCE,
    }


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        sys.stderr.write(__doc__ or "")
        return 2
    wav_path, out_dir = argv[1], argv[2]

    try:
        import numpy as np
        import librosa
    except ImportError as exc:  # pragma: no cover - dependance de maintenance
        fail(f"librosa et numpy sont requis ({exc}). Utiliser tools/probe/requirements.txt.")

    # Les echantillons sont lus sans passer par librosa : c'est exactement ce que
    # fait Boutap.Audio.WavDecoder, et cela evite que la reference herite d'un
    # reechantillonnage ou d'une conversion de canal propre a soundfile.
    y, sr = read_wav_mono(wav_path)
    y_ref, sr_ref = load_samples(wav_path)
    if sr != sr_ref or not np.array_equal(y.astype(np.float32), y_ref):
        fail(f"{wav_path} : lecture locale et lecture librosa different.")

    frames, n_frames = stft_frames(y, N_FFT, HOP_LENGTH)
    spectrum = np.fft.rfft(frames, axis=1)
    power = spectrum.real ** 2 + spectrum.imag ** 2
    mag = magnitude(power)

    mel_fb, mel_edges = mel_filterbank(sr, N_FFT, N_MELS, 0.0, sr / 2.0)
    mel = mel_spectrogram(power, mel_fb)

    chroma_fb = chroma_filterbank(sr, N_FFT, N_CHROMA, TUNING)
    chroma = chroma_spectrogram(mag, chroma_fb)

    env = onset_envelope(mel)

    # Les trames retenues sont celles qui portent du signal, reparties sur
    # toute la duree. Comparer des trames de silence ne prouverait rien, et
    # en choisir a la main rendrait la reference fragile face a une retouche
    # du morceau de demonstration.
    loud = [f for f in range(n_frames) if mel[f, :].max() > ROW_POWER_FLOOR]
    if len(loud) < ROW_COUNT:
        fail(f"seulement {len(loud)} trame(s) porteuse(s) de signal, "
             f"il en faut {ROW_COUNT}.")
    step = (len(loud) - 1) / (ROW_COUNT - 1)
    row_frames = [loud[round(i * step)] for i in range(ROW_COUNT)]

    drift = agreement(y, sr, mel_fb, chroma_fb, frames, mel, mag, chroma, env)
    worst = max(v for k, v in drift.items() if k not in ("note", "tolerance"))
    if not (worst <= AGREEMENT_TOLERANCE):
        bad = {k: v for k, v in drift.items() if k not in ("note", "tolerance") and v > AGREEMENT_TOLERANCE}
        fail(f"la transcription s'ecarte de librosa plus que {AGREEMENT_TOLERANCE:g} : {bad}")

    # Battement : reference informative seulement. Le test C# ne l'exige pas,
    # parce que le suivi de battement de librosa est un estimateur, pas une
    # verite, et que l morceau est synthetise a 120 BPM.
    tempo, beats = librosa.beat.beat_track(onset_envelope=env, sr=sr,
                                           hop_length=HOP_LENGTH, units="time")

    golden = {
        "schema": "boutap/test-golden/1",
        "source": {
            "file": os.path.basename(wav_path),
            "sample_rate": int(sr),
            "duration_seconds": len(y) / sr,
            "sample_count": int(len(y)),
        },
        "settings": {
            "n_fft": N_FFT,
            "hop_length": HOP_LENGTH,
            "window": "hann",
            "center": True,
            "pad_mode": "reflect",
            "n_mels": N_MELS,
            "mel_scale": "slaney",
            "mel_norm": "slaney",
            "n_chroma": N_CHROMA,
            "chroma_norm": 2,
            "tuning": TUNING,
        },
        "frames": {
            "stft_columns": int(n_frames),
            "mel_rows_checked": list(row_frames),
            "chroma_rows_checked": list(row_frames),
        },
        "mel": {
            "band_center_hz": rounded(mel_edges),
            "rows": rounded_rows(np.array([mel[f, :] for f in row_frames])),
            "digest": digest(np.array([mel[f, :] for f in row_frames])),
        },
        "chroma": {
            "rows": rounded_rows(np.array([chroma[f, :] for f in row_frames])),
            "digest": digest(np.array([chroma[f, :] for f in row_frames])),
        },
        "onset": {
            "length": int(len(env)),
            "values": rounded(env[:ENV_FRAMES]),
            "digest": digest(env),
        },
        "agreement": drift,
        "beat_reference": {
            "note": "Estimateur de librosa, non teste. Le morceau est a 120 BPM.",
            "tempo_bpm": round(float(np.atleast_1d(tempo)[0]), 6),
            "beat_count": int(len(beats)),
            "first_beats": rounded(beats[:8]),
        },
    }

    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, "analysis-golden.json")
    text = json.dumps(golden, indent=2, sort_keys=False) + "\n"
    with open(out_path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)

    sys.stdout.write(
        f"Reference ecrite : {out_path}\n"
        f"  {len(y)} echantillons a {sr} Hz, {n_frames} trames, "
        f"{len(env)} valeurs d'enveloppe\n"
        f"  ecart maximal a librosa : {worst:.2e} (tolerance {AGREEMENT_TOLERANCE:g})\n"
        f"  tempo de reference librosa : {float(np.atleast_1d(tempo)[0]):.3f} BPM\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
