# Tier-1 secret inventory & rotation

All Tier-1 secrets are **files in the git-ignored `secrets/` directory**, mounted read-only at
`/run/secrets` — never environment variables, never inlined in `compose.yaml` or images (§17.1). The backend
reads them with the **KeyPerFile** provider (file name `Section__Key` → config `Section:Key`); Forgejo reads
them via `FORGEJO__…__FILE`. `.env` holds only non-secret config.

`./init-secrets.sh --scaffold` generates a starting set; `./init-secrets.sh` verifies the set is complete and
re-applies permissions.

> **Why files and not `podman secret create`.** `compose.yaml` used to declare these as `external: true`.
> That is a **Docker Swarm** feature: plain `docker compose` — which `podman compose` delegates to on most
> hosts — rejects it with `unsupported external secret`. Files work with every compose implementation, and
> the values had to exist in `secrets/` regardless, so this removes a step rather than adding one.
>
> Permissions are directory `700`, files `644`. The `644` looks wrong and is deliberate: compose bind-mounts
> each file into containers that drop to their own user (Forgejo runs as uid 1000), which cannot read a file
> owned by the host user. The `700` on the directory is what keeps other host users out.
>
> Secret files carry **no trailing newline** — the value is the entire file, and a stray `\n` ends up inside
> an HTTP `Authorization` header or a connection string.

## Inventory

| Secret file (`secrets/…`) | Consumed by | Purpose |
|---|---|---|
| `ConnectionStrings__Postgres` | backend, migrator | Backend DB connection string (incl. password) |
| `backend_db_password` | backend-db, backup | `backend-db` Postgres password |
| `forgejo_db_password` | forgejo, forgejo-db, backup | `forgejo-db` Postgres password |
| `Forgejo__AdminToken` | backend | Forgejo admin API token **and** git service token (HTTPS basic) |
| `Forgejo__WebhookSecret` | backend | HMAC shared secret for Forgejo → backend webhooks |
| `Keycloak__ForgejoClientSecret` | forgejo | Forgejo's confidential Keycloak OIDC client secret |
| `Ldap__BindPassword` | backend | LDAP bind DN password (nightly sync) |
| `Smtp__Password` (optional) | backend | SMTP system-account password (omit for anonymous relay) |
| `DataProtection__MasterKey` | backend | Tier-2 AES-GCM master key (base64) for encrypted columns |
| `restic_password` | backup | restic repository encryption password |
| `backup_offsite_credentials` | backup | Offsite provider env (e.g. `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY`) |

> The Keycloak **backend** client is a public/bearer resource server and needs no client secret; the
> Forgejo client is confidential and does (`Keycloak__ForgejoClientSecret`).

## Rotation playbook

General procedure: replace the value in `secrets/<name>` (with `printf '%s' '<value>' > secrets/<name>`, so
no trailing newline), run `./init-secrets.sh` to re-check permissions, then recreate the consuming
service(s) so the new file is read: `podman compose up -d --force-recreate <service>`.

- **DB passwords** (`backend_db_password`, `forgejo_db_password`): rotate the Postgres role password, update
  the secret file (and `ConnectionStrings__Postgres`), restart the DB + its consumers.
- **`Forgejo__AdminToken`**: mint a new Forgejo admin token, update the secret, restart the backend.
- **`Forgejo__WebhookSecret`**: update the secret and the webhook definitions in Forgejo together, restart backend.
- **`Keycloak__ForgejoClientSecret`**: rotate in Keycloak, update the secret, restart Forgejo.
- **`Ldap__BindPassword` / `Smtp__Password` / `backup_offsite_credentials` / `restic_password`**: update at
  the source, replace the secret, restart the consumer (backend or backup).
- **`DataProtection__MasterKey`** (the only heavy rotation): generate a new key, **re-encrypt all Tier-2 rows**
  (decrypt with the old key, re-`Protect` with the new) before retiring the old key; the master key is never
  included in backups. Schedule downtime / a maintenance window for the re-encryption pass.
