#!/bin/sh
# One backup run: pg_dump both databases + snapshot the Forgejo data, then restic to the offsite target
# and prune per retention. Offsite credentials and the restic password are Tier-1 secrets (/run/secrets).
set -eu
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
STAMP="$(date +%Y%m%dT%H%M%S)"
echo "backup: starting run $STAMP"
# Restic configuration from secrets + non-secret env.
export RESTIC_PASSWORD_FILE="/run/secrets/restic_password"
# Offsite credentials file holds the provider env (e.g. AWS_ACCESS_KEY_ID=..., AWS_SECRET_ACCESS_KEY=...).
if [ -f /run/secrets/backup_offsite_credentials ]; then
  set -a
  . /run/secrets/backup_offsite_credentials
  set +a
fi
PGPASSWORD_BACKEND="$(cat /run/secrets/backend_db_password)"
PGPASSWORD_FORGEJO="$(cat /run/secrets/forgejo_db_password)"
echo "backup: pg_dump backend database"
PGPASSWORD="$PGPASSWORD_BACKEND" pg_dump -h backend-db -U "$BACKEND_DB_USER" -d "$BACKEND_DB_NAME" \
  -F c -f "$WORK/backend-${STAMP}.dump"
echo "backup: pg_dump forgejo database"
PGPASSWORD="$PGPASSWORD_FORGEJO" pg_dump -h forgejo-db -U "$FORGEJO_DB_USER" -d "$FORGEJO_DB_NAME" \
  -F c -f "$WORK/forgejo-db-${STAMP}.dump"
# Forgejo repos + config: the canonical tool is `forgejo dump`, run via `podman exec forgejo forgejo dump`
# when a container socket is available; the container-native fallback archives the read-only data volume,
# which together with the DB dump above reconstructs repos + config + DB (see RESTORE.md).
if [ -d /forgejo-data ]; then
  echo "backup: archiving forgejo data volume"
  tar -czf "$WORK/forgejo-data-${STAMP}.tar.gz" -C /forgejo-data .
fi
echo "backup: restic snapshot to $RESTIC_REPOSITORY"
restic snapshots >/dev/null 2>&1 || restic init
restic backup --tag leo-classroom "$WORK"
echo "backup: pruning per retention ($RESTIC_RETENTION)"
# shellcheck disable=SC2086
restic forget --prune $RESTIC_RETENTION
echo "backup: run $STAMP complete"
