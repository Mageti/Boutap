#!/usr/bin/env bash
# SPDX-FileCopyrightText: Boutap contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Renomme le projet partout, en une seule commande.
#
# Le nom « Boutap » a été choisi après coup : la spec, les ADR et les prompts
# ont été rédigés avant que le nom soit arrêté. Ce script existe pour le jour
# où le nom changera encore — et pour qu'on puisse le changer sans y passer
# une soirée.
#
# Il refait exactement ce qui a été fait à la main :
#   - les noms de fichiers (dossier wiki compris) ;
#   - les occurrences dans le texte : noms propres, identifiants, dossiers ;
#   - les noms de dossiers qui suivent le nom du projet sous src/.
#
# Il ne touche PAS à :
#   - LICENSE (le texte de la GNU ne contient aucun nom de projet) ;
#   - les fichiers de third_party/ ;
#   - .git/ (on ne réécrit pas l'historique : voir wiki: spec.md §21.2).
#
# Usage :
#   ./scripts/rename.sh --de "Boutap" --vers "NomNouveau"
#   ./scripts/rename.sh --de "Boutap" --vers "NomNouveau" --de-dossier "boutap" \
#                       --vers-dossier "nomnouveau" --dry-run
#   ./scripts/rename.sh --de "Boutap" --vers "NomNouveau" --depuis /chemin/wiki
#
# Un changement de nom est un changement d'ADR : le script affiche un rappel
# à ce sujet et ne touche pas aux fichiers d'ADR tout seul.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

OLD_NAME=""
NEW_NAME=""
OLD_DIR=""
NEW_DIR=""
WIKI_PATH=""
DRY_RUN=0

# Racine apres renommage, publiee par renommer_arbre.
RACINE_RESULTAT=""

die() {
  echo "erreur : $*" >&2
  exit 2
}

usage() {
  sed -n '2,/^$/p' "$ROOT/scripts/rename.sh" | sed 's/^# \{0,1\}//'
  exit 0
}

case "${1:-}" in
  -h|--help) usage ;;
esac

while [ $# -gt 0 ]; do
  case "$1" in
    --de)        OLD_NAME="${2:-}"; shift 2 ;;
    --vers)      NEW_NAME="${2:-}"; shift 2 ;;
    --de-dossier)  OLD_DIR="${2:-}"; shift 2 ;;
    --vers-dossier) NEW_DIR="${2:-}"; shift 2 ;;
    --depuis)    WIKI_PATH="${2:-}"; shift 2 ;;
    --dry-run)   DRY_RUN=1; shift ;;
    -h|--help)   usage ;;
    *) die "option inconnue : $1" ;;
  esac
done

[ -n "$OLD_NAME" ] || die "--de est obligatoire"
[ -n "$NEW_NAME" ] || die "--vers est obligatoire"
[ -n "$OLD_DIR" ]  || OLD_DIR="$(printf '%s' "$OLD_NAME" | tr '[:upper:]' '[:lower:]')"
[ -n "$NEW_DIR" ]  || NEW_DIR="$(printf '%s' "$NEW_NAME" | tr '[:upper:]' '[:lower:]')"

[ "$OLD_NAME" != "$NEW_NAME" ] || die "l'ancien et le nouveau nom sont identiques"
[ -n "$WIKI_PATH" ] || WIKI_PATH="${BOUTAP_WIKI_PATH:-$ROOT/../wiki/docs/idees/$OLD_DIR}"
[ -d "$WIKI_PATH" ] || die "dossier wiki introuvable : $WIKI_PATH (utilise --depuis)"

echo "Ancien nom : $OLD_NAME   (dossier : $OLD_DIR)"
echo "Nouveau nom: $NEW_NAME   (dossier : $NEW_DIR)"
echo "Dépôt      : $ROOT"
echo "Wiki       : $WIKI_PATH"
[ "$DRY_RUN" -eq 1 ] && echo ">>> Simulation : aucune écriture."
echo

# Extensions soumises au remplacement de texte.
TEXTE_EXT=(md json yml yaml cff c h cs sh py gd tres csproj sln props targets)
set +o pipefail

# ── Étape 1 : renommer les fichiers et les dossiers ────────────────────────
renommer_arbre() {
  local racine="$1"
  local parent nom_courant
  parent="$(dirname "$racine")"
  nom_courant="${racine##*/}"
  echo "== Renommage des dossiers sous $racine"

  # 1. La racine elle-même, si son nom contient l'ancien nom de dossier.
  #    Cas réel : le dossier wiki s'appelle .../idees/boutap.
  if [ -n "$nom_courant" ] && [ "${nom_courant//"$OLD_DIR"/"$NEW_DIR"}" != "$nom_courant" ]; then
    local neuf_nom="${nom_courant//"$OLD_DIR"/"$NEW_DIR"}"
    if [ "$DRY_RUN" -eq 1 ]; then
      echo "   [sim] $parent/$nom_courant  ->  $parent/$neuf_nom"
    else
      mv "$racine" "$parent/.$neuf_nom.tmp.$$"
      mv "$parent/.$neuf_nom.tmp.$$" "$parent/$neuf_nom"
      racine="$parent/$neuf_nom"
      echo "   [mv]  $parent/$nom_courant  ->  $parent/$neuf_nom"
    fi
  fi

  RACINE_RESULTAT="$racine"

  # 2. Les sous-dossiers, du plus profond au plus superficiel.
  while IFS= read -r dir; do
    [ "$dir" = "$racine" ] && continue
    local rel="${dir#"$racine"/}"
    local neuf="${rel//"$OLD_DIR"/"$NEW_DIR"}"
    if [ "$rel" != "$neuf" ]; then
      if [ "$DRY_RUN" -eq 1 ]; then
        echo "   [sim] $rel  ->  $neuf"
      else
        mv "$dir" "$racine/$neuf"
        echo "   [mv]  $rel  ->  $neuf"
      fi
    fi
  done < <(find "$racine" -mindepth 1 -type d -name "*$OLD_DIR*" -depth | sort)

  RACINE_RESULTAT="$racine"
}

