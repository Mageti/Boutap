#!/usr/bin/env bash
# SPDX-FileCopyrightText: Boutap contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Sonde de latence et oracles Python du portage C# (S3), dans le conteneur
# « probe ».
#
#   scripts/probe.sh                 affiche l'environnement mesure
#   scripts/probe.sh <script.py>...  execute un script de tools/probe/
#   scripts/probe.sh --oracle        lance les oracles de comparaison
#
# L'environnement Python est epingle fichier par fichier dans
# tools/probe/requirements.txt. Une sonde qui derive de ses bibliotheques
# donne des nombres non reproductibles, ce qui est pire qu'une absence de
# nombre : on ne peut pas distinguer « le jeu a regresse » de « librosa a
# change ».

set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

require_container_cli
ensure_caches
image="$(build_image probe)"

# Sans argument, on affiche l'etat de l'environnement : c'est la premiere
# question a poser quand une mesure bouge, et elle a une reponse immediate.
if [ "$#" -eq 0 ]; then
  set -- /opt/probe-sources/versions.py
fi

# tools/probe est monte en lecture seule au-dessus du depot monte : le
# conteneur ne doit pas pouvoir modifier les sources de la sonde par accident,
# et le montage en lecture seule est la maniere la plus courte de l'exprimer.
run_in_image "$image" \
  --volume "$BOUTAP_ROOT/tools/probe:/opt/probe-sources:ro" \
  -- python3 "$@"
