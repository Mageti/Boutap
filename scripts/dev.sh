#!/usr/bin/env bash
# SPDX-FileCopyrightText: Boutap contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Poste de travail de compilation, dans un conteneur.
#
# Le but : que « sur mon poste » et « sur la machine de CI » soient la meme
# chose. Tout ce qui est necessaire pour compiler est dans l'image ; sur
# l'hote, il suffit d'avoir Docker.
#
#   scripts/dev.sh                       terminal interactif dans l'image
#   scripts/dev.sh dotnet test           execute une commande, puis sort
#   scripts/dev.sh --rebuild dotnet build  reconstruit l'image d'abord
#   BOUTAP_CACHE=/mnt/gros scripts/dev.sh   change l'emplacement des caches
#
# Sans argument, un terminal est ouvert : c'est l'usage principal, celui qui
# permet de taper « dotnet test » et d'iterer.

set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

rebuild=0
if [ "${1:-}" = "--rebuild" ]; then
  rebuild=1
  shift
  export BOUTAP_REBUILD=1
fi

require_container_cli
ensure_caches
image="$(build_image dev)"

if [ "$#" -eq 0 ]; then
  echo "Image : $image"
  echo "Caches : $BOUTAP_CACHE"
  # -it pour avoir un terminal ; sans terminal, un developpeur qui lance ce
  # script depuis un script verrait le conteneur mourir immediatement.
  boutap_run_args
  "$(container_cli)" "${BOUTAP_RUN_ARGS[@]}" -it "$image" /bin/bash
else
  run_in_image "$image" -- "$@"
fi
