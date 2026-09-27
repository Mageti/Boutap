#!/usr/bin/env bash
# SPDX-FileCopyrightText: Boutap contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Vérifie trois choses que `dotnet format` ne vérifie pas :
#   1. les fins de ligne (LF partout, sans exception) ;
#   2. l'absence de BOM ;
#   3. l'absence d'espace en fin de ligne et de tabulation en début de ligne.
#
# Les points 1 et 3 sont des causes numbered de conflits de fusion
# inutiles. Le point 2 évite un caractère invisible en tête de fichier .cs,
# que le compilateur rejette avec un message incompréhensible.
#
# Le formatage C# lui-même est fait par `dotnet format --verify-no-changes`
# dans la CI ; ce script ne dépend pas du SDK .NET et tourne partout.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# Fichiers pour lesquels CRLF resterait la convention.
#
# Vide, et c'est volontaire. Une version anterieure de ce script affirmait que
# `.editorconfig` demandait CRLF pour les `.cs` « parce que le SDK .NET et
# Visual Studio travaillent en CRLF ». C'etait faux, et l'affirmation a survecu
# a la correction de `.editorconfig` : `.editorconfig` demande `end_of_line = lf`
# pour tous les fichiers, et `.gitattributes` impose `*.cs text eol=lf`. Le
# script ajoutait donc un simple avertissement a chaque `.cs` correctement
# ecrit, ce qui a fini par etre lu comme un signal normal.
#
# Le mecanisme reste en place : si un jour un type de fichier merite vraiment
# CRLF, il s'ajoute ici, et la verification correspondante se reveille.
CRLF_GLOBS=()

status=0
checked=0

# tolère les motifs sans correspondance (comportement normal de `find`).
set +o pipefail

is_crlf_file() {
  local file="$1"
  local g
  for g in "${CRLF_GLOBS[@]}"; do
    # shellcheck disable=SC2053
    [[ "$file" == $g ]] && return 0
  done
  return 1
}

# Répertoires à élaguer, quel que soit leur profondeur. Une version
# antérieure utilisait « -not -path './bin/*' », qui n'excluait que le
# `bin/` de la racine : les twelve projets de `src/` ont chacun le leur, et
# leurs `.json` generes declenchaient une centaine de faux positif. La forme
# avec « -prune » est la seule qui tienne.
PRUNED_DIRS=(
  .git third_party build bin obj
  .cache .codenomad .tmp-tests .venv TestResults
)

# Un seul « -prune -o » pour tout le monde : « -name X -prune -o -name Y -prune
# -o ... ) -o -type f » laisserait un « -o ) » nu, que find refuse.
prune_expr=()
for d in "${PRUNED_DIRS[@]}"; do
  if [ "${#prune_expr[@]}" -gt 0 ]; then
    prune_expr+=(-o)
  fi
  prune_expr+=(-name "$d")
done

while IFS= read -r file; do
  checked=$((checked + 1))
  rel="${file#./}"

  # BOM : UTF-8 BOM = EF BB BF
  if [ "$(head -c 3 "$file" | od -An -tx1 | tr -d ' \n')" = "efbbbf" ]; then
    echo "::error file=$rel::BOM UTF-8 en tête de fichier (3 octets invisibles)"
    status=1
  fi

  # Fins de ligne
  if grep -qU $'\r' "$file"; then
    if is_crlf_file "$rel"; then
      :
    else
      echo "::error file=$rel::fins de ligne CRLF alors que ce fichier doit être en LF"
      status=1
    fi
  fi

  if ! grep -qU $'\r' "$file" && is_crlf_file "$rel"; then
    echo "::warning file=$rel::ce fichier est en LF alors qu'il est dans CRLF_GLOBS"
  fi

  # Espace en fin de ligne
  if grep -nU ' $' "$file" >/dev/null 2>&1; then
    lines=$(grep -cU ' $' "$file" || true)
    echo "::error file=$rel,line=1::$lines ligne(s) avec un espace en fin de ligne"
    status=1
  fi

  # Tabulation en début de ligne (hors makefile, absents du dépôt)
  if grep -nU $'^\t' "$file" >/dev/null 2>&1; then
    lines=$(grep -cU $'^\t' "$file" || true)
    echo "::error file=$rel,line=1::$lines ligne(s) commençant par une tabulation"
    status=1
  fi

  # Fichier sans saut de ligne final
  if [ -s "$file" ] && [ "$(tail -c 1 "$file" | wc -l)" -eq 0 ]; then
    echo "::error file=$rel::pas de saut de ligne à la fin du fichier"
    status=1
  fi
done < <(find . \( "${prune_expr[@]}" \) -prune -o -type f \
           \( -name '*.cs' -o -name '*.md' -o -name '*.json' -o -name '*.yml' \
              -o -name '*.yaml' -o -name '*.sh' -o -name '*.py' -o -name '*.cff' \
              -o -name '*.txt' -o -name '*.c' -o -name '*.h' \) -print | sort)

if [ "$checked" -eq 0 ]; then
  echo "Aucun fichier texte à vérifier."
  exit 0
fi

if [ "$status" -eq 0 ]; then
  echo "Format : $checked fichier(s) conformes."
else
  echo "--- Problèmes de format détectés ---"
fi
exit $status
