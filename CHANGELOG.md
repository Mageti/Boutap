<!--
  SPDX-FileCopyrightText: Boutap contributors
  SPDX-License-Identifier: AGPL-3.0-or-later
-->

# Changelog

Toutes les modifications notables de Boutap sont documentées ici.

Le format suit [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/), et le
projet respecte le [Semantic Versioning](https://semver.org/lang/fr/) **à partir
de la première release stable** (S5, voir `wiki: spec.md` §16).

Avant cette release, les versions sont des **jalons de slice** (`s0`, `s1`…) et
le préfixe `v` n'est pas utilisé.

## [Non publié]

### Ajouté

- **Spec complète** (`wiki: spec.md`, 21 sections) : concept, règles de jeu,
  générateur, format `.btp`, architecture, audio, latence, interface,
  accessibilité, propriété intellectuelle, licence, plan S0–S9, risques,
  prompts IA.
- Licence **AGPL-3.0-or-later** (texte intégral dans `LICENSE`).
- Gouvernance : `README.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`
  (Contributor Covenant 2.1), `SECURITY.md`, `CHANGELOG.md`, `CITATION.cff`,
  `THIRD_PARTY_ASSETS.md`.
- 11 prompts prêts à copier pour coder les slices S0 à S9 avec une IA
  (`prompts/`).
- Schémas JSON du format `.btp` (`schema/`).
- 5 profils de périphériques livrés (`profiles/`).
- Intégration continue GitHub Actions (`.github/workflows/ci.yml`) : Linux et
  Windows, format, licences, tests, vérification des 5 règles non négociables.
- Configurations d'éditeur et d'attributs de ligne (`.editorconfig`,
  `.gitattributes`), `.gitignore`, `global.json` (.NET 8).
- Scripts de vérification locale (`scripts/`) : horloge murale, format,
  licences, renommage.
- **Squelette compilable de la solution** (`Boutap.sln` a la racine) : six
  projets (`Core`, `Audio`, `Input`, `Gen`, `Game`, `Tools`) et leurs six
  projets de test, sous `src/`. `Directory.Build.props` impose `net8.0`,
  nullable, `TreatWarningsAsErrors` et la compilation deterministe.
- `Boutap.Core` : generateur pseudo-aleatoire **xorshift128+** conforme a
  l'ADR 0005, derivation de graine depuis le SHA-256 de l'audio decode,
  versions semver, hachage SHA-256, et lectures de tonalite.
- `Boutap.Audio` : horloge audio maitre (interpolation, extrapolation plafonnee
  a un bloc, resynchronisation, correction de derive, decalage utilisateur) et
  decodeur WAV **entierement manage** (PCM 8/16/24/32, IEEE 32/64,
  WAVE_FORMAT_EXTENSIBLE) definissant la forme canonique dont le manifeste
  mesure l'empreinte.
- `Boutap.Input` : conversion d'un horodatage d'evenement en temps de jeu,
  latence retenue, mesure de latence par mediane.
- `Boutap.Tools` : l'executable `boutap` (parseur de ligne de commande maison,
  zero dependance NuGet) avec les commandes `version`, `about`, `bench-ci`,
  `bench-latency` et `bench-input`.
- **194 tests** d'unite au vert.

### Décidé

- Pile : **Godot 4 (C#) + .NET 8** pour le jeu, le générateur et les outils.
  Le générateur ne dépend d'aucun Python chez l'utilisateur ; `librosa` sert
  uniquement d'oracle en recherche (`tools/probe/`, non distribué).
- Format de pack : **`.btp`**, une archive ZIP contenant `manifest.json`
  (jsonc), `charts/*.json` et l'audio. Format ouvert et documenté par schéma
  JSON Schema 2020-12.
- **Pas d'export ni d'import `.sm` / `.bms` / `.pnc`** en V1. C'est un choix
  assumé : le format est ouvert, mais convertir vers l'extérieur pose des
  questions de redistribution de partitions qu'on préfère ne pas avoir.
- **Aucune personnage ni déblocage de collection en V1.** La progression est
  statistique (paliers, étoiles, carnet).
- Audio bas niveau : **miniaudio** (CC0), un seul backend, aucune obligation de
  licence tierce.
- **macOS hors périmètre** en V1 et V2 (décision réversible, à reconsidérer à
  S9).
- **DCO plutôt que CLA** : le DCO ne prend aucun droit sur le code, ce qui est
  cohérent avec un projet AGPL.
- Le nom du projet est **Boutap**.

### Documenté

- `wiki: generateur.md` : les algorithmes du générateur avec équations et sources
  (Ellis 2007, Krumhansl-Schmuckler 1982, CENS, HPSS Fitzgerald 2010,
  GenerationMania arXiv:1806.11170, gapless LAME).
- `wiki: accessibilite.md` : 16 modes, dont la distinction entre *vérifié
  automatiquement* et *vérifié à la main le JJ/MM/AAAA*.
- `wiki: ip.md` : l'analyse juridique, la distinction méthode / expression,
  l'analyse du contentieux Konami 2005–2006, la liste des interdits absolus,
  et la procédure de retrait.
- `wiki: adr/` : les Architecture Decision Records (stack, format de pack,
  licence, plateforme, RNG, dépendance dans le temps).

### Corrigé

- `scripts/check-no-wallclock.sh`, `check-format.sh` et `check-licenses.sh`
  n'excluaient que le `bin/` de la **racine** : après un build, les douze
  projets de `src/` déclaraient chacun une centaine de faux positifs. Les trois
  scripts élaguent maintenant les répertoires de build par `-prune`.
- `scripts/check-no-wallclock.sh` calculait `is_test` sans s'en servir : le
  commentaire promettait qu'une occurrence dans un test est un avertissement,
  elle était une erreur. La branche est implémentée. Un marqueur en ligne
  `R1-allow: <raison>` désarme un motif de façon explicite et motivée, ce qui
  permet à un test de *chercher* la chaîne interdite qu'il traque.
- `scripts/check-no-wallclock.sh` autorisait `Commands/BenchLatency.cs`,
  `BenchInput.cs` et `BenchCi.cs`, qui n'existent pas : les autorisations
  portaient sur des noms de fichiers caducs. Elles pointent désormais sur les
  fichiers réels, avec une raison par fichier.
- `scripts/check-format.sh` déclarait `CRLF_GLOBS` rempli de motifs `*.cs` et
  affirmait dans un commentaire qu'`.editorconfig` demandait CRLF pour les
  sources C#. C'est faux : `.editorconfig` demande `end_of_line = lf` sans
  exception et `.gitattributes` impose `*.cs text eol=lf`. La liste est vide,
  le mécanisme reste en place pour un éventuel fichier windows, et le
  commentaire explique pourquoi il ne décrit pas la réalité.
- `scripts/check-mojibake.py` n'ignorait ni `.cache/`, ni `.codenomad/`, ni
  `.tmp-tests/`, ni `TestResults/`.
- `.gitignore` ignorait `export_presets.cfg`. Les presets d'export sont des
  entrées de build que la CI lit pour produire les quatre cibles : les
  versionner.
