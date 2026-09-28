# SPDX-FileCopyrightText: Boutap contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Boutap — images de compilation.
#
# Une seule image, plusieurs cibles. L'image « dev » est le poste de travail ;
# « export » ajoute l'editeur Godot ; « probe » est la sonde audio et l'oracle
# Python du portage C#. Aucun de ces conteneurs n'a besoin d'une installation
# de dependances sur la machine hote : le seul prerrequis est Docker.
#
# Les images sont lancees par scripts/*.sh, qui montent le depot sur /src et des
# repertoires de cache hors de /src. Les repertoires de cache sont crees sur
# l'hote par le script et appartiennent a l'utilisateur qui lance le script, le
# conteneur tourne avec le meme uid, donc aucun fichier du depot n'appartient
# jamais a root.

ARG DOTNET_SDK_TAG=8.0-bookworm
ARG DOTNET_RUNTIME_TAG=8.0-bookworm-slim
ARG GODOT_VERSION=4.3-stable
ARG GODOT_TEMPLATE_VERSION=4.3.stable.mono

# Sommes de controle publiees par Godot dans SHA512-SUMS.txt, sur la release
# 4.3-stable. Epinglees ici : un binaire d'editeur telecharge sans verification
# dans une image de compilation n'est pas verifiable par la suite.
#
# L'editeur et les templates MONO sont obligatoires : un projet Godot C#
# refuse de compiler avec le binaire standard.
ARG GODOT_EDITOR_URL=https://github.com/godotengine/godot/releases/download/4.3-stable/Godot_v4.3-stable_mono_linux_x86_64.zip
ARG GODOT_EDITOR_SHA512=33d0539bc368ff6f30b5f15d64ec893da8639bd607df83050b5df746c3d30da99238c26dd03a16d7fe31b3a3e58680aeb5545af8784d8ce01edeff07b061ac80
ARG GODOT_TEMPLATES_URL=https://github.com/godotengine/godot/releases/download/4.3-stable/Godot_v4.3-stable_mono_export_templates.tpz
ARG GODOT_TEMPLATES_SHA512=76796c1df810ecf351b48f303e5525100597be48043d3206bab7c44bd0bc576fb944b4b788619eb9848c31b4245ae0df6f3d665e63d4a083f6dae829919e8e8d

# --------------------------------------------------------------------------
FROM ${DOTNET_SDK_TAG} AS dev
# Tout ce qu'il faut pour compiler, tester et lancer boutap et boutap-gen.
ENV DEBIAN_FRONTEND=noninteractive \
    LANG=C.UTF-8 \
    LC_ALL=C.UTF-8 \
    TZ=UTC \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
RUN apt-get update \
 && apt-get install --yes --no-install-recommends \
      build-essential \
      ca-certificates \
      cmake \
      curl \
      git \
      pkg-config \
      python3 \
      python3-venv \
      unzip \
      zip \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /src

# --------------------------------------------------------------------------
FROM dev AS export
# L'editeur Godot et ses gabarits d'export. Les gabarits sont installes dans
# XDG_DATA_HOME et non dans HOME : le conteneur tourne avec le uid de
# l'utilisateur hote et HOME=/cache/home, donc un gabarit sous HOME serait
# inaccessible ou ecrase a chaque execution.
ARG GODOT_VERSION
ARG GODOT_TEMPLATE_VERSION
ARG GODOT_EDITOR_URL
ARG GODOT_EDITOR_SHA512
ARG GODOT_TEMPLATES_URL
ARG GODOT_TEMPLATES_SHA512
ENV GODOT_EDITOR_PATH=/opt/godot/bin/godot \
    XDG_DATA_HOME=/opt/godot/xdg
RUN mkdir -p "${GODOT_EDITOR_PATH%/*}" /tmp/godot-download \
 && curl --fail --location --silent --show-error --retry 3 \
      --output /tmp/godot-download/editor.zip "${GODOT_EDITOR_URL}" \
 && curl --fail --location --silent --show-error --retry 3 \
      --output /tmp/godot-download/templates.tpz "${GODOT_TEMPLATES_URL}" \
 && printf '%s  %s\n' "${GODOT_EDITOR_SHA512}" /tmp/godot-download/editor.zip \
      | sha512sum --check --strict - \
 && printf '%s  %s\n' "${GODOT_TEMPLATES_SHA512}" /tmp/godot-download/templates.tpz \
      | sha512sum --check --strict - \
 && unzip -q /tmp/godot-download/editor.zip -d /tmp/godot-download/editor \
 && cp "$(find /tmp/godot-download/editor -type f -name 'Godot_v*' | head -n 1)" \
      "${GODOT_EDITOR_PATH}" \
 && chmod 0755 "${GODOT_EDITOR_PATH}" \
 && mkdir -p "${XDG_DATA_HOME}/godot/export_templates/${GODOT_TEMPLATE_VERSION}" \
 && unzip -q /tmp/godot-download/templates.tpz -d /tmp/godot-download/templates \
 && cp -r /tmp/godot-download/templates/templates/* \
      "${XDG_DATA_HOME}/godot/export_templates/${GODOT_TEMPLATE_VERSION}/" \
 && rm -rf /tmp/godot-download \
 && "${GODOT_EDITOR_PATH}" --headless --version
WORKDIR /src

# --------------------------------------------------------------------------
FROM python:3.11-slim-bookworm AS probe
# La sonde de latence et l'oracle du portage C#. Python separe de .NET : les
# deux n'ont aucune dependance commune, et un conteneur par tache rend le
# diagnostic d'une erreur de version immediate.
ENV LANG=C.UTF-8 \
    LC_ALL=C.UTF-8 \
    TZ=UTC \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    VIRTUAL_ENV=/opt/probe \
    PATH=/opt/probe/bin:$PATH
RUN apt-get update \
 && apt-get install --yes --no-install-recommends \
      build-essential \
      ca-certificates \
      libsndfile1 \
      pkg-config \
      python3-dev \
 && rm -rf /var/lib/apt/lists/*
COPY tools/probe/requirements.txt tools/probe/pip.conf /tmp/probe/
RUN python3 -m venv "${VIRTUAL_ENV}" \
 && PIP_CONFIG_FILE=/tmp/probe/pip.conf \
    python3 -m pip install --requirement /tmp/probe/requirements.txt \
 && rm -rf /tmp/probe
WORKDIR /src

# --------------------------------------------------------------------------
FROM dev AS build
# Publication des deux executables de ligne de commande.
RUN dotnet publish Boutap.sln \
      --configuration Release \
      --output /out \
      -p:ErrorOnDuplicatePublishOutputFiles=false
# Recopie dans un arborescence stable : /out contient aussi les dependances,
# et l'image runtime n'a pas besoin des projets de test.

# --------------------------------------------------------------------------
FROM ${DOTNET_RUNTIME_TAG} AS runtime
# Image minimale d'execution : ni SDK, ni Python, ni Godot. Un pack se
# verifie, s'affiche et se regenere ici, ce qui couvre l'usage le plus courant
# de boutap en dehors du jeu.
ENV LANG=C.UTF-8 \
    LC_ALL=C.UTF-8 \
    TZ=UTC \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
WORKDIR /src
COPY --from=build /out ./
# Un seul point d'entree, plusieurs programmes : l'utilisateur choisit par le
# premier argument plutot que par le nom du binaire, ce qui evite d'apprendre
# quatre noms pour quatre outils.
COPY scripts/entrypoint.sh /usr/local/bin/boutap-entrypoint
RUN chmod 0755 /usr/local/bin/boutap-entrypoint
ENTRYPOINT ["/usr/local/bin/boutap-entrypoint"]
