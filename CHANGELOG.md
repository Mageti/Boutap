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

- **Podman comme moteur de conteneurs**, pour les postes qui n'ont pas Docker.
  Les scripts de `scripts/` ne dépendent plus de `docker` en dur : ils résolvent
  le moteur présent dans le `PATH` — Docker d'abord, parce que c'est la voie
  documentée, puis Podman — et `BOUTAP_CONTAINER_CLI` permet de désigner
  explicitement un moteur, y compris un moteur compatible qui n'était pas prévu.
  Tout ce dont ces scripts ont besoin a la même syntaxe des deux côtés
  (`build --target`, `image inspect`, `run --user/--volume/--env`), donc rien
  d'autre ne bouge : les caches restent hors du dépôt, et le conteneur tourne
  toujours avec l'uid de l'utilisateur, donc aucun fichier produit n'appartient
  jamais à `root`.
- **3 tests sur la note de difficulté**, qui vérifient que le terme de rafale
  pèse enfin un quart de la note : la même charge de seize notes notée 7 sur
  20 quand elle est en rafales et 4 sur 20 quand elle est répartie, une note
  par temps à 120 BPM qui reste à 5, et neuf notes dans une seconde qui
  saturent à 16.
- **7 tests de la ligne de commande de `boutap-gen`**, sur les options
  inconnues : un `--tempo`, un `--levl` (transposition de `--level`), une option
  refusée par chacune des trois commandes, la cible de `generate` qui n'est
  acceptée que par elle, deux `--level` successifs dont le dernier gagne, et le
  tiret seul qui reste un nom de fichier et non une option.
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
- **145 tests** couvrant tout le namespace `Compose`, qui n'en avait aucun :
  pliage des hauteurs, accords, quantification, courbe d'effort, lisibilité
  (règles L1 à L10, avec les seuils de part et d'autre et l'ordre des règles),
  simulation de joueur, humanisation, nettoyage, et la chaîne complète jusqu'au
  pack. Dont la promesse centrale du format, vérifiée : **deux générations
  successives du même audio produisent le même pack, octet pour octet**,
  horodatages d'archive ZIP compris, et le contre-test qui prouve que le
  test précédent ne prouve rien tant qu'un changement d'empreinte ne change
  pas le résultat.
- **10 tests** sur la graine imposée par `--seed` : la graine du pack et des
  trois niveaux vient du matériau et non de l'empreinte, le matériau inscrit
  est relu par le validateur, deux matériaux identiques donnent le même pack
  octet pour octet, l'empreinte de l'audio ne bouge pas, un pack à graine
  imposée passe la validation de bout en bout, et un matériau forgé ou invalide
  est refusé puis retombe sur l'empreinte.
- **38 tests** de la ligne de commande de `boutap-gen`, le dernier namespace
  sans tests. Le programme entier tient dans `GenProgram.Run(args, sortie,
  erreur)`, les deux flux étant injectables : la ligne de commande se teste
  sans lancer de processus, et sans rendre une classe interne visible aux
  tests. Usage, options, codes de sortie, les trois commandes sur le vrai
  audio du pack de démonstration, sortie JSON, et le pack écrit relu par le
  validateur. Le projet de test référence donc `Boutap.Gen.Cli`.
- **535 tests** d'unité au vert.

### Corrigé

- **`make-fixtures.py --check` échouait sur un fichier qui n'est pas le sien.**
  `tests/data/goldens/analysis-golden.json`, produit par `tools/make-goldens.py`,
  était signalé comme « fichier inattendu dans tests/data », et l'étape 3 de
  `scripts/ci.sh` s'arrêtait là : la vérification était rouge avant même les
  exports, partout, y compris en CI. Les deux outils sont volontairement
  distincts — `make-goldens.py` exige librosa et numpy, et son propre
  docstring refuse d'être un script de CI — mais `make-fixtures.py` ne
  connaissait que ses propres fichiers et voyait donc un intrus dans tout ce
  qu'un autre fabrique écrit sous `tests/data`. Il ignore désormais le
  sous-répertoire d'un autre fabrique. La garde reste entière sur son
  domaine : un fichier parasite dans `tests/data` échoue toujours, et une
  fixture modifiée à la main aussi.
