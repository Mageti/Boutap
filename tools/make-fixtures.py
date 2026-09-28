#!/usr/bin/env python3
# SPDX-FileCopyrightText: Boutap contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Fabrique les fixtures .btp du depot, de facon reproductible.

Ce script n'utilise que la bibliotheque standard. C'est volontaire : une
fixture doit pouvoir etre regeneree sur n'importe quelle machine, y compris
sans reseau et sans numpy. Si ce script dependait de numpy, un contributeur
sans la bonne version verrait ses fixtures changer sans raison.

Deux decisions meritent une explication.

**Aucune fonction transcendante.** Le signal est synthetise en arithmetique
entiere : phases, dents de scie, carres, et un petit generateur congruentiel
pour le bruit. `math.sin` est correctement arrondie sur la plupart des
machines, mais pas partout, et pas dans toutes les versions de bibliotheque.
Un octet de differance dans l'audio changerait son SHA-256, donc la graine du
generateur, donc le chart entier. Un fichier non reproductible dans ce projet
n'est pas un fichier de test, c'est une bombe a retardement.

**Stockage sans compression.** `zipfile` en `ZIP_STORED` ecrit exactement les
octets qu'on lui donne. En `ZIP_DEFLATED`, la sortie depend de la version de
zlib installee, donc `--check` echouerait sur une machine differente. La
taille du pack de demonstration (882 Ko) ne justifie pas de rendre les
fixtures irreproductibles.

Usage :
    python3 tools/make-fixtures.py            # ecrit les fixtures
    python3 tools/make-fixtures.py --check    # verifie qu'elles sont a jour
