<!--
  SPDX-FileCopyrightText: Boutap contributors
  SPDX-License-Identifier: AGPL-3.0-or-later
-->

# Guide de contribution

Merci de t'intéresser à Boutap. Ce document dit **comment** contribuer sans
perdre deux semaines sur un rejet administratif.

> **Le résumé en quatre lignes**
> 1. Une PR = une slice. Jamais deux.
> 2. Lis `wiki: prompts/COMMON-CONTEXT.md` avant d'écrire la première ligne.
> 3. Tout commit porte un DCO (`git commit -s`). Sinon la CI refuse.
> 4. Aucun asset propriétaire dans le dépôt, même temporairement.

### Où sont les documents

**La spécification n'est pas dans ce dépôt : elle est dans le wiki.**

👉 **[wiki.mageti.fr/idees/boutap/](https://wiki.mageti.fr/idees/boutap/)**

| | |
|---|---|
| Ce dépôt (`/code/Boutap`) | code, schémas, profils, outils, scripts, CI, gouvernance |
| Le wiki (`idees/boutap/`) | spec, générateur, accessibilité, PI, ADR, prompts IA |

Dans toutes les pages de ce dépôt, un chemin sans préfixe désigne **ce dépôt**, et un
chemin précédé de `wiki:` désigne **le dossier wiki**. Exemple : `src/Boutap.Gen/` est
local, `wiki: generateur.md` est distant.

**La spec fait foi.** En cas de divergence entre le code et la spec, c'est la spec
qu'on corrige, jamais l'inverse, et jamais en silence.

---

## Table des matières

1. [Avant de commencer](#1-avant-de-commencer)
2. [Le contrat de la slice](#2-le-contrat-de-la-slice)
3. [Mise en place](#3-mise-en-place)
4. [Style de code](#4-style-de-code)
5. [Tests](#5-tests)
6. [Commits, DCO et signatures](#6-commits-dco-et-signatures)
7. [Pull Requests](#7-pull-requests)
8. [Propriété intellectuelle et assets](#8-propriété-intellectuelle-et-assets)
9. [Documentation](#9-documentation)
10. [Ce qui fera rejeter ta PR](#10-ce-qui-fera-rejeter-ta-pr)

---

## 1. Avant de commencer

### 1.1 Lis la spec

`wiki: spec.md` n'est pas une documentation, c'est **le contrat**. Les critères
DONE y sont binaires, chiffrés, et vérifiables. Une implémentation qui ne les
atteint pas n'est pas une contribution, c'est une divergence.

Si tu penses qu'un critère est faux, **ouvre une issue pour en discuter**. Ne
dévie pas du critère dans le code : ça casse la vérification automatique et ça
rend la base de code non fiable.

### 1.2 Choisis une slice, pas un bout de slice

Les slices (`wiki: spec.md` §16) sont ordonnées par **risque décroissant**, pas
par facilité. On attaque l'audio et le générateur en premier parce que ce sont
eux qui peuvent tuer le projet.

Une PR qui mélange « un bout de S3 » et « un bout de S5 » sera fermée sans
lecture. Le modèle de revue n'est pas designed pour ça, et le diff devient
illisible.

### 1.3 La règle des 5 non-négociables

Ces cinq règles ne se négocient pas en revue (`wiki: spec.md` §7.3). Toutes sont
vérifiées automatiquement :

| Règle | Comment c'est vérifié |
|---|---|
| Zéro horloge murale dans le chemin critique | `scripts/check-no-wallclock.sh` |
| Zéro `await` dans la boucle de jeu | test d'analyse statique |
| Zéro allocation dans la boucle audio | `GC.GetAllocatedBytesForCurrentThread()` |
| Déterminisme par construction | tests dorés (SHA-256) |
| `Boutap.Game` n'importe rien de `Boutap.Gen` | vérification de références de projet |

---

## 2. Le contrat de la slice

Chaque slice du plan a, dans `wiki: spec.md` §16.2 :

| Champ | Ce que ça veut dire |
|---|---|
| **Livrable jouable** | Ce que tu peux lancer pour voir que ça marche. Si tu ne peux pas le lancer, la slice n'est pas faite. |
| **Critère DONE** | Une liste de conditions binaires. Toutes, ou refusée. |
| **Perf gate** | Un seuil chiffré mesurable. Une feature qui marche mais saccade n'est pas faite. |

**La règle de l'arrêt :** si tu ne peux pas atteindre un critère, **arrête-toi et
ouvre une issue**. Ne livre pas 80 % en espérant que ça passe. Un 80 % non
vérifiable coûte plus cher à reprendre qu'un 0 % honestement déclaré.

---

## 3. Mise en place

### 3.1 Prérequis

| Outil | Version | Pourquoi |
|---|---|---|
| .NET SDK | 8.0+ | Tout le C# |
| Godot Engine | 4.x, build .NET | Le moteur de jeu |
| Python | 3.10+ | Uniquement pour `tools/probe` (S0) et les outils de vérification |
| Git | 2.30+ | DCO via `git commit -s` |

### 3.2 Premier lancement

```bash
git clone https://github.com/<votre-org>/boutap.git
cd boutap

# sonde de recherche (optionnelle, uniquement pour S0 et les benchmarks)
python -m venv .venv && source .venv/bin/activate
pip install -r tools/probe/requirements.txt

# build
dotnet restore
dotnet build
dotnet test
```

### 3.3 Vérifications locales avant de pousser

```bash
./scripts/check-no-wallclock.sh    # règle 1
./scripts/check-format.sh          # mise en forme
./scripts/check-licenses.sh        # en-têtes SPDX, conformité AGPL
dotnet test                        # suite complète
```

Si `check-licenses.sh` échoue, lis `wiki: ip.md` §7. Ce n'est pas un problème de
format, c'est un garde-fou juridique.

---

## 4. Style de code

### 4.1 Langues

- **Code source** : anglais (identifiants, commentaires techniques, messages
  d'erreur de la CLI). C'est la norme, et ça rend les PR avec desreonçables
  accessibles.
- **Documentation** : français. Le projet est né en français et vise un public
  francophone.
- **Interface du jeu** : les deux, avec i18n (`fr` par défaut, `en` secondaire).
- **Messages de commit** : anglais, type [Conventional Commits](https://www.conventionalcommits.org/).

### 4.2 Langage et architecture

- C# / .NET 8 pour tout : jeu, générateur, CLI. **Pas de GDScript** hors cas
  strictement justifiés (scène, shader).
- Le générateur (`Boutap.Gen`) ne référence **jamais** le jeu.
- `Boutap.Core` ne référence **jamais** `Boutap.Game` ni `Boutap.Gen`.
- Tout ce qui est dans le chemin critique (boucle audio, boucle de jeu) doit être
  **testable sans moteur**.

### 4.3 Le déterminisme

C'est la règle qui crée le plus de discussions, donc elle est explicite :

| Interdit | Autorisé |
|---|---|
| `DateTime.Now`, `Environment.TickCount` | `AudioClock.SamplePosition / SampleRate` |
| `new Random()` sans graine | `Xorshift128Plus(seed)` — réimplémentation maison |
| `HashSet<T>` / `Dictionary<K,T>` non ordonnés dans une sortie | `List<T>` trié explicitement, ou `SortedDictionary` |
| Culture/locale qui influence une sortie | Culture invariante explicite |
| Itération sur un `Dictionary` non trié | Itération sur une liste triée |

Si ton code doit produire un fichier, le SHA-256 de ce fichier doit être
identique sur toutes les machines, tous les runtimes .NET, toutes les versions
du CPU. Si ce n'est pas le cas, ce n'est pas fini.

### 4.4 Style mécanique

- 4 espaces, pas de tabs. `.editorconfig` fait autorité.
- Longueur de ligne : 100 caractères (pas de hard limit, mais respecte).
- Nommage : `PascalCase` pour les types, `camelCase` pour les locaux et les
  paramètres, `_camelCase` pour les champs privés, `SCREAMING_SNAKE` pour les
  constantes.
- Une méthode publique = une responsabilité. Si le nom contient « et », coupe
  en deux.
- Commentaires : **pourquoi**, pas **quoi**. Le code dit déjà ce qu'il fait.
  Le genre « On met le décodeur ici parce que miniaudio gère nativement le gapless
  LAME mais pas le Tag ID3v2 TXXX iTunSMPB » est utile. Le genre `// incrémente i`
  ne l'est pas.

---

## 5. Tests

### 5.1 Règle simple

> **Pas de test = pas de feature.**

Le critère DONE d'une slice inclut toujours ses tests. Un test qui n'existe pas
et un test qui échoue ont le même statut : la slice n'est pas finie.

### 5.2 Ce qui est exigé par slice

| Type de test | Obligatoire pour | Exemple |
|---|---|---|
| Unitaire | toute fonction pure, toute règle de jeu | Une fenêtre de jugement donne le bon verdict aux bornes exactes |
| Propriété | générateur, validation de schéma | Toute partition générée passe la validation |
| Golden (SHA-256) | S3, et toute sortie sérialisée | 3 runs du même MP3 donnent le même octet |
| Intégration | S1, S4, S5 | Le jeu tient 240 notes à 0 frame perdu |
| Performance | toute slice avec un perf gate | Le rapport contient les chiffres du gate, ou ça échoue |
| Non-régression | tout bug corrigé | Un test qui reproduit le bug avant le correctif |

### 5.3 Tests dorés

Pour tout ce qui produit un fichier, la procédure est :

1. Génère la sortie.
2. Enregistre le SHA-256 dans `tests/golden/`.
3. **Vérifie la stabilité sur 2 runtimes différents** (.NET 8 et .NET 9, une
   machine x64 et une arm64) avant de figer.
4. Un changement de golden doit être explicite dans la PR : *pourquoi* l'ancienne
   référence ne vaut plus rien.

Un golden modifié « pour faire passer la CI » est le pire signal possible dans
une PR. Le reviewer le verra.

### 5.4 Tests de performance

Un perf gate n'est pas une assertion xUnit classique : c'est un rapport. Le
pattern est :

```csharp
[Fact]
public void Genere_4_minutes_en_moins_de_60_secondes()
{
    var stopwatch = Stopwatch.StartNew();
    Generer(Chemin("4min.wav"));
    stopwatch.Stop();

    // Le rapport est écrit même en cas d'échec, pour debug.
    EcrireRapport("perf-gen.json", stopwatch.Elapsed, 60.0);

    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60),
        $"Génération en {stopwatch.Elapsed}, budget 60 s. Voir perf-gen.json.");
}
```

Le fichier de rapport est committé dans les artefacts de CI. On ne se fie pas
à un chiffre vert sur un runner bruyant : **le rapport contient la variance**.

---

## 6. Commits, DCO et signatures

### 6.1 DCO, pas CLA

Chaque commit doit porter une ligne `Signed-off-by:`. C'est la
[Developer Certificate of Origin](https://developercertificate.org/).

```bash
# une fois par clone
git config --local user.name  "Ton Nom"
git config --local user.email "ton@email.org"

# à chaque commit
git commit -s -m "feat(gen): implémente la sélection d'accords"
```

C'est obligatoire. La CI échoue sur un commit sans `Signed-off-by`, et le bot
`dco` la fait échouer aussi — c'est volontaire, ce n'est pas un bug.

**Pourquoi DCO et pas CLA ?** Un Contributor License Agreement transfère la
propriété du code, ce qui est contradictoire avec un projet AGPL où l'on veut
que les contributions restent libres. Le DCO ne prend aucun droit.

### 6.2 Format des messages

```
<type>(<scope>) : <sujet en imperatif, < 72 chars>

<corps : pourquoi, pas quoi. Les décisions, les alternatives écartées,
les consequences negatives connues.>

Refs: #<issue>
Signed-off-by: Ton Nom <ton@email.org>
```

| Type | Quand |
|---|---|
| `feat` | nouvelle fonctionnalité |
| `fix` | correction de bug |
| `perf` | optimisation avec gain mesuré |
| `refactor` | réorganisation **sans** changement de comportement |
| `test` | tests seuls |
| `docs` | documentation |
| `build` | build, CI, packaging |
| `chore` | le reste |

Le `perf:` sans chiffre mesuré dans le corps est un `refactor:`. On ne ment pas
sur les étiquettes.

---

## 7. Pull Requests

### 7.1 Avant d'ouvrir

- [ ] La slice concernée est **entièrement** DONE, pas partiellement
- [ ] `dotnet test` passe
- [ ] Les 4 scripts de `./scripts/` passent
- [ ] Le perf gate est mesuré et le rapport est joint
- [ ] La PR ne contient qu'une slice
- [ ] Le diff est lisible : pas de 3 000 lignes de reformatting mêlées à 200
      lignes fonctionnelles
- [ ] Chaque commit a `-s`
- [ ] Aucun binaire, aucun asset non déclaré

### 7.2 Template de description

Une bonne description de PR contient :

1. **La slice** et la sous-étape (`S3.4`)
2. **Le critère DONE** — coché, avec la preuve
3. **Le perf gate** — chiffre avant / après
4. **Ce que tu as refusé de faire** et pourquoi
5. **Le test manuel** que tu as fait (exactement ce que tu as joué, sur quelle
   machine, avec quelle manette)

Le point 4 est le plus utile et le plus souvent absent. « Je n'ai pas
implémenté l'export `.sm` parce que la spec §5.9 l'exclut en V1 » est une
phrase qui fait gagner une revue entière.

### 7.3 Cycle de revue

- **2 approbations** pour merger une slice de gameplay ou d'audio.
- **1 approbation** pour un correctif de typo, une mise à jour de doc, un test
  supplémentaire.
- La CI doit être verte. Aucune exception, y compris « ça passe chez moi ».

Si la revue prend plus de deux semaines, c'est notre problème, pas le tien.
Relance.

---

## 8. Propriété intellectuelle et assets

### 8.1 La ligne rouge

**Ce qui est interdit dans le dépôt** (`wiki: ip.md` est le texte de référence) :

| Catégorie | Exemples concrets |
|---|---|
| Marques et noms | Tout nom de marque de jeu de rythme commercial, tout nom de l'éditeur |
| Habillage visuel | Logo, personnage, illustration, police propriétaire, Capture d'écran de jeu existant |
| Musique | Tout morceau qui n'est pas libre de droits, y compris « juste 30 secondes pour tester » |
| Partitions | Toute partition de jeu tiers, même transposée, même « générée automatiquement » |
| Fichiers de jeu | Toute extraction de ROM, de disque, de borne |

### 8.2 Le test en 5 secondes

Avant d'ajouter un fichier binaire, demande-toi :

> *Si Konami voyait ce fichier dans mon dépôt, pourrait-il soutenir qu'il
> m'appartient ?*

Si tu hésites, il n'entre pas. Pas « pour l'instant », pas « juste le temps de ».

### 8.3 Les tests eux-mêmes

Un fichier de test audio est un asset comme un autre. Pour S3 (générateur), les
fixtures doivent être :
- soit des **sons synthétiques générés par le code lui-même** (cliquet, sine,
  accord) — c'est la solution par défaut, et elle est préférable ;
- soit des fichiers **sous licence libre explicite** avec l'attribution dans
  `THIRD_PARTY_ASSETS.md` ;
- soit des fichiers **dans le domaine public**, avec la source notée.

Un morceau sous CC-BY-SA est acceptable. Un morceau « trouvé sur un site de
free mp3 » ne l'est pas, même sans indication de licence.

### 8.4 Le cas particulier du pack de démo

Le pack de démonstration de S8 sera **100 % CC0**, sans exception, afin de
pouvoir être redistribué avec le jeu sans obligation. C'est une contrainte, pas
une préférence.

---

## 9. Documentation

Toute PR qui change un comportement doit changer la doc correspondante dans la
**même PR**. Pas dans une PR suivante.

| Tu changes | Tu mets à jour |
|---|---|
| Un critère DONE | `wiki: spec.md` §16.2 et le prompt correspondant dans `wiki: prompts/` |
| Un algorithme du générateur | `wiki: generateur.md` (avec l'équation et la référence) |
| Un mode d'accessibilité | `wiki: accessibilite.md` |
| Une dépendance | `wiki: spec.md` §7.5 + `THIRD_PARTY_ASSETS.md` si asset |
| Le format `.btp` | `schema/*.json` + `wiki: spec.md` §6 |

La documentation vit dans le wiki, **la spec est donc versionnée à part du code**.
Concrètement : si ton changement invalide un critère, tu ouvres une PR sur le wiki
*et* tu le signales dans la PR de code. Ne laisse jamais les deux diverger en silence
plus d'une semaine.

---

## 10. Ce qui fera rejeter ta PR

Liste non exhaustive, tirée des rejets réels :

| Motif | Comment l'éviter |
|---|---|
| Deux slices dans une PR | Relis §2 |
| Changement de stack sans ADR | Une décision de stack passe par un ADR, pas par un diff |
| Aucune mesure de performance | Le perf gate est un critère, pas un bonus |
| Golden modifié sans justification | §5.3 |
| Asset propriétaire | §8 |
| `DateTime` dans `Boutap.Core.Gameplay` | `scripts/check-no-wallclock.sh` le dit |
| Fonction « encyclopédique » de 400 lignes | Découpe. Une responsabilité par méthode. |
| Pas de test pour un chemin d'erreur | Les erreurs sont le code le plus testé, pas le moins testé |
| Reformattage de masse déguisé | Sépare le reformat dans son propre commit, jamais dans la PR de feature |
| Documentation mise à jour « après » | §9 |

---

## Voir aussi

- [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) — la charte de conduite
- [`SECURITY.md`](SECURITY.md) — signaler une faille
- **[`spec.md`](https://wiki.mageti.fr/idees/boutap/spec.md)** — la spec, dans le wiki
- **[`ip.md`](https://wiki.mageti.fr/idees/boutap/ip.md)** — le cadre juridique, dans le wiki
- **[`prompts/COMMON-CONTEXT.md`](https://wiki.mageti.fr/idees/boutap/prompts/COMMON-CONTEXT.md)** — le contexte permanent à coller à toute IA
- **[`index.md`](https://wiki.mageti.fr/idees/boutap/index.md)** — le point d'entrée du dossier wiki
