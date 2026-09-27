<!--
  SPDX-FileCopyrightText: Boutap contributors
  SPDX-License-Identifier: AGPL-3.0-or-later
-->

# Assets et dépendances tierces

Ce document est le **registre de traçabilité** du projet. Il répond à deux
questions :

1. **Qu'est-ce qui est dans ce dépôt, et sous quelle licence ?**
2. **Qu'est-ce qui est embarqué dans le binaire distribué, et sous quelle
   licence ?**

`boutap audit` (S2, §5 de la spec) vérifie automatiquement qu'un *pack* respecte
cette table. Il ne vérifie pas le dépôt lui-même : c'est le rôle de
`scripts/check-licenses.sh` en CI.

---

## 1. Règle générale

> **Rien n'entre dans ce dépôt sans une ligne dans ce fichier.**

Y compris, et c'est là que ça coince le plus souvent :
les **fixtures de test audio**. Un `.wav` de 200 Ko dans
`tests/golden/audio/` est un asset, pas une donnée. Voir
`CONTRIBUTING.md` §8.3.

---

## 2. Assets produits par le projet

| Asset | Provenance | Licence | Embarqué |
|---|---|---|---|
| Code source (`src/`, `tests/`, `scripts/`, `.github/`) | Projet | AGPL-3.0-or-later | — |
| Documentation (dossier wiki, `README.md`, …) | Projet | AGPL-3.0-or-later **ou** CC-BY-4.0 | — |
| Schémas JSON (`schema/`) | Projet | CC0-1.0 (données de spécification) | Oui (en lecture seule) |
| Profils de périphériques (`profiles/`) | Projet | CC0-1.0 | Oui |
| Favicon / icône | À produire (S9) | CC0-1.0 | Oui |
| Pack de démonstration (`packs/demo/`) | Sources tierces, voir §4 | **CC0-1.0 obligatoire** | Optionnel (téléchargé) |
| Polices d'interface | Voir §3 | Voir §3 | Oui |

---

## 3. Polices

| Police | Licence | Raison d'être | Attribution |
|---|---|---|---|
| À déterminer (S8) | OFL-1.1 recommandée | 3 familles au choix de l'utilisateur (§11.2) | Required by OFL |

**Contraintes** :
- Trois familles au minimum, dont **une à haut taux de distinction des
  caractères** (c'est le critère de lisibilité le plus utile pour la dyslexie,
  pas « une police qui ressemble à du Comic Sans »).
- Toutes doivent couvrir **français et anglais** intégralement, y compris les
  caractères accentués combinants.
- Toutes doivent avoir une variante **à chasse fixe** pour les écrans de
  diagnostic.
- OFL-1.1 impose de joindre le fichier de licence et de ne pas vendre la police
  seule. On respectera les deux.

> **Ne dépose jamais une police système** (Arial, Calibri, Segoe) dans le
> dépôt. Les licences Microsoft interdisent la redistribution. Utilise
> Liberation, Carlito, ou les polices libres equivalentes.

---

## 4. Pack de démonstration

Cible : **8 morceaux, 100 % CC0-1.0**, dont 4 cas limites (voir spec §12.3).

| # | Cas | Critère | Source à utiliser |
|---|---|---|---|
| 1 | Référence | Percussions nettes, BPM 120, tonalité majeure | **Synthétisé par le code** (voir ligne suivante) |
| 2 | Complexe | Batterie avec cymbales, 174 BPM | Libre, CC0 |
| 3 | Sans percussion | Piano ou nappe seule | Libre, CC0 |
| 4 | Tempo élevé | ≥ 200 BPM, dense | Libre, CC0 |
| 5 | Très court | < 20 s | Libre, CC0 |
| 6 | Changements de tempo | Roulade, tempo variable | Libre, CC0 |
| 7 | Intro silencieuse | ≥ 2 s de silence en tête (teste le pré-roll) | Libre, CC0 |
| 8 | MP3 gapless | Encodé LAME avec Xing, boucle raccordée | **Synthétisé puis encodé** |

### 4.1 Source privilégiée : la synthèse

Quand c'est possible, **le morceau est généré par un script du dépôt** :
`tools/make-fixtures.py`. Un sinus, un accord, un cliquet : c'est un sinus, un
accord, un cliquet. Trois lignes de code, une licence CC0 par construction, et
aucun doute sur les droits.

C'est la solution par défaut pour les tests. Le contenu tiers n'est acceptable
que pour le pack de démonstration destiné aux joueurs, pas pour les tests.

### 4.2 Registre des sources du pack de démo

À remplir en S8. Format :

| Fichier | Origine | URL stable | Licence | Preuve |
|---|---|---|---|---|
| `demo-01.ogg` | … | … | CC0-1.0 | Lien de l'attestation |

