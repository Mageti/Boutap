<!--
  SPDX-FileCopyrightText: Boutap contributors
  SPDX-License-Identifier: AGPL-3.0-or-later
-->

## Ce que fait cette PR

<!-- Une slice, une phrase. -->

Slice : <!-- S0, S1, … S9 -->

Closes #<!-- numéro d'issue, si elle existe -->

## Contrat de la slice

<!--
  Ces quatre lignes sont la partie la plus importante du formulaire. Si l'une
  des trois premières est vide, la PR sera fermée sans lecture.
-->

- **Critère DONE atteint** : <!-- colle le critère exact de wiki: spec.md §16.2, et où il se lit dans ton diff -->
- **Perf gate atteint** : <!-- le chiffre de la mesure, et le fichier de rapport -->
- **Tests ajoutés ou modifiés** : <!-- quels fichiers, quel cas couvert -->
- **Ce que cette PR ne fait pas** : <!-- le reste de la slice, et pourquoi c'est dans la PR suivante -->

## Règles de code

<!-- Les cinq règles de wiki: spec.md §7.3. Toutes doivent être respectées. -->

- [ ] R1 — aucune horloge murale (`scripts/check-no-wallclock.sh` passe)
- [ ] R2 — aucun `await` dans la boucle de jeu
- [ ] R3 — aucune allocation dans la boucle audio et de jeu
- [ ] R4 — déterminisme par construction (pas de `System.Random`, pas de `Sum()` sur des flottants dans un chemin critique)
- [ ] R5 — `Boutap.Game` ne dépend pas de `Boutap.Gen`

## Propriété intellectuelle

<!-- La question la plus importante du formulaire, et la plus oubliée. -->

- [ ] Aucun asset propriétaire : ni image, ni musique, ni police, ni fichier de jeu tiers
- [ ] La disposition 3×3 des touches est justifiée comme contrainte fonctionnelle du matériel (voir `wiki: ip.md` §3.1)
- [ ] Tout algorithme repris d'un article est réécrit, avec la référence bibliographique dans le fichier ET dans `THIRD_PARTY_ASSETS.md`
- [ ] Tout asset ajouté est inscrit dans `THIRD_PARTY_ASSETS.md` avec source, licence et SHA-256

## Documentation

La spec vit dans le wiki, **pas dans ce dépôt**.

- [ ] J'ai mis à jour la page wiki concernée dans la **même** PR, ou dans une PR liée ouverte en parallèle
- [ ] J'ai écrit ici quel fichier wiki et quelle section
- [ ] La spec fait foi : je n'ai pas contourné un critère, je l'ai atteint

Fichier wiki touché : <!-- par exemple wiki: generateur.md §4 -->

## Licence

- [ ] Tous les nouveaux fichiers portent un en-tête `SPDX-License-Identifier: AGPL-3.0-or-later`
- [ ] Chaque commit porte un DCO (`git commit -s`)

## Plateformes

Le projet promet la parité stricte Linux / Windows.

- [ ] J'ai vérifié les deux systèmes (ou j'ai dit explicitement pourquoi ce n'était pas possible)
- [ ] Aucun chemin absolu, aucun séparateur de chemin codé en dur, aucune dépendance à un chemin d'OS
- [ ] Les fichiers de test en lecture seule fonctionnent

## Vérifications avant de fusionner

```bash
dotnet build --configuration Release
dotnet test --configuration Release
bash    scripts/check-no-wallclock.sh
bash    scripts/check-licenses.sh
bash    scripts/check-format.sh
python3 scripts/check-mojibake.py
```

- [ ] Toutes passent en local
- [ ] Ou : la CI les passe (colle le lien au run)

## Dépendances

- [ ] Aucune nouvelle dépendance
- [ ] Sinon : licence compatible AGPL vérifiée, et ligne ajoutée dans `THIRD_PARTY_ASSETS.md`

## Pour la relecture

- [ ] Le diff fait moins de ~600 lignes, ou il est découpé en commits lisibles
- [ ] Le reformattage de masse est dans son propre commit, pas dans le diff de la fonctionnalité
- [ ] Les commentaires expliquent **pourquoi**, pas **quoi**
