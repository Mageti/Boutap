# SPDX-FileCopyrightText: Boutap contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Utilitaires partages par les scripts de compilation.
#
# Ce fichier est source, pas un point d'entree : il se source avec « . ».

set -euo pipefail

# Racine du depot, quel que soit le repertoire d'appel.
BOUTAP_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# Emplacement des caches, hors du depot.
#
# Un cache dans le depot-hook gitignore, mais un cache Docker de plusieurs
# giga-octets n'a rien a faire dans un repertoire suivi par git, et il rendrait
# `git status` illisible. Par defaut il est sous $XDG_CACHE_HOME, donc hors du
# depot sans configuration.
BOUTAP_CACHE="${BOUTAP_CACHE:-${XDG_CACHE_HOME:-$HOME/.cache}/boutap}"
BOUTAP_IMAGE_PREFIX="${BOUTAP_IMAGE_PREFIX:-boutap}"

# Nom de l'image pour une cible donnee. Une image par cible plutot qu'une
# image unique et mutable : une image de travail qui contient Godot pese 3 Go
# alors que le developpeur ne fait que compiler.
image_for() {
  printf '%s-%s' "$BOUTAP_IMAGE_PREFIX" "$1"
}

# Verifie que Docker est disponible avant de perdre trente secondes a echouer
# sur une commande incomprise.
require_docker() {
  if ! command -v docker >/dev/null 2>&1; then
    echo "Docker est introuvable dans le PATH." >&2
    echo "C'est le seul prerrequis de ces scripts (wiki: README, section Compilation)." >&2
    exit 1
  fi
  if ! docker info >/dev/null 2>&1; then
    echo "Le demon Docker ne repond pas." >&2
    echo "Sur Linux, l'utilisateur doit pouvoir parler au demon :" >&2
    echo "    sudo usermod -aG docker \"\$USER\"" >&2
    echo "puis se reconnecter." >&2
    exit 1
  fi
}

# Cree les repertoires de cache sur l'hote, avec l'utilisateur courant.
#
# C'est le script, et non le conteneur, qui les cree : un repertoire cree par
# le conteneur avec le uid de l'hote appartient deja au bon proprietaire, mais
# un repertoire absent monte par Docker serait cree par root, et le developpeur
# ne pourrait plus y ecrire ensuite.
ensure_caches() {
  mkdir -p "$BOUTAP_CACHE/home" "$BOUTAP_CACHE/nuget" "$BOUTAP_CACHE/godot"
}

# Construit une cible du Dockerfile si l'image n'existe pas deja, ou si on a
# demande une reconstruction.
build_image() {
  local target="$1"
  local image
  image="$(image_for "$target")"
  if [ "${BOUTAP_REBUILD:-0}" = "1" ] || ! docker image inspect "$image" >/dev/null 2>&1; then
    echo "==> Construction de l'image $image (cible : $target)"
    docker build --target "$target" --tag "$image" "$BOUTAP_ROOT"
  else
    echo "==> Image $image deja presente (BOUTAP_REBUILD=1 pour reconstruire)"
  fi
  printf '%s' "$image"
}

# Arguments communs a tous les « docker run », dans un tableau global.
#
# Trois choix meritent d'etre explicites :
#
#   --user $(id -u):$(id -g)
#       Le conteneur ecrit dans le depot. Sans cela, tout fichier produit par
#       une compilation appartient a root et le developpeur ne peut plus le
#       supprimer.
#
#   HOME et les caches de bibliotheques dans /cache
#       Ils pointent hors du depot. Un cache NuGet epingle dans le depot
#       ajouterait plusieurs centaines de mega-octets a chaque archive et
#       aparecerait dans « git status ».
#
#   DOTNET_CLI_TELEMETRY_OPTOUT et DOTNET_NOLOGO
#       Sans quoi la premiere commande ecrit un message de diagnostic dans la
#       sortie, ce qui casse toute comparaison de sortie dans un test.
#
# XDG_DATA_HOME n'est pas surchargé : dans l'image « export » il pointe vers
# /opt/godot/xdg, où sont les gabarits d'export, et on ne veut surtout pas
# les perdre au profit d'un repertoire de cache vide monte par l'hote.
boutap_run_args() {
  BOUTAP_RUN_ARGS=(
    run --rm
    --user "$(id -u):$(id -g)"
    --workdir /src
    --env HOME=/cache/home
    --env DOTNET_CLI_HOME=/cache/home/dotnet
    --env NUGET_PACKAGES=/cache/nuget
    --env XDG_CACHE_HOME=/cache/godot
    --env DOTNET_CLI_TELEMETRY_OPTOUT=1
    --env DOTNET_NOLOGO=1
    --env LANG=C.UTF-8
    --env TZ=UTC
    --volume "$BOUTAP_ROOT:/src"
    --volume "$BOUTAP_CACHE/home:/cache/home"
    --volume "$BOUTAP_CACHE/nuget:/cache/nuget"
    --volume "$BOUTAP_CACHE/godot:/cache/godot"
  )
}

# Lance un conteneur.
#
#   run_in_image IMAGE [options docker additionnelles...] -- [commande...]
#
# Le separateur « -- » distingue les options de docker de la commande : sans
# lui, un « --volume » de plus se retrouverait passe au programme execute.
run_in_image() {
  local image="$1"
  shift
  local docker_extra=()
  while [ "$#" -gt 0 ] && [ "$1" != "--" ]; do
    docker_extra+=("$1")
    shift
  done
  if [ "${1:-}" = "--" ]; then
    shift
  fi
  boutap_run_args
  docker "${BOUTAP_RUN_ARGS[@]}" ${docker_extra[@]+"${docker_extra[@]}"} "$image" "$@"
}
