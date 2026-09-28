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
  zero dependance NuGet) avec les commandes `version`, `about`, `validate`,
  `show`, `audit`, `bench-ci`, `bench-latency` et `bench-input`. Chaque
  commande declare ses options **drapeau** : sans cela, `--json` avalait le nom
  du pack qui le suivait.
- **S2, format `.btp`** (`Boutap.Core/Pack/`) : modele du manifeste et de la
  chart aligne sur `schema/*.json`, lecture et ecriture d'archive, et
  **validateur** qui produit des codes d'erreur stables et un pointeur JSON.
  L'ecriture est **deterministe octet pour octet** : meme contenu, meme fichier.
- `SpdxLicenses` : liste blanche des licences de contenu (CC0-1.0, CC-BY-4.0,
  ODbL-1.0 ; CC-BY-SA-4.0 avec avertissement), `boutap audit` refuse les autres.
- `Audit` (`Boutap.Core/Audit/`) : verification des **profils de manette**,
  **contrat modele ↔ schémas** (y compris une comparaison des noms *reellement
  serialises*), et audit de l'arborescence — schemas, profils et tous les packs
  trouves. Les fixtures volontairement invalides sont declarees attendues dans
  `tests/data/fixtures/index.json` : l'audit verifie qu'elles echouent comme
  annonce au lieu de compter leurs erreurs comme les siennes.
- `tools/make-fixtures.py` (**bibliotheque standard seule**) : fabrique un WAV
  de 20 s en arithmetique entiere et 12 packs de test (5 valides, 1 sans
  rapport, 1 sous licence partagee, 6 volontairement invalides) plus
  `demo.btp`. `--check` verifie que le depot contient exactement ce que la
  fabrique produit.
- **238 tests** d'unite au vert.

### Corrigé

- **Un pack écrit par `boutap` était invalide** : `PackFormat.ChartFileName`
  oubliait l'extension `.json`, donc les charts étaient écrits sous
  `charts/berceau` et le pack se déclarait lui-même absent de lui-même.
- **L'audio d'un pack compressé n'était pas décodable** : la lecture d'en-tête
  WAV s'appuyait sur `Stream.Length`, que le flux d'une entrée ZIP compressée ne
  fournit pas. Les fixtures passaient parce que la fabrique n'utilise pas la
  compression ; les packs écrits par l'outil, si.
- **Le flux d'une entrée d'archive n'est pas seekable** : la sonde d'audio
  exigeait un `Position` lisible, ce que rien ne garantit hors d'un
  `MemoryStream`. Elle ne dépend plus que de la position courante.
- **Les propriétés calculées partaient sur le disque** : `Note.ForceOrDefault`,
  `Note.IsHold`, `Note.EndTime`, `Chart.LastNoteTime`, `Chart.NoteCount`
  étaient sérialisés. Un pack réécrit depuis le modèle était refusé par le
  validateur, et le test de contrat ne le voyait pas parce qu'il comparait des
  listes de chaînes écrites à la main au lieu des noms émis par le sérialiseur.
- **Le saut d'un chunk WAV ignoré pouvait avaler le `data`** : le compte
  d'octets déjà lus était initialisé à 16 pour tous les chunks, et un chunk
  inconnu plus court que 16 faisait disparaître les données — uniquement sur un
  flux qu'on ne peut pas repositionner, donc uniquement sur l'audio d'un pack.
- **Une sortie `--json` n'était pas exploitable** : les messages allaient sur la
  sortie standard. Ils passent maintenant sur la sortie d'erreur, qui reste le
  canal de l'humain.
- `scripts/check-format.sh` ne pardonnait pas les tabulations des fichiers
  Godot alors que `.editorconfig` les impose pour eux, et ignorait les
 extensions de projet. `check-no-wallclock.sh` signalait les tests en erreur
  alors que son commentaire promettait un avertissement, et ne connaissait pas
  les noms réels des commandes `bench-*`. `check-mojibake.py` et les trois
  scripts shell n'élaguaient que le `bin/` de la racine, pas celui de chacun des
  douze projets de `src/`.
- `.gitignore` ignorait `export_presets.cfg`, que la CI doit lire.

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
