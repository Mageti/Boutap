#!/usr/bin/env bash
# SPDX-FileCopyrightText: Boutap contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Exports du jeu, dans le conteneur « export ».
#
# Les quatre cibles sont celles que la CI construit et que mkdocs.yaml
# documente : Linux x86-64, Linux arm64, Windows x86-64, et le portable
# Windows qui embarque les donnees dans l'executable.
#
#   scripts/export.sh               les quatre
#   scripts/export.sh linux-x86_64  une seule
#   scripts/export.sh --list        la liste et rien d'autre
#
# Le projet Godot doit avoir ete compile en Debug : l'editeur ne trouve pas
# l'assembly d'un projet compile uniquement en Release. Le script le compile
# donc lui-meme avant d'exporter, pour ne pas laisser l'echec pour plus tard.

set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

# Les noms de preset viennent de game/export_presets.cfg. Les changer ici sans
# changer la-bas produirait un export qui echoue avec un message obscur.
PRESETS=(linux-x86_64 linux-arm64 windows-x86_64 portable-windows)

if [ "${1:-}" = "--list" ]; then
  printf '%s\n' "${PRESETS[@]}"
  exit 0
fi

if [ "$#" -gt 0 ]; then
  PRESETS=("$@")
fi

require_container_cli
ensure_caches
image="$(build_image export)"

# Godot refuse d'ecrire dans un dossier de sortie absent, et le message ne
# dit pas quel preset est en cause. On les cree donc tous avant.
for preset in "${PRESETS[@]}"; do
  mkdir -p "$BOUTAP_ROOT/build/$preset"
done

echo "==> Compilation Debug du projet Godot (requis par l'editeur)"
run_in_image "$image" -- dotnet build game/Boutap.Shell.csproj --configuration Debug

for preset in "${PRESETS[@]}"; do
  echo "==> Export $preset"
  # --headless : pas de serveur d'affichage dans un conteneur.
  # --quit      : sans lui, l'editeur reste ouvert apres l'export.
  # --path game : le projet Godot est dans game/, pas a la racine. La racine
  #               ne contient qu'un seul couple solution/projet, pour que
  #               « dotnet build » sans argument fonctionne.
  if ! run_in_image "$image" -- godot --headless --path game --export-release "$preset" "../build/$preset"; then
    echo "Echec de l'export $preset" >&2
    exit 1
  fi
done

echo "==> Resultats dans build/ :"
for preset in "${PRESETS[@]}"; do
  if [ -d "$BOUTAP_ROOT/build/$preset" ]; then
    du -sh "$BOUTAP_ROOT/build/$preset" | sed "s|$BOUTAP_ROOT/||"
  fi
done