"""

from __future__ import annotations

import argparse
import filecmp
import hashlib
import io
import json
import struct
import sys
import tempfile
import wave
import zipfile
from pathlib import Path
from typing import Any

# --------------------------------------------------------------------------
# Constantes du format.Elles reprennent schema/manifest.schema.json et
# schema/chart.schema.json, qui font foi.
# --------------------------------------------------------------------------

MANIFEST_SCHEMA = "boutap/pack-manifest/1"
CHART_SCHEMA = "boutap/chart/1"
GENERATOR_NAME = "boutap-gen"
GENERATOR_VERSION = "0.1.0"
MIN_PLAYER_VERSION = "0.1.0"

# Horodatage fixe dans l'archive. Un .btp regenere doit etre le meme fichier
# octet pour octet, donc jamais « maintenant ».
ZIP_TIMESTAMP = (1980, 1, 1, 0, 0, 0)
ZIP_MODE = 0o100644 << 16

SAMPLE_RATE = 22_050
CHANNELS = 1
SAMPLE_WIDTH = 2
DEMO_SECONDS = 20
FIXTURE_SECONDS = 2
BPM = 120
BEAT = 60.0 / BPM  # 0.5 s

# Progression d'accords du morceau de demonstration, en frequences de basse.
# La mineur : La, Fa, Do, Sol.
ROOTS = (220, 175, 262, 196)


# --------------------------------------------------------------------------
# Audio
# --------------------------------------------------------------------------


class Rng:
    """xorshift 32 bits, en arithmetique entiere.

    Le meme algorithme que `Xorshift128Plus` en C#, en 32 bits par commodite.
    Il ne sert qu'a produire un bruit percussionnel reproductible : un seed
    fixe, donc le meme bruit sur toutes les machines.
    """

    def __init__(self, seed: int) -> None:
        self._state = seed & 0xFFFFFFFF or 0x9E3779B9

    def next(self) -> int:
        x = self._state
        x ^= (x << 13) & 0xFFFFFFFF
        x ^= x >> 17
        x ^= (x << 5) & 0xFFFFFFFF
        self._state = x
        return x

    def bipolar(self, amplitude: int) -> int:
        """Entier dans [-amplitude ; amplitude]."""
        return (self.next() % (2 * amplitude + 1)) - amplitude


def triangle(phase: int) -> int:
    """Dents de scie sur une phase en 12 bits, dans [-2048 ; 2048]."""
    p = phase & 0xFFF
    return (4 * p) - 2048 if p < 0x800 else (4096 - (4 * p))


def square(phase: int, duty: int) -> int:
    """Carre a la duree `duty` sur 4096."""
    return 2047 if (phase & 0xFFF) < duty else -2048


def clamp16(value: int) -> int:
    return -32768 if value < -32768 else (32767 if value > 32767 else value)


def synth(duration_seconds: float, beats: int, seed: int = 0x5EED) -> list[int]:
    """Synthetise un morceau de demonstration, en echantillons 16 bits.

    Un kick sur chaque temps, une basse triangulaire, un accord carre, et un
    bruit de caisse claire sur les contretemps. Assez pour qu'un detecteur
    d'onsets ait de quoi travailler, et assez court pour que le fichier tienne
    dans un depot.
    """
    total = int(round(duration_seconds * SAMPLE_RATE))
    beat_samples = int(round(BEAT * SAMPLE_RATE))
    samples = [0] * total
    rng = Rng(seed)

    for beat in range(beats):
        start = beat * beat_samples
        if start >= total:
            break
        root = ROOTS[(beat // 4) % len(ROOTS)]
        accent = (beat % 4) == 0

        # Basse : triangle a la fundamental, sur toute la duree du temps.
        bass_step = max(1, (root * 4096) // SAMPLE_RATE)
        pad_step = max(1, (root * 2 * 4096) // SAMPLE_RATE)
        duty = 1024 if accent else 640

        length = min(beat_samples, total - start)
        bass_phase = 0
        pad_phase = 0
        hat_length = beat_samples // 8

        for i in range(length):
            bass_phase = (bass_phase + bass_step) & 0xFFF
            pad_phase = (pad_phase + pad_step) & 0xFFF

            # Enveloppe en centiemes, donc entiere : une enveloppe en flottant
            # ferait dependre le fichier de l'arrondi de la division.
            attack = min(64, i + 1) * 100 // 64
            decay = (length - i) * 100 // length
            envelope = attack * decay // 10000
            value = (triangle(bass_phase) * 7 // 2 + square(pad_phase, duty) * 3 // 2) * envelope // 100

            index = start + i
            noise = 0
            if hat_length > 0 and i >= beat_samples // 2 and i < beat_samples // 2 + hat_length:
                # Caisse claire : bruit qui s'eteint vite, sur le contretemps.
                hat = (hat_length - (i - beat_samples // 2)) * 100 // hat_length
                noise = rng.bipolar(2000) * hat * hat // 10_000

            samples[index] = clamp16(samples[index] + value * 2 + noise)

    return samples


def wav_bytes(samples: list[int]) -> bytes:
    """Empaquette des echantillons 16 bits en WAV, en memoire."""
    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as handle:
        handle.setnchannels(CHANNELS)
        handle.setsampwidth(SAMPLE_WIDTH)
        handle.setframerate(SAMPLE_RATE)
        handle.writeframes(struct.pack(f"<{len(samples)}h", *samples))
    return buffer.getvalue()


def canonical_sha256(samples: list[int]) -> str:
    """SHA-256 de la forme canonique : flottants 32 bits entrelaces.

    C'est cette empreinte que porte `audio.sha256`, parce que c'est elle que le
    joueur decode. Le decodage 16 bits divise par 32768, division par une
    puissance de deux donc exacte : le flottant 32 bits obtenu ici est
    exactement celui qu'obtient le decodeur C#.
    """
    canonical = struct.pack(f"<{len(samples)}f", *[s / 32768.0 for s in samples])
    return hashlib.sha256(canonical).hexdigest()


# --------------------------------------------------------------------------
# Graine, telle que fixee par l'ADR 0005
# --------------------------------------------------------------------------


def seed_value(audio_sha: str, version: str, level: str | None) -> int:
    """Recalcule `generator.seed` comme le fait `SeedDerivation.DeriveCore`."""
    material = bytes.fromhex(audio_sha) + version.encode("utf-8") + b"\0"
    if level is not None:
        material += level.encode("utf-8")
    return int.from_bytes(hashlib.sha256(material).digest()[:8], "big")


# --------------------------------------------------------------------------
# Notes
# --------------------------------------------------------------------------


def make_notes(count: int, spacing: float, duration: float, wheel_every: int = 0) -> list[dict[str, Any]]:
    """Fabrique `count` notes reparties regulierement sur `duration` secondes.

    Les touches suivent la grille 3x3 en diagonale, ce qui donne une lecture
    visuelle realiste. Un huitieme des notes porte une phase decalee, un
    dixieme une fenetre elargie, et les longues sont posees sur les temps
    forts.
    """
    notes: list[dict[str, Any]] = []
    step = duration / count if count else 0.0

    for index in range(count):
        time = round(index * step, 6)
        note: dict[str, Any] = {"t": time, "k": (index * 4 + index // 3) % 9}

        if index % 4 == 0:
            note["d"] = round(min(0.5, max(0.0, duration - time)), 6)
        if index % 5 == 0:
            note["v"] = 4 + (index % 5)
        if index % 10 == 0:
            note["w"] = 1.5
        if index % 8 == 0 and index > 0:
            note["s"] = 0.125
        if wheel_every and index % wheel_every == wheel_every - 1:
            note["k"] = "WHEEL_L" if (index // wheel_every) % 2 == 0 else "WHEEL_R"

        notes.append(note)

    notes.sort(key=lambda n: n["t"])
    return notes


def peak_nps(notes: list[dict[str, Any]], window: float = 1.0) -> float:
    """Notes par seconde dans la fenetre glissante la plus dense."""
    times = sorted(n["t"] for n in notes)
    best = 0
    start = 0
    for end in range(len(times)):
        while times[end] - times[start] >= window:
            start += 1
        best = max(best, end - start + 1)
    return float(best) / window


def peak_load(notes: list[dict[str, Any]], duration: float) -> float:
    """Charge maximale sur 10, selon la formule de spec.md 5.7.2.

    Le poids d'un coup vaut 1 pour une note simple, 1.5 pour une tenue, 2 pour
    un accord, 0.8 pour un volant. La charge est la somme des poids de la
    fenetre d'une seconde la plus dense, divisee par dix. C'est une estimation
    de fatigue du joueur, pas une mesure : elle sert a comparer deux niveaux
    entre eux, pas a promettre un ressenti.
    """
    events: list[tuple[float, float]] = []
    for note in notes:
        weight = 1.5 if note.get("d") else 1.0
        if note.get("k") in ("WHEEL_L", "WHEEL_R"):
            weight = 0.8
        events.append((note["t"], weight))
        if note.get("d"):
            events.append((note["t"] + min(note["d"], 1.0), 0.0))

    best = 0.0
    for start_time, _ in events:
        total = 0.0
        for time, weight in events:
            if start_time <= time < start_time + 1.0:
                total += weight
        best = max(best, total)

    return round(min(10.0, best / 10.0), 3)


# --------------------------------------------------------------------------
# Documents du format
# --------------------------------------------------------------------------


def chart_document(audio_sha: str, level: str, notes: list[dict[str, Any]], offset: float = 0.0) -> dict[str, Any]:
    """Une chart au format du depot."""
    return {
        "schema": CHART_SCHEMA,
        "level": level,
        "audio_sha256": audio_sha,
        "offset_seconds": offset,
        "generator": {
            "name": GENERATOR_NAME,
            "version": GENERATOR_VERSION,
            "seed": seed_value(audio_sha, GENERATOR_VERSION, level),
        },
        "notes": notes,
    }


def chart_entry(level: str, notes: list[dict[str, Any]], duration: float, audio_sha: str) -> dict[str, Any]:
    """L'entree de manifeste qui decrit une chart."""
    return {
        "level": level,
        "file": f"charts/{level}.json",
        "note_count": len(notes),
        "duration_seconds": round(duration, 6),
        "nps": round(peak_nps(notes), 3),
        "peak_load": peak_load(notes, duration),
        "rating": min(20, max(1, round(peak_nps(notes) * 3.5))),
    }