- **La note de difficulté ignorait presque ses rafales.** Le terme pèse 25 %
  du score, mais il comptait les notes dans une fenêtre de 100 ms, que la règle
  de lisibilité L5 borne à quatre notes, et il divisait par 32 : il plafonnait
  donc à 3,1 % de la note finale. Sur le morceau de démonstration il en pesait
  0,8 %. La fenêtre passe à une seconde et le diviseur à 8, ce qui rend le
  terme mesurable ; la note du morceau de démonstration passe de 4 à 5 sur 20.
  **Une note de difficulté n'est pas comparable à celles des packs écrits
  avant cette correction.**
- **Une option inconnue était ignorée en silence.** Tout argument commençant par
  un tiret qui n'était pas reconnu tombait à la fin de la boucle de lecture :
  `generate piste.wav -o p.btp --tempo 100` sortait 0 en ignorant `--tempo`, et
  rien dans la sortie ne permettait de le remarquer. Une option inconnue est
  presque toujours une faute de frappe, et une faute de frappe doit se voir :
  elle est maintenant refusée avec le code 2, en nommant l'option et en
  renvoyant vers `--help`. Les options propres à une commande sont déclarées
  par elle, ce qui laisse `-o` et `--output` à `generate` seul.
- **La note de difficulté calculait une charge qu'elle n'utilisait pas.** Le
  facteur « charge » était calculé puis jeté. Il ne manquait pas de terme : les
  quatre poids de la formule font bien 1, et la charge est déjà publiée telle
  quelle dans le champ `peak_load` du manifeste, où on peut la lire. Le calcul
  inutile a été supprimé.

- **`--seed` écrivait la graine dans l'empreinte de l'audio.** L'option passait
  sa valeur là où le pipeline attendait le SHA-256 du contenu décodé : le pack
  portait donc `audio.sha256` et `chart.audio_sha256` égaux à la graine, et
  sortait en erreur `audio.hash-mismatch` à la validation. Le paramètre faisait
  double emploi — provenance *et* matériau de dérivation des graines — il est
  maintenant séparé, et le matériau imposé est inscrit dans
  `generator.params.seed_material`. Le validateur le relit et le redérive : la
  règle de dérivation reste vivante au lieu de devenir inerte, et aucun champ
  du manifeste n'a eu à changer, donc pas de version de format à monter.
- **`levels --json` sortait le tableau lisible par un humain.** La sortie JSON
  existait, elle n'était appelée nulle part, et la commande ne lisait même pas
  ses arguments. L'option était annoncée dans l'aide commune.
- **`--level` suivi d'un nom inconnu tuait le programme.** L'analyse de niveau
  lève une `FormatException`, qui n'hérite pas de l'`ArgumentException` que le
  programme rattrape : on obtenait la pile d'appels sur l'écran et le code de
  sortie 134. Le message, lui, était déjà le bon ; c'est sa traduction en
  erreur d'usage qui manquait.
- **`--level` et `--seed` sans valeur étaient ignorés en silence.** Un
  `--level` nu produisait les trois niveaux et sortait 0 ; un `--seed` nu
  laissait la graine tirée au sort. Une option qui attend une valeur et n'en a
  pas est une faute de frappe, pas une option inconnue.
- **L'identifiant du pack dépendait du dossier de travail.** Quand le nom du
  fichier ne laissait pas trois caractères lisibles, le repli prenait une
  empreinte du *chemin absolu*. Le même fichier placé ailleurs, ou sur une
  autre machine, produisait donc un autre identifiant, donc un autre pack à
  contenu égal — ce qu'interdit la règle R1 et ce que promettait le commentaire
  de la classe elle-même. Le repli porte maintenant sur le nom de fichier.