# Les noms de fichiers ne contiennent pas le nom du projet, sauf cas particuliers.
renommer_fichiers() {
  local racine="$1"
  echo "== Renommage des fichiers sous $racine"
  while IFS= read -r fichier; do
    rel="${fichier#"$racine"/}"
    base="$(basename "$rel")"
    extension="${base##*.}"
    [ "$extension" = "$base" ] && extension=""
    nom_sans_ext="${base%.*}"
    [ -n "$extension" ] || nom_sans_ext="$base"
    neuf_nom="${nom_sans_ext//"$OLD_DIR"/"$NEW_DIR"}.${extension}"
    if [ "$nom_sans_ext" != "$neuf_nom" ] || [ "$base" != "$neuf_nom" ]; then
      cible="$(dirname "$racine/$rel")/$(basename "$neuf_nom")"
      if [ "$DRY_RUN" -eq 1 ]; then
        echo "   [sim] $rel  ->  ${rel#"$(dirname "$rel")"/}/$neuf_nom"
      else
        mv "$racine/$rel" "$cible"
        echo "   [mv]  $rel"
      fi
    fi
  done < <(find "$racine" -type f -name "*$OLD_DIR*" -not -path '*/.git/*' | sort)
}

renommer_fichiers "$ROOT"
renommer_arbre "$ROOT"
renommer_fichiers "$WIKI_PATH"
renommer_arbre "$WIKI_PATH"
WIKI_PATH="$RACINE_RESULTAT"

# ── Étape 2 : remplacer dans le texte ─────────────────────────────────────
# Deux formes : le nom exact (Boutap) et la forme minuscule déjà
# qui apparait dans les chemins (boutap). On ne touche pas aux formes mixtes comme
# « boutap-gen » ou « Boutap.Core » : elles sont couvertes par le remplacement
# de la forme minuscule.
remplacer_texte() {
  local racine="$1"
  echo "== Remplacement du texte sous $racine"
  local count=0
  while IFS= read -r fichier; do
    rel="${fichier#"$racine"/}"
    if grep -qF -- "$OLD_NAME" "$fichier" || grep -qF -- "$OLD_DIR" "$fichier"; then
      count=$((count + 1))
      if [ "$DRY_RUN" -eq 1 ]; then
        n=$(grep -cF -- "$OLD_NAME" "$fichier" || true)
        m=$(grep -cF -- "$OLD_DIR" "$fichier" || true)
        echo "   [sim] $rel ($n × $OLD_NAME, $m × $OLD_DIR)"
      else
        # LC_ALL=C force sed à raisonner en octets plutôt qu'en caractères :
        # le nom du projet est en ASCII, et une locale à 8 bits ferait
        # échouer sed sur les accents du reste du fichier.
        LC_ALL=C sed -i \
          -e "s|$OLD_NAME|$NEW_NAME|g" \
          -e "s|$OLD_DIR|$NEW_DIR|g" \
          "$fichier"
        echo "   [sed] $rel"
      fi
    fi
  done < <(find "$racine" -type f \
             -not -path '*/.git/*' \
             -not -path '*/third_party/*' \
             -not -path '*/build/*' \
             -not -path '*/bin/*' \
             -not -path '*/obj/*' \
             \( -name LICENSE -o -name 'LICENSE.*' \) -prune -o \
             -type f \
             \( -name '*.md' -o -name '*.json' -o -name '*.yml' -o -name '*.yaml' \
                -o -name '*.cff' -o -name '*.c' -o -name '*.h' -o -name '*.cs' \
                -o -name '*.sh' -o -name '*.py' -o -name '*.gd' -o -name '*.tres' \
                -o -name '*.csproj' -o -name '*.sln' -o -name '.editorconfig' \
                -o -name '.gitattributes' -o -name '.gitignore' \) \
             -print | sort)
  echo "   $count fichier(s) modifié(s)."
}

remplacer_texte "$ROOT"
remplacer_texte "$WIKI_PATH"

# ── Étape 3 : rappels ──────────────────────────────────────────────────────
echo
echo "Reste à faire à la main :"
cat <<'RAPPEL'
  1. Un changement de nom est un changement de décision : ajoute un ADR
     dans wiki: adr/ et mets à jour wiki: spec.md §21.
  2. Vérifie les entrées du wiki dans mkdocs.yml : le dossier a changé de nom.
  3. Mets à jour les pages de format court sur GitHub (About, Security) et les topics.
  4. Si le nom est déjà publié : dépose une nouvelle version de licence
     sur spdx.org. Un changement de nom ne dispense pas de refaire ce travail.
  5. La recherche de marque (INPI, EUIPO, WIPO) doit être refaite AVANT
     d'aller plus loin. Voir wiki: ip.md §6.
  6. L'historique git n'est pas réécrit. Si l'ancien nom apparaît dans
     d'anciens commits, c'est normal : ne lance pas git filter-repo pour
     effacer des mots, sauf raison juridique (voir wiki: ip.md §8).
RAPPEL

if [ "$DRY_RUN" -eq 1 ]; then
  echo
  echo "Simulation terminée. Relance sans --dry-run quand le résultat te convient."
else
  echo
  echo "Terminé. Lance maintenant :"
  echo "  python3 scripts/check-mojibake.py"
  echo "  bash    scripts/check-licenses.sh"
  echo "  bash    scripts/check-format.sh"
  echo "  grep -rn '$OLD_NAME' . ../wiki --exclude-dir=.git"
fi
