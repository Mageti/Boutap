#!/usr/bin/env bash
# SPDX-FileCopyrightText: Boutap contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# La verification complete, dans les conteneurs du projet.
#
#   scripts/ci.sh          tout
#   scripts/ci.sh --fast   sans les exports (ils sont lents : ~4 Go de
#                          telechargement de gabarits et quatre executables)
#
# Cette commande doit reussir sur une machine ou rien n'est installe hormis
# Docker. C'est la definition de « la compilation est aussi facile que
# possible pour tout le monde ».

set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

fast=0
if [ "${1:-}" = "--fast" ]; then
  fast=1
  shift
fi

require_docker
ensure_caches

step() {
  echo
  echo "############################################################"
  echo "## $1"
  echo "############################################################"
}

# 1. Les verifications statiques. Elles ne demandent ni .NET ni Godot :
#    seulement bash et python3, presents dans l'image « dev » et dans
#    « export ».
step "Verifications statiques"
dev_image="$(build_image dev)"
run_in_image "$dev_image" -- bash -c '
  set -euo pipefail
  python3 scripts/check-mojibake.py
  bash scripts/check-no-wallclock.sh
  bash scripts/check-licenses.sh
  bash scripts/check-format.sh
'

# 2. Compilation et tests. -m:1 : en parallele, MSBuild se heurte lui-meme
#    sur les verrous de fichiers partages quand plusieurs projets compilent
#    dans le meme dossier de sortie.
step "Compilation et tests"
run_in_image "$dev_image" -- bash -c '
  set -euo pipefail
  dotnet restore Boutap.sln
  dotnet build Boutap.sln --configuration Release --no-restore -m:1
  dotnet test Boutap.sln --configuration Release --no-build --nologo
'

# 3. Le format ne doit pas dependre de l'ordre de fabrication.
step "Fixtures reproductibles"
run_in_image "$dev_image" -- python3 tools/make-fixtures.py --check

# 4. Tout pack du depot doit etre conforme, ou porter une attente declaree.
#    C'est exactement l'appel du job « assets » de la CI.
step "Audit des packs, profils et schemas"
run_in_image "$dev_image" -- bash -c '
  set -euo pipefail
  dotnet run --project src/Boutap.Tools --configuration Release --no-build -- audit --warn .
'

if [ "$fast" -eq 1 ]; then
  echo
  echo "Termine (exports ignores : --fast)."
  exit 0
fi

# 5. Les quatre exports. necessite l'image « export », donc Godot.
step "Exports du jeu"
export_image="$(build_image export)"
run_in_image "$export_image" -- bash -c '
  set -euo pipefail
  dotnet build game/Boutap.Shell.csproj --configuration Debug
  for preset in linux-x86_64 linux-arm64 windows-x86_64 portable-windows; do
    mkdir -p "build/$preset"
    echo "--- export $preset"
    godot --headless --path game --export-release "$preset" "../build/$preset"
  done
'

echo
echo "Tout est vert."
