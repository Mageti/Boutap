<!--
  SPDX-FileCopyrightText: Boutap contributors
  SPDX-License-Identifier: AGPL-3.0-or-later
-->

# Boutap

**Un jeu de rythme à neuf touches et deux volants. Tu y mets ta musique, il en sort
une partition jouable.**

> **Statut : V0.1 en construction.** Le format `.btp`, l'outillage en ligne de commande
> et la coque du jeu existent et sont testés ; le générateur et la lecture réelle
> des notes ne sont pas encore écrits. Toute la conception est dans le wiki :
> **[wiki.mageti.fr/idees/boutap/](https://wiki.mageti.fr/idees/boutap/)**
>
> Ce qui est fait, et ce qui ne l'est pas, est écrit noir sur blanc dans le
> [`CHANGELOG.md`](CHANGELOG.md). Aucun de ces deux textes ne prétend que le
> projet est plus avancé qu'il ne l'est.

---

## Le pitch en cinq lignes

Boutap est un clone de la *mécanique* des jeux de rythme à neuf boutons — 9 boutons
colorés en grille 3×3 et 2 volants, la famille des bornes japonaises. Il est
entièrement libre, sous licence AGPL-3.0 : aucune exploitation de console, aucun
compte japonais, aucun abonnement, aucune marque tierce.

Le cœur du projet n'est pas le jeu, c'est **le générateur**. Tu donnes un `.mp3`,
un `.ogg`, un `.opus` ou un `.wav` ; Boutap en extrait la structure, la tonalité, les
accords et la phase rythmique, puis il en déduit une partition jouable par une
machine — et il te dit *pourquoi* il a choisi chaque note.

Trois choses qu'il fait et que les jeux existants ne font pas :

| | |
|---|---|
| 🎵 | **N'importe quelle musique.** Aucun morceau n'est quenché à la main pour exister dans le jeu. |
| 🚫 | **Aucun échec punitif.** On ne « perd » pas une partie, on est « moins bon ». |
| 👁 | **Zéro lecture requise.** Une information = deux canaux minimum (forme + couleur + son). |

Et une promesse qu'on assume publiquement : **Boutap ne prétend pas soigner la
dyslexie.** Il s'engage sur des propriétés mesurables — jamais sur un effet thérapeutique.

---

## Ce qui est dans ce dépôt, et ce qui n'y est pas

| | Où |
|---|---|
| Code (`src/`, `native/`, `third_party/`) | ce dépôt |
| Schémas du format `.btp` (`schema/`) | ce dépôt |
| Profils de manettes livrés (`profiles/`) | ce dépôt |
| Outils et scripts (`tools/`, `scripts/`) | ce dépôt |
| Gouvernance (contribution, sécurité, code de conduite, licence) | ce dépôt |
| **Spécification complète** (≈ 2 000 lignes, 21 sections) | **le wiki** |
| Le générateur en détail (équations, références scientifiques) | **le wiki** |
| Le cahier d'accessibilité (16 modes) | **le wiki** |
| L'analyse juridique (propriété intellectuelle) | **le wiki** |
| Les ADR (décisions d'architecture) | **le wiki** |
| Les prompts prêts à copier pour coder chaque slice avec une IA | **le wiki** |

La raison est simple : **la documentation vit et meurt avec le projet.** Si elle est
dans le dépôt, elle se fige ; si elle est dans un wiki public, elle reste
discutable — et ce projet a besoin d'être discuté, parce qu'il prétend **faire**
des choses que personne n'a essayé.

> **Convention de chemins**, utilisée dans toute la documentation :
> un chemin sans préfixe désigne un fichier **de ce dépôt** ; un chemin précédé de
> `wiki:` désigne une page **du dossier wiki `idees/boutap/`**.

---

## Les sept contraintes cardinales

Non négociables, vérifiables, et testées quand il y aura du code :

| # | Contrainte | Pourquoi |
|---|---|---|
| C1 | **Zéro échec punitif.** On est « moins bon », jamais « battu ». | Un enfant de sept ans doit vouloir rejouer. |
| C2 | **Aucune lecture requise.** Le jeu est jouable sans lire. | Le canal textuel est le moins fiable. Tout double un canal visuel ou sonore. |
| C3 | **Le temps est en secondes absolues.** Jamais en mesures. | Un tempo qui dérive casse tout. |
| C4 | **L'horloge, c'est l'audio.** Ni `DateTime`, ni l'horloge murale. | Un jeu de rythme qui dérive de 10 ms est raté. |
| C5 | **Le générateur est déterministe.** Même fichier + même graine = octets identiques. | Sans ça, impossible de tester ni de faire confiance. |
| C6 | **Zéro réseau au lancement.** Aucune donnée ne sort de ta machine. | Un enfant de sept ans n'a pas besoin qu'un serveur sache qui il est. |
| C7 | **Latence bout en bout < 25 ms.** Mesurée, pas estimée. | Au-delà, on joue contre l'appareil, pas contre la musique. |

---

## Matériel visé

| Support | Précision |
|---|---|
| **Manette 9 boutons + 2 volants** | Cible principale. Les clones USB grand public en mode **clavier** fonctionnent sans pilote à écrire. |
| **Clavier AZERTY / QWERTY** | 9 touches en grille 3×3, plusieurs variantes livrées dans `profiles/`. |
| **Manette de jeu (gamepad)** | Boutons et axes mappables. |
| **Souris** | Supportée, mais déconseillée : le jeu se joue aux doigts. |

Boutap ne « supporte » aucune manette en particulier : il supporte des **profils JSON**.
Un clone de manette, un clavier, une manette de jeu — trois fichiers dans `profiles/`.

---

## Compiler Boutap

### Le chemin court : Docker, et rien d'autre

**Le seul prérequis est Docker.** Ni .NET, ni Godot, ni Python à installer. Tout
le reste est dans les images du dépôt.

```bash
git clone https://github.com/<votre-org>/boutap.git
cd boutap

./scripts/ci.sh            # tout : vérifications, compilation, tests, exports
./scripts/dev.sh            # un terminal dans le conteneur de développement
```

`./scripts/dev.sh` ouvre un shell dans lequel `dotnet test`, `dotnet build` et
`boutap` fonctionnent comme sur n'importe quel poste .NET. Sans argument, il
exécute la commande que vous lui donnez :

```bash
./scripts/dev.sh dotnet test Boutap.sln
./scripts/dev.sh dotnet run --project src/Boutap.Tools -- audit --warn .
```

| Script | Ce qu'il fait | Image |
|---|---|---|
| `./scripts/dev.sh` | Compile et teste. Terminal interactif si sans argument. | `boutap-dev` |
| `./scripts/ci.sh` | Tout vérifier, comme le ferait la CI. `--fast` saute les exports. | `dev` + `export` |
| `./scripts/export.sh` | Les quatre exécutables du jeu, dans `build/`. | `boutap-export` |
| `./scripts/probe.sh` | L'environnement Python de la sonde, puis un script de mesure. | `boutap-probe` |

Les images ne sont construites qu'une fois. Pour forcer la reconstruction :

```bash
./scripts/dev.sh --rebuild
BOUTAP_REBUILD=1 ./scripts/ci.sh
```

### Le chemin long : sans Docker

Si vous préférez compiler sur votre propre machine, il vous faut :

| Outil | Version | Pourquoi |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 8.0.4xx | Constraint par `global.json`. |
| [Godot](https://godotengine.org/download) **mono** | 4.3-stable | La version **mono**, pas la version standard : un projet Godot C# ne compile pas avec l'autre. |
| Gabarits d'export Godot **mono** | 4.3.stable.mono | `Godot_v4.3-stable_mono_export_templates.tpz`. Sans eux, aucun export. |
| Python | 3.11+ | Uniquement pour les vérifications et la fabrique de fixtures. |

```bash
dotnet restore
dotnet build --configuration Release
dotnet test Boutap.sln --configuration Release
```

La coque Godot se compile à part, parce qu'elle impose son propre SDK et que la
racine ne doit porter qu'une seule solution :

```bash
dotnet build game/Boutap.Shell.csproj --configuration Debug
godot --headless --path game --export-release linux-x86_64 ../build/linux-x86_64
```

> Sous Windows, la voie la plus simple reste WSL 2 avec Docker. La compilation
> directe sous Windows est vérifiée par la CI, mais sans elle, un conteneur est
> la seule garantie que « ça marche chez moi » veut dire la même chose partout.

### Ce que fait la vérification, et pourquoi

`./scripts/ci.sh` exécute cinq choses, dans cet ordre. Chacune peut échouer
seule, et aucune n'est un « au moins un test qui passe ».

| Étape | Ce qu'elle attrape |
|---|---|
| `check-mojibake.py` | Un caractère accentué qui a viré en CJK à la copier-coller. |
| `check-no-wallclock.sh` | Règle C4 : une décision de jeu qui lirait l'heure du système. |
| `check-licenses.sh` | Un fichier source sans en-tête SPDX, ou sous la mauvaise licence. |
| `check-format.sh` | CRLF, BOM, tabulations, espaces en fin de ligne, saut de ligne final manquant. |
| `make-fixtures.py --check` | Un fichier binaire de test modifié à la main. |
| `dotnet test` | 294 tests, dont les huit imposés par l'ADR 0005. |
| `boutap audit` | Un pack, un profil ou un schéma non conforme. |

### Les caches

Les caches (NuGet, Godot, dossiers personnels) sont écrits **hors du dépôt**,
par défaut sous `${XDG_CACHE_HOME:-$HOME/.cache}/boutap`. Ils n'apparaissent donc
jamais dans `git status` et ne sont jamais commités par accident.

```bash
BOUTAP_CACHE=/mnt/gros-disque ./scripts/ci.sh
```

Les conteneurs tournent avec votre uid, pas avec celui de `root`. Aucun fichier
que vous ne pouvez pas supprimer n'est produit dans le dépôt.

---

## Contribuer

Tout est détaillé dans [`CONTRIBUTING.md`](CONTRIBUTING.md). En résumé :

1. **Une PR = une slice.** Jamais deux.
2. **Commence par lire la spec dans le wiki** — c'est elle qui fait foi. En cas de
   divergence entre le code et la spec, c'est **la spec** qu'on corrige, jamais
   l'inverse, et jamais en silence.
3. **Aucun commit sans DCO** (`git commit -s`). Motif de rejet automatique.
4. **Aucun asset propriétaire** dans le dépôt, même temporairement, même « juste pour
   tester ». Voir [`wiki: ip.md`](https://wiki.mageti.fr/idees/boutap/ip/).

```bash
git clone https://github.com/<votre-org>/boutap.git
cd boutap
./scripts/dev.sh          # un terminal de compilation, sans rien installer
# puis lire le wiki : https://wiki.mageti.fr/idees/boutap/
```

---

## Licence

**Boutap est sous licence [AGPL-3.0-or-later](LICENSE).**

| | |
|---|---|
| ✅ | Utiliser, modifier, vendre, étudier, forker. |
| ⚠️ | Modifier et proposer **en réseau** (site, service) → il faut publier le code source. |
| ⚠️ | Distribuer un binaire → il faut fournir **le code complet**, modifications comprises. |
| ❌ | Distribuer une version modifiée en gardant le code secret. |

La section 13 a été retenue parce que le « Boutap en ligne » est explicitement envisagé
(hors V1, hors V2). Tant que rien n'est distribué, une migration vers GPL-3.0 ou MIT
reste possible — d'où le DCO plutôt qu'un CLA. Voir l'ADR 0003 dans le wiki.

**Le contenu n'est pas concerné.** Un pack de morceaux est un fichier de *données* :
il porte sa propre licence (CC0, CC-BY, domaine public…). Voir `wiki: spec.md` §12 et §14.5.

---

## Projets de référence

Si tu veux comprendre d'où viennent les choix techniques :

| Projet | Licence | Ce qu'on en tire |
|---|---|---|
| [StepMania](https://github.com/stepmania/stepmania) | MIT | Le moteur temps réel de référence du genre. Vingt ans de maturité, développement arrêté. |
| [Rhythia](https://github.com/Rhythia/Client) | AGPL-3.0 | Jeu de rythme moderne en Godot + C#, grille 3×3. La preuve que la stack fonctionne. |
| [beatoraja](https://github.com/exch-bms2/beatoraja) | GPL-3.0 | Simulateur Bemani en Java. La référence pour l'analyse audio. |
| [librosa](https://librosa.org) | ISC | La boîte à outils d'analyse audio de référence. Sert d'oracle pour le générateur. |
| [Godot Engine](https://godotengine.org) | MIT | Le moteur. |
| [miniaudio](https://github.com/mackron/miniaudio) | CC0 | Décodage et sortie audio à basse latence, zéro obligation de licence. |

---

## Contributeurs

Voir [`CONTRIBUTORS.md`](CONTRIBUTORS.md) (généré automatiquement par GitHub).

Code sous AGPL-3.0-or-later. Documentation sous licence duale AGPL-3.0-or-later /
CC-BY-4.0, au choix du lecteur.