def manifest_document(
    pack_id: str,
    title: str,
    audio_sha: str,
    duration: float,
    entries: list[dict[str, Any]],
    license_id: str = "CC0-1.0",
    artist: str = "",
    params: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Un manifeste au format du depot."""
    document: dict[str, Any] = {
        "schema": MANIFEST_SCHEMA,
        "id": pack_id,
        "title": title,
    }

    if artist:
        document["artist"] = artist

    document["audio"] = {
        "path": "audio.wav",
        "sha256": audio_sha,
        "duration_seconds": round(duration, 6),
        "pre_skip_seconds": 0.0,
        "gapless_metadata": "none",
    }
    document["analysis"] = {
        "duration_seconds": round(duration, 6),
        "bpm": float(BPM),
        "bpm_confidence": 1.0,
        "key": "Am",
        "key_confidence": 0.75,
    }
    document["generator"] = {
        "name": GENERATOR_NAME,
        "version": GENERATOR_VERSION,
        "params": params if params is not None else {"fixture": True},
        "seed": seed_value(audio_sha, GENERATOR_VERSION, None),
    }
    document["charts"] = entries
    document["content_license"] = license_id
    document["min_player_version"] = MIN_PLAYER_VERSION
    return document


def report_text(document: dict[str, Any]) -> str:
    """Le rapport de production, par convention de nom."""
    lines = [
        "Boutap - rapport de production",
        "",
        f"Pack      : {document['id']}",
        f"Titre     : {document['title']}",
        f"Licence   : {document['content_license']}",
        f"Audio     : {document['audio']['duration_seconds']} s, "
        f"{document['analysis'].get('bpm')} BPM, {document['analysis'].get('key')}",
        "",
    ]
    for entry in document["charts"]:
        lines.append(
            f"{entry['level']:<8} {entry['note_count']:>6} notes  "
            f"nps {entry['nps']}  charge {entry['peak_load']}/10  note {entry['rating']}/20"
        )
    lines.append("")
    lines.append("Ce fichier n'est pas declare dans le manifeste : le manifeste refuse")
    lines.append("tout champ inconnu. C'est une convention de nom, appliquee par")
    lines.append("`boutap validate` sous forme d'avertissement.")
    return "\n".join(lines) + "\n"


def license_text(identifier: str) -> str:
    """Le texte de licence embarque, par convention de nom."""
    return (
        f"{identifier}\n"
        "\n"
        "Ce pack est distribue sous la licence indiquee par content_license dans\n"
        "manifest.json. Le texte de la licence complete est celui de SPDX :\n"
        f"https://spdx.org/licenses/{identifier}.html\n"
        "\n"
        "Ce fichier n'est pas declare dans le manifeste, pour la meme raison que\n"
        "REPORT.txt : le manifeste refuse tout champ inconnu.\n"
    )


# --------------------------------------------------------------------------
# Ecriture d'archive
# --------------------------------------------------------------------------


def dumps(document: Any) -> bytes:
    """Serialise un document du format : 2 espaces, UTF-8, saut de ligne final."""
    text = json.dumps(document, ensure_ascii=False, indent=2, allow_nan=False)
    return (text + "\n").encode("utf-8")


def write_pack(path: Path, entries: list[tuple[str, bytes]]) -> None:
    """Ecrit un .btp de facon reproductible : pas de compression, horodatage fixe."""
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_STORED) as archive:
        for name, payload in entries:
            info = zipfile.ZipInfo(name, date_time=ZIP_TIMESTAMP)
            info.compress_type = zipfile.ZIP_STORED
            info.external_attr = ZIP_MODE
            info.create_system = 3
            archive.writestr(info, payload)


def pack_entries(
    manifest: dict[str, Any],
    charts: list[tuple[str, dict[str, Any]]],
    audio: bytes,
    with_report: bool = True,
    with_license: bool = True,
) -> list[tuple[str, bytes]]:
    """Assemble les entrees d'un pack, dans l'ordre impose par PackWriter."""
    entries: list[tuple[str, bytes]] = [("manifest.json", dumps(manifest)), ("audio.wav", audio)]
    for name, document in charts:
        entries.append((name, dumps(document)))
    if with_report:
        entries.append(("REPORT.txt", report_text(manifest).encode("utf-8")))
    if with_license:
        entries.append(("LICENSE.txt", license_text(manifest["content_license"]).encode("utf-8")))
    return entries


# --------------------------------------------------------------------------
# Fabrique de base
# --------------------------------------------------------------------------


def build_pack(
    pack_id: str,
    title: str,
    seconds: float,
    beats: int,
    levels: list[tuple[str, int]],
    artist: str = "",
    license_id: str = "CC0-1.0",
    with_report: bool = True,
    with_license: bool = True,
    seed: int = 0x5EED,
) -> list[tuple[str, bytes]]:
    """Fabrique un pack complet et coherent, pret a etre ecrit.

    `levels` est une liste de (nom de niveau, nombre de notes). Les notes sont
    reparties sur toute la duree, la derniere tombant juste avant la fin.
    """
    samples = synth(seconds, beats, seed=seed)
    audio = wav_bytes(samples)
    audio_sha = canonical_sha256(samples)
    duration = len(samples) / SAMPLE_RATE

    charts: list[tuple[str, dict[str, Any]]] = []
    entries: list[dict[str, Any]] = []
    for level, count in levels:
        notes = make_notes(count, duration / max(count, 1), duration)
        charts.append((f"charts/{level}.json", chart_document(audio_sha, level, notes)))
        entries.append(chart_entry(level, notes, duration, audio_sha))

    manifest = manifest_document(pack_id, title, audio_sha, duration, entries, license_id, artist)
    return pack_entries(manifest, charts, audio, with_report, with_license)


def resync(entries: list[tuple[str, bytes]]) -> list[tuple[str, bytes]]:
    """Retrie les notes et recalcule les entrees de manifeste correspondantes.

    Ajouter une note a la main sans remettre le manifeste d'accord est
    l'erreur la plus facile a commettre ici — et elle se voit immediatement,
    parce que le validateur refuse qu'une chart mente sur son propre compte.
    Cette fonction rend l'oubli impossible : on touche aux notes, on rappelle
    resync, et le pack redevient coherent.
    """
    documents: dict[str, list[dict[str, Any]]] = {}
    out: list[tuple[str, bytes]] = []
    for name, payload in entries:
        if name.startswith("charts/") and name.endswith(".json"):
            document = json.loads(payload.decode("utf-8"))
            # Le tri est stable : deux notes de meme t gardent l'ordre d'ecriture,
            # ce qui rend un accord deterministe d'une machine a l'autre.
            document["notes"] = sorted(document["notes"], key=lambda note: note["t"])
            documents[name] = document["notes"]
            out.append((name, dumps(document)))
        else:
            out.append((name, payload))

    resynced: list[tuple[str, bytes]] = []
    for name, payload in out:
        if name != "manifest.json":
            resynced.append((name, payload))
            continue

        document = json.loads(payload.decode("utf-8"))
        duration = document["audio"]["duration_seconds"]
        for entry in document["charts"]:
            notes = documents.get(entry["file"])
            if notes is None:
                continue

            entry["note_count"] = len(notes)
            entry["duration_seconds"] = round(duration, 6)
            entry["nps"] = round(peak_nps(notes), 3)
            entry["peak_load"] = peak_load(notes, duration)
            entry["rating"] = min(20, max(1, round(peak_nps(notes) * 3.5)))

        resynced.append((name, dumps(document)))

    return resynced


def mutate_manifest(entries: list[tuple[str, bytes]], change) -> list[tuple[str, bytes]]:
    """Reecrit un pack en modifiant son manifeste."""
    out: list[tuple[str, bytes]] = []
    for name, payload in entries:
        if name == "manifest.json":
            document = json.loads(payload.decode("utf-8"))
            change(document)
            out.append((name, dumps(document)))
        else:
            out.append((name, payload))
    return out


def mutate_chart(entries: list[tuple[str, bytes]], level: str, change) -> list[tuple[str, bytes]]:
    """Reecrit un pack en modifiant une de ses charts."""
    out: list[tuple[str, bytes]] = []
    for name, payload in entries:
        if name == f"charts/{level}.json":
            document = json.loads(payload.decode("utf-8"))
            change(document)
            out.append((name, dumps(document)))
        else:
            out.append((name, payload))
    return out


# --------------------------------------------------------------------------
# Les fixtures
# --------------------------------------------------------------------------


def fixtures() -> list[tuple[str, list[tuple[str, bytes]]]]:
    """Toutes les fixtures, avec leur nom de fichier.

    Le prefixe dit tout : `valid-` doit passer sans erreur, `invalid-` doit
    en produire au moins une. Le fichier `index.json` dit laquelle, et avec
    quels codes.
    """
    built: list[tuple[str, list[tuple[str, bytes]]]] = []

    # --- valides ---------------------------------------------------------

    built.append((
        "valid-minimal.btp",
        build_pack("demo-minimal", "Pack minimal", FIXTURE_SECONDS, 4, [("berceau", 4)]),
    ))

    full = build_pack(
        "demo-complet",
        "Pack complet",
        FIXTURE_SECONDS,
        4,
        [("berceau", 8), ("ronde", 16), ("cascade", 24)],
        artist="Boutap contributors",
    )
    full = mutate_manifest(full, lambda d: d.update({"artist": "Boutap contributors"}))
    built.append(("valid-complet.btp", full))

    # Deux accords simultanes, deux volants, des phases, des fenetres elargies.
    wheel = build_pack("demo-volants", "Volants et accords", FIXTURE_SECONDS, 8, [("berceau", 16)])
    wheel = mutate_chart(wheel, "berceau", lambda d: d["notes"].extend(
        [
            {"t": 0.5, "k": 4, "v": 8},
            {"t": 0.5, "k": 8, "v": 8},
            {"t": 1.0, "k": "WHEEL_L"},
            {"t": 1.5, "k": "WHEEL_R", "w": 3.0, "s": 0.375},
        ]
    ))
    built.append(("valid-volants.btp", resync(wheel)))

    built.append((
        "valid-licence-partagee.btp",
        build_pack(
            "demo-partage",
            "Licence partagee",
            FIXTURE_SECONDS,
            4,
            [("berceau", 8)],
            license_id="CC-BY-SA-4.0",
        ),
    ))

    built.append((
        "valid-sans-rapport.btp",
        build_pack("demo-sans-rapport", "Sans rapport", FIXTURE_SECONDS, 4, [("berceau", 8)], with_report=False),
    ))

    # --- invalides -------------------------------------------------------

    bad_license = build_pack("demo-licence", "Licence interdite", FIXTURE_SECONDS, 4, [("berceau", 8)])
    bad_license = mutate_manifest(bad_license, lambda d: d.update({"content_license": "MIT"}))
    bad_license = [
        (name, payload.replace(b"MIT\n", b"MIT\n", 1) if name == "LICENSE.txt" else payload)
        for name, payload in bad_license
    ]
    built.append(("invalid-licence.btp", bad_license))

    unsorted_notes = build_pack("demo-tri", "Notes non triees", FIXTURE_SECONDS, 8, [("berceau", 8)])
    unsorted_notes = mutate_chart(unsorted_notes, "berceau", lambda d: d["notes"].reverse())
    built.append(("invalid-notes-non-triees.btp", unsorted_notes))

    bad_count = build_pack("demo-compte", "Compte faux", FIXTURE_SECONDS, 8, [("berceau", 8)])
    bad_count = mutate_manifest(
        bad_count, lambda d: d["charts"][0].update({"note_count": 9})
    )
    built.append(("invalid-compte-de-notes.btp", bad_count))

    bad_hash = build_pack("demo-empreinte", "Empreinte fausse", FIXTURE_SECONDS, 4, [("berceau", 4)])
    bad_hash = mutate_manifest(
        bad_hash, lambda d: d["audio"].update({"sha256": "0" * 64})
    )
    built.append(("invalid-empreinte-audio.btp", bad_hash))

    bad_seed = build_pack("demo-graine", "Graine non derivee", FIXTURE_SECONDS, 4, [("berceau", 4)])
    bad_seed = mutate_chart(
        bad_seed, "berceau", lambda d: d["generator"].update({"seed": 1})
    )
    built.append(("invalid-graine-non-derivee.btp", bad_seed))

    unknown_field = build_pack("demo-champ", "Champ inconnu", FIXTURE_SECONDS, 4, [("berceau", 4)])
    unknown_field = mutate_manifest(unknown_field, lambda d: d.update({"difficulty": 7}))
    unknown_field = mutate_chart(
        unknown_field, "berceau", lambda d: d["notes"][0].update({"left_hand": True})
    )
    built.append(("invalid-champ-inconnu.btp", unknown_field))

    return built


# Le fichier d'index dit, pour chaque fixture, ce qu'on attend d'elle. C'est
# lui qui evite qu'un test se contente de verifier qu'une fixture est valide
# sans dire pourquoi elle devrait l'etre.
EXPECTATIONS: list[dict[str, Any]] = [
    {"file": "valid-minimal.btp", "errors": 0, "warnings": 0, "codes": []},
    {"file": "valid-complet.btp", "errors": 0, "warnings": 0, "codes": []},
    {"file": "valid-volants.btp", "errors": 0, "warnings": 0, "codes": []},
    {"file": "valid-licence-partagee.btp", "errors": 0, "warnings": 1, "codes": ["manifest.license-requires-share-alike"]},
    {"file": "valid-sans-rapport.btp", "errors": 0, "warnings": 1, "codes": ["manifest.report-missing"]},
    {"file": "invalid-licence.btp", "min_errors": 1, "codes": ["manifest.license-not-allowed"]},
    {"file": "invalid-notes-non-triees.btp", "min_errors": 1, "codes": ["chart.notes-unsorted"]},
    {"file": "invalid-compte-de-notes.btp", "min_errors": 1, "codes": ["manifest.note-count-mismatch"]},
    {"file": "invalid-empreinte-audio.btp", "min_errors": 1, "codes": ["audio.hash-mismatch"]},
    {"file": "invalid-graine-non-derivee.btp", "min_errors": 1, "codes": ["manifest.seed-not-derived"]},
    {"file": "invalid-champ-inconnu.btp", "min_errors": 2, "codes": ["manifest.unknown-field", "chart.unknown-field"]},
]


def demo_pack() -> list[tuple[str, bytes]]:
    """Le pack de demonstration : 20 s, trois niveaux, 40 notes au berceau."""
    return build_pack(
        "boutap-demo",
        "Morceau de demonstration",
        DEMO_SECONDS,
        40,
        [("berceau", 40), ("ronde", 80), ("cascade", 120)],
        artist="Boutap contributors",
    )


def repository_root() -> Path:
    """Remonte jusqu'a la racine du depot, reperee par sa solution."""
    for candidate in [Path.cwd(), *Path(__file__).resolve().parents]:
        if (candidate / "Boutap.sln").is_file():
            return candidate
    raise SystemExit("Boutap.sln est introuvable : ce script doit tourner dans le depot.")


def generate(target: Path) -> None:
    """Ecrit le pack de demonstration, les fixtures et leur index."""
    (target / "fixtures").mkdir(parents=True, exist_ok=True)

    write_pack(target / "demo.btp", demo_pack())

    for name, entries in fixtures():
        write_pack(target / "fixtures" / name, entries)

    index = {"version": 1, "expectations": EXPECTATIONS}
    (target / "fixtures" / "index.json").write_text(
        json.dumps(index, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n"
    )

    total = 1 + len(EXPECTATIONS)
    print(f"{total} pack(s) ecrit(s) dans {target}.")


def check(target: Path) -> int:
    """Verifie que les fixtures commitees sont celles que ce script produit."""
    with tempfile.TemporaryDirectory() as scratch:
        regenerated = Path(scratch)
        generate(regenerated)

        problems: list[str] = []
        # Le chemin relatif, pas le seul nom : les fixtures vivent dans un
        # sous-repertoire, et c'est lui qu'il faut comparer au depot.
        expected_files = sorted(str(p.relative_to(regenerated)) for p in regenerated.rglob("*") if p.is_file())
        actual_files = sorted(str(p.relative_to(target)) for p in target.rglob("*") if p.is_file())

        for extra in sorted(set(actual_files) - set(expected_files)):
            problems.append(f"{extra} : fichier inattendu dans tests/data.")

        for relative in expected_files:
            fresh = regenerated / relative
            committed = target / relative
            if not committed.is_file():
                problems.append(f"{relative} : absent du depot.")
            elif not filecmp.cmp(fresh, committed, shallow=False):
                problems.append(
                    f"{relative} : differe de ce que produit tools/make-fixtures.py."
                )

    if problems:
        print("Fixtures perimees :", file=sys.stderr)
        for problem in problems:
            print(f"  {problem}", file=sys.stderr)
        print("  Lancez : python3 tools/make-fixtures.py", file=sys.stderr)
        return 1

    print(f"Fixtures a jour ({len(expected_files)} fichier(s)).")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Fabrique les fixtures .btp du depot.")
    parser.add_argument(
        "--out",
        type=Path,
        default=None,
        help="Repertoire de sortie. Par defaut, tests/data du depot.",
    )
    parser.add_argument(
        "--check",
        action="store_true",
        help="Verifie que les fixtures commitees sont a jour au lieu de les reecrire.",
    )
    arguments = parser.parse_args()

    root = repository_root()
    target = arguments.out if arguments.out is not None else root / "tests" / "data"

    if arguments.check:
        return check(target)

    generate(target)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
