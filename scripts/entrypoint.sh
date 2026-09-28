#!/bin/sh
# SPDX-FileCopyrightText: Boutap contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Point d'entree de l'image « runtime ». Execute le programme demande au lieu
# de d'imposer l'un des quatre.

set -eu

usage() {
  cat <<'USAGE'
Usage : boutap-entrypoint <programme> [arguments...]

Programmes :
  tools   les commandes d'outillage (boutap) : version, about, audit,
          validate, show, bench-ci, bench-latency, bench-input
  gen     le generateur (boutap-gen)
  audit   raccourci de « tools audit »
  validate  raccourci de « tools validate »
  show    raccourci de « tools show »

Sans argument, la liste des commandes est affichee.
USAGE
}

case "${1:-}" in
  '')
    usage
    exit 0
    ;;
  tools)
    shift
    exec dotnet /src/boutap.dll "$@"
    ;;
  gen)
    shift
    exec dotnet /src/boutap-gen.dll "$@"
    ;;
  audit|validate|show)
    exec dotnet /src/boutap.dll "$@"
    ;;
  -h|--help|help)
    usage
    ;;
  *)
    echo "Programme inconnu : $1" >&2
    usage >&2
    exit 2
    ;;
esac
