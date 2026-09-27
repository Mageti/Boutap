<!--
  SPDX-FileCopyrightText: Boutap contributors
  SPDX-License-Identifier: AGPL-3.0-or-later
-->

# Politique de sécurité

## Versions supportées

| Version | Support sécurité | Notes |
|---|---|---|
| `main` (non taguée) | ✅ | C'est la seule ligne qui reçoit des correctifs tant qu'aucune release n'est stable. |
| Releases stables (`vX.Y.Z`) | ✅ pendant 6 mois | Voir `CHANGELOG.md`. |
| Anything antérieur | ❌ | |

Le projet est en pré-alpha. Il n'y a pas de version « stable » avant S5
(la boucle de jeu complète). Tant que le jeu ne traite aucune donnée sensible,
le risque de sécurité est essentiellement **le contenu que tu ouvres** :

| Surface de risque | Statut |
|---|---|
| Packs `.btp` tiers (ZIP + JSON) | ⚠️ Surface principale. Le lecteur décompresse et analyse des fichiers fournis par des inconnus. |
| Entrées clavier / gamepad | Faible, mais un bug de debounce peut rendre la manette inutilisable |
| Réseau | **Aucun en V1 et V2.** Pas de surface réseau à cette date. |
| `boutap audit` | Outil de défense, il lit des fichiers suspects — à lancer dans un bac à sable si le pack est douteux |

---

## Signalement

**Ne fais pas de ticket public pour une faille.**

| Canal | Usage |
|---|---|
| Issue privée / `security@boutap.invalid` | Signalement d'une faille |
| Issue publique | Bug non sensible, question, proposition |
| `wiki: ip.md` §8 | Signalement de contenu illicite (voir la section dédiée) |

Si l'adresse `security@` n'est pas configurée sur le dépôt, utilise
**GitHub Security Advisories** (onglet *Security* du dépôt) — c'est le canal
privilégié et il est privé par construction.

### Ce que contient un bon signalement

```
Titre        : [Security] Résumé en une ligne
Sévérité     : Critique / Haute / Moyenne / Basse / Info
Composant    : Boutap.Core / Boutap.Gen / Boutap.Game / lecteur .btp / CLI
Versions     : commit ou tag
Description  : ce que fait l'entrée, ce qui devrait se passer, ce qui se passe
Reproduction : étapes exactes, fichier .btp de test si disponible
Impact       : exécution de code,lecture hors du répertoire sandboxé, déni de
               service, corruption de données, fuite d'information
```

Un POC sous forme de pack `.btp` est **le bien le plus précieux** — de
préférence généré synthétiquement, jamais avec du contenu tiers.

---

## Ce que nous promettons

| Délai | Cible |
|---|---|
| Accusé de réception | 72 h |
| Première évaluation (vulnérabilité vs. non-vulnérabilité) | 7 jours |
| Correctif publié | 30 jours pour Haute et Critique, 90 jours pour Moyenne |
| Divulgation publique | Au moment du correctif, crédit au rapporteur sauf refus explicite |

Nous ne promettons pas le « zéro faille ». Un logiciel qui parse des fichiers
fournis par des inconnus **a** des failles ; le but est qu'elles soient
trouvées et corrigées, pas qu'elles n'existent pas.

---

## Recommandations en attendant

Pour l'utilisateur :

1. **N'ouvre pas un `.btp` d'origine inconnue dans une installation qui compte.**
   Un pack est une archive ZIP. Tu peux l'inspecter avant :
   ```bash
   unzip -l fichier.btp                 # liste du contenu, aucune écriture
   mkdir -p /tmp/verif && unzip -q fichier.btp -d /tmp/verif
   boutap audit /tmp/verif              # vérifie licences, hash, schéma
   ```
   Un pack contient normalement **exactement** : `manifest.json`, `charts/*.json`,
   et un ou plusieurs fichiers audio. **Rien d'autre.** Un `.exe`, un `.sh`, un
   `.dll`, un `.py`, ou un `..` dans un nom de chemin = Signalement immédiat.
2. **Vérifie la signature du checksum** si tu distribues un pack dans une
   communauté : le `sha256` est dans le `manifest.json` (§6 de la spec).
3. **Fais tourner le jeu sans privilèges.** Le sandboxer du système
   (`flatpak`, `AppArmor`, `bubblewrap`) est ton ami.

Pour le contributeur :

1. **Toute entrée non fiable est un `untrusted`.** Commentaire explicite sur
   chaque méthode qui touche un chemin venant d'un pack.
2. **Pas de ZIP-slip.** Si tu décompresses, vérifie que le chemin résolu reste
   sous le répertoire cible. C'est la faille la plus courante du genre.
3. **Pas de désérialisation de masse.** Le pack est du JSON explicite avec un
   schéma validé. Si tu introduces un désérialiseur binaire ou `TypeNameHandling`,
   tu as introduit une faille.
4. **Les bornes de nombres sont testées.** Un `chart.json` avec `d: 1e300` ou
   `k: -42` doit être **refusé**, pas tracé.

---

## Périmètre explicitement hors périmètre

| Ce n'est **pas** une faille de sécurité | Pourquoi |
|---|---|
| Détection de triche sur les scores locaux | Le jeu est local, les scores sont locaux, c'est documenté. Voir `wiki: spec.md` §9.3. |
| L'utilisateur triche sur son propre classement local | Ce n'est pas un problème de sécurité. |
| Un plantage sur un `.btp` malformé qui ne corrompt rien | C'est un bug de robustesse, pas une faille. Corrige-le, mais ne le classe pas Critique. |
| Absence de chiffrement des scores locaux | Les scores locaux ne sont pas un secret. |
| Dépendances avec une vulnérabilité non exploitable depuis un pack | Reste à traiter, mais par la mise à jour de dépendances, pas par une alerte immédiate. |

---

## Signalement de contenu illicite

Si tu trouves dans un pack ou une pull request du contenu qui enfreint les
règles de propriété intellectuelle du projet (musique non libre, partition
d'un jeu existant, marque d'autrui, asset extrait d'une borne), applique la
procédure de retrait de `wiki: ip.md` §8. Elle est distincte de la procédure de
sécurité et passe par les mainteneurs, pas par la security team.

---

## Merci

Les rapports de sécurité sont un don de temps que peu de gens font. Merci.
