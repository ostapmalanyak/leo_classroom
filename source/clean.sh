#!/usr/bin/env bash
# Deletes local build output so the working tree holds source only - ready to zip and put away.
# Nothing a build cannot recreate is touched: ./secrets, ./.env, ./certs and ./backup stay put.
#
#   ./clean.sh                  remove build output and the bundles in ./dist
#   ./clean.sh --keep-bundles   leave ./dist alone
#   ./clean.sh --dry-run        list what would go, delete nothing
#
# Removed at any depth under backend/, cli/ and frontend/:
#   bin obj TestResults node_modules dist .angular .vs Logs
# Removed at the top level: dist (unless --keep-bundles), logs, backend/migration.sql
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

DRY_RUN=0
KEEP_BUNDLES=0

while [ $# -gt 0 ]; do
  case "$1" in
    --keep-bundles) KEEP_BUNDLES=1; shift ;;
    --dry-run)      DRY_RUN=1; shift ;;
    -h|--help)      sed -n '2,11p' "${BASH_SOURCE[0]}" | sed 's/^# \?//'; exit 0 ;;
    *)              echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done

# rm -rf in a loop deserves a landmark check that this really is the project root.
if [ ! -e compose.yaml ] || [ ! -d backend ]; then
  echo "this does not look like the leoclassrooms root: $ROOT" >&2
  exit 1
fi

# --- collect what goes -----------------------------------------------------------------------------
# -prune keeps the walk out of what is about to be deleted, so frontend/node_modules is one entry
# rather than thousands, and its .bin never matches the bin rule.
declare -a TARGETS=()
while IFS= read -r path; do
  [ -n "$path" ] && TARGETS+=("$path")
done < <(
  find backend cli frontend -type d \
       \( -name bin -o -name obj -o -name TestResults -o -name node_modules \
          -o -name dist -o -name .angular -o -name .vs -o -name Logs \) \
       -prune -print 2>/dev/null | sort
)

# Top-level output the walk above does not cover: Serilog's bind-mount directory, the EF script
# ManageMigration.ps1 writes, and the tarballs bundle.sh drops in ./dist.
for extra in logs backend/migration.sql; do
  if [ -e "$extra" ]; then TARGETS+=("$extra"); fi
done
if [ "$KEEP_BUNDLES" -eq 0 ] && [ -e dist ]; then TARGETS+=(dist); fi

if [ ${#TARGETS[@]} -eq 0 ]; then
  echo "nothing to remove; the tree is already source-only"
  exit 0
fi

# --- report, then remove ---------------------------------------------------------------------------
human() { awk -v kb="${1:-0}" 'BEGIN { split("KiB MiB GiB TiB", u); v = kb; i = 1
                                       while (v >= 1024 && i < 4) { v /= 1024; i++ }
                                       printf "%.1f %s\n", v, u[i] }'; }

TOTAL_KB=0
printf '%s %d path(s):\n' "$([ "$DRY_RUN" -eq 1 ] && echo 'would remove' || echo 'removing')" "${#TARGETS[@]}"
for path in "${TARGETS[@]}"; do
  KB="$(du -sk "$path" 2>/dev/null | cut -f1)"
  TOTAL_KB=$((TOTAL_KB + ${KB:-0}))
  printf '  %9s  %s\n' "$(human "${KB:-0}")" "$path"
done
echo

if [ "$DRY_RUN" -eq 1 ]; then
  echo "$(human "$TOTAL_KB") would be freed (dry run; nothing deleted)"
  exit 0
fi

for path in "${TARGETS[@]}"; do
  rm -rf -- "$path"
done
echo "removed ${#TARGETS[@]} path(s), $(human "$TOTAL_KB") freed"

# --- what is left that must not leave this machine --------------------------------------------------
declare -a SECRET=()
for entry in .env secrets certs; do
  if [ -e "$entry" ]; then SECRET+=("$entry"); fi
done
if [ ${#SECRET[@]} -gt 0 ]; then
  echo
  echo "still present, and a plain zip of this directory would include them: ${SECRET[*]}"
  echo "exclude them, or use ./bundle.sh, which never packs them."
fi
