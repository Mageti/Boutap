#!/usr/bin/env bash
# SPDX-FileCopyrightText: Boutap contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Règle R1 : aucun code de jeu ne lit l'horloge murale.
#
# Le temps d'un jeu de rythme vient de l'horloge audio. Si une décision de jeu
# dépend de DateTime.Now ou de Stopwatch, alors deux machines différentes ne
# jouent pas le même morceau de la même façon, et le test de dérive de S1
# devient inapplicable.
#
# Ce script refuse les appels interdits dans src/ et native/. Il ne dit pas
# pourquoi ils sont interdits : voir wiki: spec.md §7.3 et les Notes du
# contexte permanent.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# Répertoires inspectés. third_party/ est exclu : on ne corrige pas les
# sources d'autrui, et miniaudio comme Godot ont leurs propres horloges.
DIRS=("src" "native" "tools")
[ -d "src" ] || DIRS=("native" "tools")

# Motifs interdits, un par expression régulière.
#   1-2  horloge murale .NET
#   3-4  horloge murale C / C++
#   5    construction d'une horloge à partir de l'heure système
#   6    compteur de performance (interdit hors S1, où il sert à étalonner)
#   7    graine dérivée de l'heure ou de l'identifiant du processus
PATTERNS=(
  'DateTime\.(Now|UtcNow|Today)'
  'Stopwatch\.StartNew'
  'gettimeofday\('
  'GetSystemTimeAsFileTime'
  'clock_gettime\(CLOCK_REALTIME'
  'QueryPerformanceCounter'
  'Random\.Shared'
  'System\.Random'
  'Guid\.NewGuid'
  'Environment\.TickCount'
)

# Lignes où l'appel est légitime, avec la raison. Une exception sans raison
# écrite est une exception qui va survivre trois ans.
# Format : chemin|fragment de la ligne|raison
ALLOW=(
  "src/Boutap.Input/InputClock.cs|QueryPerformanceCounter|mesure une fois l'ecart entre l'horloge du noyau et l'horloge audio"
  "src/Boutap.Audio/AudioClock.cs|QueryPerformanceCounter|etalonnage de l'horloge audio au demarrage"
  "src/Boutap.Tools/Commands/BenchLatencyCommand.cs|Stopwatch.StartNew|mesure de la latence de la chaine audio, hors boucle de jeu"
  "src/Boutap.Tools/Commands/BenchInputCommand.cs|Stopwatch.StartNew|mesure de la latence de la chaine d'entree, hors boucle de jeu"
  "src/Boutap.Tools/Commands/BenchCiCommand.cs|Stopwatch.StartNew|mesure de la duree totale de la CI locale"
)

# Marqueur en ligne, pour les cas qu'aucune liste ne peut anticiper : un test
# qui cherche la chaine interdite doit la contenir, et un commentaire peut la
# citer. « R1-allow: <raison> » sur la ligne concerned desarme le motif pour
# cette ligne. Une raison vide reste une autorisation sans raison.
R1_MARKER="R1-allow:"

is_allowed() {
  local file="$1" line_no="$2" pattern="$3" content="$4"
  local entry
  # Déclarations séparées : dans `local a=… b=$a`, bash développe $a avant
  # d'exécuter local, donc b lirait la variable du niveau appelant.
  if [[ "$content" == *"$R1_MARKER"* ]]; then
    local inline_reason="${content#*"$R1_MARKER"}"
    if [ -z "${inline_reason//[[:space:]]/}" ]; then
      echo "   autorisation SANS RAISON : $file:$line_no ($pattern)"
      return 1
    fi
    return 0
  fi
  for entry in "${ALLOW[@]}"; do
    # Déclarations séparées : dans `local a=… b=$a`, bash développe $a avant
    # d'exécuter local, donc b lirait la variable du niveau Called.
    local f="${entry%%|*}"
    local rest="${entry#*|}"
    local needle="${rest%%|*}"
    local reason="${rest#*|}"
    # Le motif d'autorisation est une sous-chaine du motif interdit.
    if [[ "$needle" != "" && "$content" == *"$needle"* ]]; then
      [[ -z "$f" || "$file" == "$f" ]] || continue
      if [[ -z "$reason" ]]; then
        echo "   autorisation SANS RAISON : $f:$line_no ($needle)"
        return 1
      fi
      return 0
    fi
  done
  return 1
}

status=0
scanned=0

for dir in "${DIRS[@]}"; do
  [ -d "$dir" ] || continue
  # Même raison que dans les deux autres scripts : « -path './bin/*' »
  # n'excluait que la racine. Les objets de compilation sont du code
  # généré, et generated_code_marker ne s'applique qu'aux sources du
  # projet : les balayer reviendrait a auditer la sortie du compilateur.
  # third_party/ n'est pas élagué parce que les DIRS ne le contiennent pas.
  while IFS= read -r file; do
    scanned=$((scanned + 1))
    rel="${file#./}"
    # Les fichiers de test peuvent utiliser une horloge de test : ils doivent
    # injecter un temps, pas le lire. On les inspecte quand même, mais une
    # occurrence y est signalée comme avertissement, pas comme erreur.
    is_test=0
    [[ "$rel" == *.Tests/* ]] && is_test=1

    for pattern in "${PATTERNS[@]}"; do
      line_no=0
      while IFS= read -r line; do
        line_no=$((line_no + 1))
        if [[ "$line" =~ $pattern ]]; then
          if is_allowed "$rel" "$line_no" "$pattern" "$line"; then
            continue
          fi
          if [ "$is_test" -eq 1 ]; then
            # Un test doit pouvoir citer l'appel interdit : c'est
            # littéralement ce qu'il cherche. Reste un avertissement, visible,
            # jamais un échec de build.
            echo "::warning file=$rel,line=$line_no::Règle R1 dans un test — « $line » (motif : $pattern)"
            continue
          fi
          echo "::error file=$rel,line=$line_no::Règle R1 — « $line » (motif : $pattern)"
          echo "   Le temps de jeu vient de l'horloge audio. Voir wiki: spec.md §7.3."
          status=1
        fi
      done < "$file"
    done
  done < <(find "$dir" \
             \( -name bin -o -name obj -o -name build -o -name .cache \
                -o -name .codenomad -o -name .tmp-tests -o -name TestResults \
             \) -prune -o \
             -type f \( -name '*.cs' -o -name '*.c' -o -name '*.h' \) -print | sort)
done

if [ "$scanned" -eq 0 ]; then
  # Normal au tout début du projet : il n'y a pas encore de sources.
  echo "Aucun fichier source à inspecter (projet encore sans code). Règle R1 : rien à signaler."
  exit 0
fi

if [ "$status" -eq 0 ]; then
  echo "Règle R1 : $scanned fichier(s) inspecté(s), aucune horloge murale."
else
  echo "--- Règle R1 violée ---"
fi
exit $status
