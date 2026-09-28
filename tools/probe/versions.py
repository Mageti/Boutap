#!/usr/bin/env python3
# SPDX-FileCopyrightText: Boutap contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Etat de l'environnement de sonde, et non resultat de mesure.

Ce script ne mesure rien. Il affiche les versions effectivement chargees, pour
que la ligne « librosa 0.10.2.post1 » d'un rapport de sonde soit verifiee par
le rapport lui-meme et pas seulement par l'intention.

Si une de ces versions ne correspond pas a tools/probe/requirements.txt, la
sonde qui suit ne sera pas comparable aux precedentes : les deux causes les
plus frequencies d'un resultat qui bouge sont une bibliotheque qui a change et
une machine qui a change, et cette page sert a les departager.
"""

from __future__ import annotations

import importlib.metadata
import platform
import sys

# Les paquets dont la version change la mesure. soundfile et soxr sont dans le
# lot parce qu'ils decodent l'audio : un decodeur different donne des
# echantillons legerement differents, donc un SHA-256 different, donc un pack
# rejete pour une raison qui n'a rien a voir avec le chart.
RELEVANT = (
    "librosa",
    "numpy",
    "scipy",
    "soundfile",
    "soxr",
    "numba",
    "llvmlite",
    "matplotlib",
)


def main() -> int:
    print("Environnement de sonde Boutap")
    print("=" * 64)
    print(f"python      {sys.version.split()[0]}")
    print(f"machine     {platform.machine()}")
    print(f"systeme     {platform.system()} {platform.release()}")
    print()

    ecarts = []
    for nom in RELEVANT:
        try:
            version = importlib.metadata.version(nom)
        except importlib.metadata.PackageNotFoundError:
            version = "(absent)"
            ecarts.append(nom)
        print(f"{nom:<12} {version}")

    if ecarts:
        print()
        print("Paquets manquants : " + ", ".join(ecarts))
        print("L'image de sonde est incomplete ; les mesures ne seront pas fiables.")
        return 1

    print()
    print("Toutes les dependances de mesure sont presentes.")
    print("Rappel : la sonde de latence (S1) n'est pas encore ecrite.")
    print("Ce qui est ecrit aujourd'hui, c'est l'infrastructure et l'oracle ;")
    print("voir le CHANGELOG pour l'etat tranche/non tranche de S1.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
