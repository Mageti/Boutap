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
