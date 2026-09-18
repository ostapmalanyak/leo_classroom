# RESTORE runbook

A backup without an exercised restore is non-functional (§18.1). This runbook is **version-controlled** and
**exercised on a schedule** (e.g. once per term) with a sign-off. It restores both Postgres databases and
the Forgejo data from the restic offsite repository produced by the `backup` container.

## What compose can and cannot destroy

`forgejo_data`, `forgejo_db_data`, `backend_db_data` and `caddy_data` are declared `external: true` in
`compose.yaml`. Compose neither creates nor removes them, so `podman compose down -v` leaves them intact and
only the regenerable volumes (`caddy_config`, `backend_downloads`) go. Destroying data now takes an
explicit `podman volume rm`, which is the only place in these runbooks that names one.

## Inputs

- `restic_password` and `backup_offsite_credentials` (the same Tier-1 secrets the backup container uses).
- `RESTIC_REPOSITORY` from `.env`.
- The pinned Forgejo image tag and the current backend image (so the schema version matches the dumps).

## Restore procedure

1. **Stop services that write data** (keep databases up): `podman compose stop backend forgejo backup`.
2. **Pick a snapshot:**
   ```sh
   export RESTIC_PASSWORD_FILE=secrets/restic_password
   set -a; . secrets/backup_offsite_credentials; set +a
   restic -r "$RESTIC_REPOSITORY" snapshots
   restic -r "$RESTIC_REPOSITORY" restore <snapshot-id> --target ./restore
   ```
3. **Restore the backend database:**
   ```sh
   PGPASSWORD=$(cat secrets/backend_db_password) \
     pg_restore -h <backend-db> -U leoclassroom -d leoclassroom --clean --if-exists \
     ./restore/.../backend-<stamp>.dump
   ```
4. **Restore the Forgejo database:**
   ```sh
   PGPASSWORD=$(cat secrets/forgejo_db_password) \
     pg_restore -h <forgejo-db> -U forgejo -d forgejo --clean --if-exists \
     ./restore/.../forgejo-db-<stamp>.dump
   ```
5. **Restore the Forgejo data volume** (repos + config): extract `forgejo-data-<stamp>.tar.gz` into the
   `forgejo_data` volume. It is `external: true`, so it survives every compose command - restoring into it
   means overwriting its contents, not recreating it. (If the snapshot was produced by `forgejo dump`
   instead, follow Forgejo's restore-from-dump steps: unzip and place `repos/`, `data/`, and `app.ini`.)
6. **Start services:** `podman compose up -d` (the `migrator` runs first; the backend's schema guard then
   verifies the live schema matches the build).

## Verification checklist (sign-off)

- [ ] `migrator` exited 0 and the backend started without a schema-mismatch failure.
- [ ] **Schema version:** `SELECT "MigrationId" FROM leo_classroom."__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 1;`
      matches the deployed build's latest migration.
- [ ] **Row-count sanity:** spot-check `Users`, `Courses`, `Assignments`, `Acceptances` counts against expectations.
- [ ] **Forgejo integrity:** `podman exec forgejo forgejo doctor check --all` reports healthy; a sample repo clones over HTTPS.
- [ ] **SSO:** a test user logs into the app and Forgejo via Keycloak.
- [ ] Restore exercised by: ______________________  date: ____________  result: ☐ pass ☐ fail

## PostgreSQL major version upgrades

A Postgres major upgrade is **not** in place: version 18 cannot read a data directory written by 16, so
swapping the image tag on an existing volume leaves the container refusing to start with
`database files are incompatible with server`. The dumps this runbook restores are logical
(`pg_dump -F c`), so they cross major versions fine and are the supported path:

1. Take a fresh backup with the **old** image still running, and verify it lists: `restic snapshots`.
2. Stop everything: `podman compose down`.
3. Remove the old data volumes **and create them again empty**:
   ```sh
   podman volume rm  leoclassrooms_backend_db_data leoclassrooms_forgejo_db_data
   podman volume create leoclassrooms_backend_db_data
   podman volume create leoclassrooms_forgejo_db_data
   ```
   Recreating them is not optional: both are `external: true`, so compose will not make them for you and
   refuses to start while either is missing. (Names may differ - check `podman volume ls`; the prefix is
   `VOLUME_PREFIX` in `.env`. Nothing else in this stack is stored in them; the Forgejo repositories live in
   `forgejo_data`, which is **not** touched here.)
4. Start only the databases so the new version initialises empty: `podman compose up -d backend-db forgejo-db`.
5. Restore both dumps with steps 3 and 4 of the restore procedure above.
6. `podman compose up -d`.

Two things to know about the 18 images:

- From 18 on, the official image's `PGDATA` is `/var/lib/postgresql/<major>/docker` and its declared volume is
  `/var/lib/postgresql`. `compose.yaml` mounts the new location. A mount still aimed at the old
  `/var/lib/postgresql/data` is **silently ignored** - the server starts, works, and writes into an anonymous
  volume that disappears on the next `podman compose down -v`.
- `pg_dump` refuses to dump a server newer than itself, so the `backup` image's client tracks the server
  version (Alpine 3.23, `postgresql18-client`). Bump both together or the nightly backup fails.

## Pre-upgrade safety (Forgejo version bumps)

Forgejo self-migrates its DB on a version bump and there is **no automated compatibility gate** (deliberate
deviation from §3.3). Before bumping the pinned Forgejo image tag: take a fresh backup, **verify it restores**
using this runbook in a scratch environment, then bump. Roll back by restoring the pre-upgrade snapshot.