**Règles** :
- CC0-1.0, CC-BY-4.0 ou domaine public. **Rien d'autre.**
- La colonne « Preuve » doit pointer vers une page qui survivra à la
  disparition du site (archive.org, ou la page de l'artiste).
- Un fichier trouvé sur un site de « free mp3 » **sans licence écrite sur la
  page** est du domaine public par défaut selon la loi dans de nombreux
  pays, mais **c'est un pari**, et on ne prend pas ce pari dans un dépôt public.
  Si tu hésites, ne l'inclus pas.

---

## 5. Dépendances logicielles embarquées

Ces éléments sont **livrés avec le binaire**. Voir `wiki: spec.md` §7.5 et §14.4.

| Composant | Licence | Rôle | Inclusion |
|---|---|---|---|
| Godot Engine 4 | MIT | Moteur, rendu, entrées, export | Runtime embarqué dans l'export |
| .NET 8 | MIT | Exécuteur | *self-contained* (runtime embarqué) |
| miniaudio | CC0 / domaine public | Décodage + sortie audio | Vendorisé dans `third_party/miniaudio/` |
| Codes shaders Boutap | AGPL-3.0-or-later | Rendu du plateau, effets | Source du dépôt |

### 5.1 Dépendances de développement (non embarquées)

| Composant | Licence | Rôle |
|---|---|---|
| xUnit | Apache-2.0 | Tests |
| FluentAssertions | Apache-2.0 | Assertions (à remplacer par xUnit v3 natif si la licence inquiète) |
| librosa | ISC | Sonde de recherche (S0) |
| numpy | BSD-3-Clause | Sonde de recherche |
| scipy | BSD-3-Clause | Sonde de recherche |
| Python | PSF | Sonde de recherche |
| GTK / GTKMM | LGPL-2.1 | Wrappers optionnels, hors build principal |

> `tools/probe/` n'est **jamais** distribué avec le jeu. C'est un outil de
> recherche, il vit dans le dépôt pour la reproductibilité scientifique, et il
> n'entre dans aucun artefact.

### 5.2 Notices tierces

Quand le projet produira un binaire, il devra joindre un dossier `licenses/`
contenant :

- Le texte complet de la licence de chaque composant embarqué
- Les notices d'attribution exigées (Godot MIT, .NET MIT)
- Le lien vers la source de chaque composant

C'est une obligation AGPL (§14.4) autant qu'une obligation de respect des
licences tierces. `scripts/check-licenses.sh` vérifiera sa présence dans S9.

---

## 6. Textes de référence cités

Ces textes ne sont **pas** embarqués. Ils sont cités avec leur référence
bibliographique dans `wiki: spec.md` §20.

| Référence | Auteur(s) | Année | DOI / Identifiant |
|---|---|---|---|
| Beat Tracking by Dynamic Programming | Ellis, D. P. W. | 2007 | JMNR 36(1):51–60 |
| Krumhansl-Schmuckler Profiles | Krumhansl, C. L. & Kessler, E. K. | 1982 | *Psychology of Music*, 10(2) |
| HPSS / median-filtering | Fitzgerald, D. | 2010 | AES 2010 |
| Chroma feature / CENS | Ellis, D. P. W. | 2007 | MIREX 2007 |
| Learning to Semantically Choreograph | Nishikimi, R. et al. | 2018 | arXiv:1806.11170 |
| Onset detection guide (librosa) | Böck, J. et al. | 2013 | *17th ICASSP* |
| LAME gapless encoding spec | LAME Project | — | Documentation LAME |

Les algorithmes sont réimplémentés *from scratch* en C# à partir des
**descriptions publiées**, pas copiés d'une implémentation existante. C'est
important légalement : copier l'implémentation de librosa serait une violation
de sa licence (ISC) — même si cette licence le permettrait, la convention AGPL
impose de préférer la réécriture. Et la réécriture a deux avantages concrets :
le contrôle total de l'allocation mémoire (obligatoire dans la boucle temps
réel), et la possibilité de corriger les biais que la référence document elle-même
(voir le piège des 20–60 ms avec `center=True`, spec §5.4).

---

## 7. Comment mettre à jour ce fichier

1. Tu ajoutes un asset ou une dépendance.
2. Tu ajoutes la ligne **avant** de commiter, pas après.
3. `scripts/check-licenses.sh` échouera si un nouveau fichier binaire n'est pas
   déclaré.
4. En cas de doute sur une licence, **ne l'ajoute pas**. Demande dans une issue.

Toute contribution modifiant ce fichier est lue par une personne qui n'est pas
l'auteur de la contribution. C'est le seul contrôle humain réellement
efficace sur l'IP du projet.

---

## Voir aussi

- `wiki: ip.md` — l'analyse juridique complète
- `wiki: spec.md` §12 (contenu et licences) et §14 (licence du code)
- `CONTRIBUTING.md` §8 (propriété intellectuelle et assets)
- `SECURITY.md` — surface d'attaque liée aux packs
