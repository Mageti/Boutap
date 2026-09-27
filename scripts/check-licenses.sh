#!/usr/bin/env bash
# SPDX-FileCopyrightText: Boutap contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Vérifie les en-têtes de licence SPDX des fichiers sources.
#
# Règle : tout fichier source du projet porte un en-tête SPDX. C'est ce qui
# permet à `reuse` (https://reuse.software) de certifier le dépôt, et c'est ce
# qui permet à un contributeur de savoir sans ouvrir le wiki quelle licence
# s'applique au fichier qu'il regarde.
#
# Format attendu, en tête de fichier :
#   # SPDX-FileCopyrightText: <auteur ou « Boutap contributors »
#   # SPDX-License-Identifier: AGPL-3.0-or-later
#
# Sont exemptés :
#   - third_party/   (les sources d'autrui portent leurs propres licences)
#   - LICENSE         (le texte de la licence)
#   - les fichiers sans code : markdown, json, yml, .editorconfig… Ces
#     fichiers sont couverts par THIRD_PARTY_ASSETS.md, pas par un en-tête.
#
# Aucune dépendance externe : ni `reuse`, ni Python.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

EXPECTED_ID="AGPL-3.0-or-later"
# Fichiers qui portent volontairement une autre licence, avec la raison.
# Format : chemin|identifiant|raison
EXCEPTIONS=(
  "third_party/miniaudio/miniaudio.h|CC0-1.0|vendorise tel quel, licence CC0 imposee"
)

status=0
checked=0

set +o pipefail

is_exempt() {
  local rel="$1"
  [ "$rel" = "LICENSE" ] && return 0
  case "$rel" in
    third_party/*) return 0 ;;
    *.md)           return 0 ;;
    *.json)         return 0 ;;
    *.yml|*.yaml)   return 0 ;;
    .editorconfig|.gitattributes|.gitignore) return 0 ;;
    CHANGELOG.md)   return 0 ;;
  esac
  return 1
}

exception_for() {
  local rel="$1" entry f needle reason
  for entry in "${EXCEPTIONS[@]}"; do
    f="${entry%%|*}"
    local rest="${entry#*|}"
    needle="${rest%%|*}"
    reason="${rest#*|}"
    if [ "$f" = "$rel" ]; then
      printf '%s\t%s\n' "$needle" "$reason"
      return 0
    fi
  done
  return 1
}

while IFS= read -r file; do
  rel="${file#./}"
  is_exempt "$rel" && continue
  checked=$((checked + 1))

  # Lit les 5 premières lignes ; l'en-tête doit y être.
  head -n 5 "$file" | grep -q 'SPDX-License-Identifier:' || {
    echo "::error file=$rel::pas d'en-tête SPDX-FileCopyrightText / SPDX-License-Identifier"
    status=1
    continue
  }

  found=$(head -n 5 "$file" | sed -n 's/.*SPDX-License-Identifier:[[:space:]]*\([^[:space:]]*\).*/\1/p' | head -1)
  head -n 5 "$file" | grep -q 'SPDX-FileCopyrightText:' || {
    echo "::error file=$rel::en-tête SPDX sans ligne SPDX-FileCopyrightText"
    status=1
  }

  if exc="$(exception_for "$rel")"; then
    expected="${exc%%	*}"
  else
    expected="$EXPECTED_ID"
  fi

  if [ "$found" != "$expected" ]; then
    echo "::error file=$rel::licence annoncée « $found », attendue « $expected »"
    status=1
  fi
done < <(find . -type f \
           -not -path './.git/*' \
           -not -path './third_party/*' \
           -not -path './build/*' \
           -not -path './bin/*' \
           -not -path './obj/*' \
           \( -name '*.cs' -o -name '*.c' -o -name '*.h' -o -name '*.sh' \
              -o -name '*.py' -o -name '*.gd' -o -name '*.tres' -o -name '*.csproj' \) \
         | sort)

# L'en-tête du script lui-même et celui de check-mojibake.py sont vérifiés
# ci-dessus puisqu'ils sont en .sh / .py. On vérifie aussi que le dépôt
# contient bien les deux fichiers que tout le monde oublie.
for required in LICENSE THIRD_PARTY_ASSETS.md CONTRIBUTING.md CODE_OF_CONDUCT.md SECURITY.md; do
  if [ ! -f "$required" ]; then
    echo "::error file=$required::fichier de gouvernance absent"
    status=1
  fi
done

# La licence annoncée par LICENSE doit être celle qu'on annonce partout.
if [ -f LICENSE ] && ! head -n 5 LICENSE | grep -qi 'GNU AFFERO GENERAL PUBLIC LICENSE'; then
  echo "::error file=LICENSE::le texte n'est pas celui de l'AGPL-3.0"
  status=1
fi

if [ "$checked" -eq 0 ]; then
  echo "Aucun fichier source à vérifier pour l'instant (projet encore sans code)."
  exit 0
fi

if [ "$status" -eq 0 ]; then
  echo "Licences : $checked fichier(s) avec un en-tête SPDX $EXPECTED_ID conforme."
else
  echo "--- Problèmes de licence détectés ---"
fi
exit $status