- **Le score local de battement valait exactement zéro.** La formule de la
  spec (`generateur.md` §4.7.2), `somme des bandes moins leur moyenne`, se
  soustrait à chaque bande la moyenne de ces mêmes bandes : elle vaut zéro pour
  toute entrée, en arithmétique réelle. Le code ne renvoyait donc que du bruit
  d'arrondi flottant — de l'ordre de 5e-14 sur une enveloppe montant à 70 — et
  c'est dans ce bruit que la meilleure périodicité du morceau était trouvée.
  Trois des six tests de `BeatTrackerTests` passaient à vide, c'est-à-dire sur
  un `BeatTrack.Empty`. Le score local est remplacé par celui d'Ellis, celui de
  `librosa.beat.__beat_local_score` : les bandes sont réduites à une enveloppe
  d'onsets à une valeur par trame, normalisée par son écart-type
  d'échantillon, puis convoluée par une cloche causale large d'un temps,
  recalculée pour chaque hypothèse de tempo comme le fait librosa.
- **La remontee de la grille ajoutait deux temps fantômes.** `Backtrace`
  partait de la dernière trame sans condition — la variable `best`, argument
  du maximum du score cumulé, était calculée puis jamais utilisée — et
  remontait jusqu'à la trame zéro en l'ajoutant toujours. Le tempo final vaut
  `60 × (n − 1) / étendue`, donc une période entière en trop le sous-estimait.
  Deux fonctions de librosa manquent à l'appel et sont désormais portées :
  `__last_beat` (écarter la queue) et `__trim_beats` (écarter la tête et la
  queue sans signal). Sur le morceau de démonstration, la grille passe de 40
  temps mal placés à 38, exactement ceux que librosa retrouve sur le même
  fichier, et le tempo de 117,0 à **119,96 BPM pour un morceau réellement à
  120 BPM** : le risque R2 est levé sur ce point, et l'outil `boutap-gen
  analyse` n'annonce plus 117.
- **Le gabarit majeur de Krumhansl-Kessler était corrompu.** Les indices 2 et 3
  portaient 4.38 et 5.38 au lieu de 3.48 et 2.33 ; 5.38 est la sous-dominante du
  gabarit *mineur*, contamination croisée des deux tableaux. Le chroma de La
  majeur était attribué à Ré bémol, avec 0,43 de score contre 0,73. Les deux
  gabarits sont ceux de 1982, complets et sans retouche ; c'est la spec qui est
  décalée, puisqu'elle place 4.38 sur la médiante et 5.38 sur la dominante. La
  régression est couverte par un test sur le chroma de La majeur.
- **La première note d'une partition commençant par un tenu disparaissait.**
  Dans la règle L7 de `ReadabilityFilter.Reject`, la condition
  `index == 0 || Math.Abs(hold.Time - note.Time) > 1e-9` mettait la toute
  première note contre son propre début de tenue : l'exemption destinée à
  protéger la première note — celle qui n'a rien devant elle — était justement
  ce qui la faisait tomber. L'inégalité est inversée, il fallait `index > 0`.
  Une partition réduite à un unique tenu ressortait vide. Le générateur
  n'émettant que des frappes, la perte ne l'atteignait pas, mais `Smooth` est
  une API publique de la bibliothèque et toute partition importée ou écrite à la
  main s'en trouvait amputée.
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
- **`--seed` écrivait la graine dans le champ d'empreinte.**
  `boutap-gen generate --seed <64 hex>` faisait passer la graine dans le
  paramètre `audioSha256Hex` de `Pipeline.Run`, qui portait deux noms pour une
  seule valeur : l'empreinte de provenance et le matériau de dérivation des
  graines. Le pack produit était rejeté sur `audio.hash-mismatch` — l'empreinte
  ne désignait plus l'audio. Corriger le seul appel n'aurait fait que déplacer
  l'erreur, car le validateur redérive la graine depuis `audio.sha256` et aurait
  alors refusé le pack sur `manifest.seed-not-derived`.
  La formule de graine a deux entrées, pas une : `generator.params.seed_material`
  les distingue. Le matériau vaut l'empreinte de l'audio décodé par défaut, et
  `--seed` le remplace pour demander une variante du même morceau ; le champ
  n'est écrit que lorsqu'il a été imposé. Le validateur relit le champ et
  redérive, donc la règle de dérivation reste vivante : un `seed_material`
  forgé est toujours refusé, sur les trois niveaux. Aucune version de schéma
  n'a bougé, `params` étant un objet libre, et le pack produit par défaut
  reste identique octet pour octet.

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
