# First deployment

Bringing the stack up on a fresh server, in order. Nothing here is idempotent-by-accident: steps 6 and 7
bootstrap credentials that cannot exist before the services they belong to are running, so the order matters.

`TROUBLESHOOTING.md` covers what to do when a step misbehaves, and lists every problem already hit and
solved. `RESTORE.md` covers restoring from backup and PostgreSQL major upgrades; `SECRETS.md` covers the
secret inventory and rotation; `DECISIONS.md` covers why things are the way they are.

## 0. Prerequisites

The stack does **not** contain Keycloak, LDAP, SMTP or Moodle. Those are external and, except Keycloak, all
optional.

- **Podman** with `podman compose` (or `docker compose`).
- **Keycloak**: the school's shared instance at `https://auth.htl-leonding.ac.at`, realm `htlleonding`,
  client `htlleonding-service`. You do not create or administer it, which means **one thing has to be
  requested from whoever does**: the redirect URI and web origin of this deployment
  (`http://<server-ip>/*` and `http://<server-ip>`) added to that client. Without it Keycloak refuses the
  login with `Invalid parameter: redirect_uri` and nothing else in this runbook matters.
  - That realm carries **no roles claim**; a caller's group is read from `ldap_entry_dn`
    (`OU=Students` / `OU=Teachers` / `OU=TestUsers`), which is what the school's own demos key on.
  - It also sets **no `aud`** on access tokens, so `KEYCLOAK_VALIDATE_AUDIENCE=false`.
  - It has **no administrator role at all**, so administrators are named by username in `ADMIN_USER_*`.
- **Nothing else.** Forgejo accounts and their passwords are managed by the backend itself - see
  `DECISIONS.md`, "Forgejo accounts get an app-managed password instead of SSO". Step 7 is optional and only
  applies if you later get an LDAP source or a second Keycloak client.
- **LDAP is the one thing worth asking for.** It is not configured today (`LDAP_HOST` is empty), and that
  single gap costs two things at once: the nightly sync never runs, so user data and class rosters never
  arrive on their own; and Forgejo has to keep a **second account per user**, with a password this
  application generates and shows once. A bind DN and password fix both - the sync populates rosters, and
  step 7's `forgejo admin auth add-ldap` lets users sign in to Forgejo and push over git with their ordinary
  school password, after which the generated credential stops being issued at all. Until then, class rosters
  fill in as users sign in (`DECISIONS.md`, "Class rosters are built from sign-ins").
- **Reachability**: the server must be able to reach *itself* at `APP_DOMAIN` / `FORGEJO_DOMAIN`. The
  backend clones student repositories over the public URLs Forgejo generates, so a server that cannot reach
  its own public address provisions repositories fine and then fails to export them. With a bare IP this is
  automatic; with a hostname it needs split-horizon DNS or NAT hairpinning.
- **Ports**: 80 (and 443 if `PUBLIC_SCHEME=https`) plus `FORGEJO_PORT` reachable inbound.

## 1. Copy the project

`./bundle.sh` packs the tree into `dist/leoclassrooms-full-<stamp>.tar.gz` and prints the commands for the
other end. It never touches the server itself, so any transport works — `scp` through a jump host, a proxy,
a USB stick. Build output is left out; the images compile from source inside the container.

```sh
# workstation
./bundle.sh
scp -J jumpuser@jumphost dist/leoclassrooms-full-*.tar.gz perfadmin@<server>:~/

# server
mkdir -p ~/leoclassrooms && cd ~/leoclassrooms
tar xzf ~/leoclassrooms-full-*.tar.gz --strip-components=1 -C .
mkdir -p logs                       # the backend's Serilog files land here
```

`secrets/`, `.env` and `certs/` are excluded from every bundle, so extracting over a directory that already
holds them leaves them alone. That is the intended way to redeploy: wipe the server directory except for
those, extract, rebuild.

For a change to one part, a smaller bundle saves the transfer and says which images to rebuild:

| Command | Carries | Server rebuilds |
|---|---|---|
| `./bundle.sh --part backend` | `backend/`, `compose.yaml` | `podman compose build backend migrator` |
| `./bundle.sh --part frontend` | `frontend/` | `podman compose build frontend` |
| `./bundle.sh --part config` | `compose.yaml`, `Caddyfile`, scripts, docs | nothing; `up -d`, plus `restart caddy` for a Caddyfile edit |

`migrator` is in the backend row because it builds from the same `./backend` context — a new migration
reaches the database only if that image is rebuilt too.

## 2. Configure

```sh
cd ~/leoclassrooms
cp .env.example .env
$EDITOR .env
```

Every variable is commented in `.env.example`. The ones with no sensible default and no secret behind them:
`APP_DOMAIN`, `FORGEJO_DOMAIN`, `ACME_EMAIL`, and the five `KEYCLOAK_*` values. Leave `LDAP_HOST` and
`SMTP_HOST` empty for now if you like — the stack runs without them.

Leave `FORGEJO_AUTH_SOURCE_ID=0`; step 7 sets it.

## 3. Create the secrets

```sh
./init-secrets.sh --scaffold     # generates ./secrets/ with random values where it can
./init-secrets.sh                # verifies the set is complete and fixes permissions
```

These are plain files that compose mounts at `/run/secrets`; there is no `podman secret create` step. On the
default setup you do not have to edit any of them — the optional ones (LDAP, mail, the Forgejo OIDC client)
are created empty on purpose, because nothing reads them until you enable those features.

`Keycloak__ForgejoClientSecret` stays at its `REPLACE_ME` placeholder unless you take option B in step 7;
it is mounted but unused otherwise.

Two of the scaffolded values are **placeholders that must be replaced later, not kept**:

- `Forgejo__AdminToken` — a random string until Forgejo exists to issue a real token (step 6).
- `Forgejo__WebhookSecret` — this one is genuinely just a shared random string; keep it, and give the same
  value to Forgejo when you create the webhook.

`ConnectionStrings__Postgres` is scaffolded to point at `backend-db` with the generated database password —
check it matches `BACKEND_DB_NAME` / `BACKEND_DB_USER` in `.env`.

The backend refuses to start outside development if `Forgejo__AdminToken`, `Forgejo__WebhookSecret` or
`DataProtection__MasterKey` is blank, or if the master key is not a base64 128/192/256-bit value.

## 3a. After any later copy: check for new settings

`.env` is never overwritten by a copy, so a release that introduces a variable leaves yours missing it. List
what `.env.example` has that yours does not:

```sh
comm -23 <(grep -oE '^[A-Z_]+' .env.example | sort -u) <(grep -oE '^[A-Z_]+' .env | sort -u)
```

Anything printed should be copied across with its comment. Then re-run the preflight:

```sh
podman compose config >/dev/null && echo "compose file OK"
```

## 3b. Create the persistent volumes

`forgejo_data`, `forgejo_db_data`, `backend_db_data` and `caddy_data` are declared `external: true`, so
compose will not create them — and, more to the point, no compose command can delete them. Create them once:

```sh
for v in forgejo_data forgejo_db_data backend_db_data caddy_data; do
  podman volume create "${VOLUME_PREFIX:-leoclassrooms}_$v"
done
podman volume ls
```

If one is missing, compose refuses to start with `volume … declared as external, but could not be found`.
That is the point: the alternative is a database silently coming up empty.

### On a deployment that already has data

The names default to exactly what compose created for you before — `<project>_<volume>`, where the project
name is the directory name. So there is nothing to migrate: the existing volumes already carry these names
and the stack picks them straight back up. Confirm before the first `up` with the new file:

```sh
podman volume ls --format '{{.Name}}' | grep -E '_(forgejo_data|forgejo_db_data|backend_db_data|caddy_data)$'
```

Four names, all sharing one prefix. If that prefix is not `leoclassrooms`, put it in `.env` as
`VOLUME_PREFIX=` before starting; if you skip that, compose looks for volumes that do not exist and stops —
loudly, without touching the ones you have.

---

## 4. First start

Rootless Podman cannot bind ports below 1024, and `caddy` publishes 80 and 443. Allow it once, before the
first `up`:

```sh
echo 'net.ipv4.ip_unprivileged_port_start=80' | sudo tee /etc/sysctl.d/99-leo-unprivileged-ports.conf
sudo sysctl --system
sysctl net.ipv4.ip_unprivileged_port_start          # must print 80
```

Without it the stack comes up except for `caddy`, which fails with
`rootlessport cannot expose privileged port 80`. The alternative is running the whole stack rootful with
`sudo podman compose`, which puts the images and volumes under root and means every later command needs
`sudo` too.

```sh
podman compose build
podman compose up -d
```

Startup order is enforced by healthchecks: databases → `migrator` (applies the EF bundle, exits 0) →
`forgejo` + `backend` → `frontend` + `caddy`.

```sh
podman compose ps                    # everything healthy, migrator "exited (0)"
podman compose logs -f backend       # "Database schema verified up to date", "Now listening"
curl -fsS https://APP_DOMAIN/api/version
```

If the backend exits immediately, read the first line of its log — the secret guard names exactly which
value is missing.

`migrator` builds its own `DbContext` through `DesignTimeDatabaseContextFactory` rather than by starting the
web application, so it needs nothing but the connection string. If it ever fails with
`Unable to create a 'DbContext'`, that factory is missing from the image — rebuild with
`podman compose build --no-cache migrator`.

## 5. Create the Forgejo admin

Forgejo ships with `INSTALL_LOCK=true` and registration disabled, so there is no web setup wizard and no
first user. Create one:

```sh
podman compose exec -u git forgejo forgejo admin user create \
  --admin --username leo-admin --email admin@school.example --random-password
```

Note the password it prints.

> **Never give an operator account a real person's e-mail address.** Forgejo enforces unique e-mails in the
> schema (`EmailAddress.Email` and `.LowerEmail` are `UNIQUE NOT NULL`) and no setting relaxes it. The
> backend creates each app user's Forgejo account from the `email` claim in their token, so any address held
> by `leo-admin` or the bot is an address **no user of the app can ever be created with** — and that person
> gets a 422 on their first git-credential reset, for a reason that points nowhere near this step. Use role
> addresses: `admin@…`, `bot@…`.

This account is a **break-glass login**, not a dependency. Step 6's bot is created by the same CLI command
and does not need it, and day-to-day Forgejo administration can be done from your own account once you
promote it (Site Administration → User Accounts → *you* → Administrator). What `leo-admin` buys is a way
into Forgejo that does not go through Keycloak or through this application — worth keeping for the day one
of those is broken. Skip it only if you are content to have no such route.

## 6. Create the bot account and its token

The backend acts as a bot account. Its token is what `Forgejo__AdminToken` must contain — the random value
from step 3 is a placeholder that Forgejo has never seen.

```sh
# the account named by FORGEJO_BOT_NAME in .env
# --must-change-password=false is NOT optional: see the warning below.
podman compose exec -u git forgejo forgejo admin user create \
  --admin --username leo-classroom-bot --email bot@school.example \
  --random-password --must-change-password=false

# a token for it, with the scopes the backend needs
podman compose exec -u git forgejo forgejo admin user generate-access-token \
  --username leo-classroom-bot --token-name leo-backend \
  --scopes write:organization,write:repository,write:user,write:admin
```

> **`--must-change-password=false` or every API call fails.** `admin user create` sets the
> must-change-password flag by default — it assumes a human will be handed the password and log in. Forgejo
> checks that flag on **every authenticated request, tokens included**, and answers
> `403 {"message":"You must change your password. …"}` until it is cleared. The token is valid; the account
> is simply not allowed to do anything yet. A bot account never logs into the web UI, so it would sit there
> forever. To clear it on an account that already has the flag:
>
> ```sh
> podman compose exec -u git forgejo forgejo admin user must-change-password --unset leo-classroom-bot
> ```
>
> It takes effect on the next request — nothing to restart.

Put the printed token into the secret and restart what reads it:

```sh
printf '%s' '<token>' > secrets/Forgejo__AdminToken     # printf, so there is no trailing newline
podman compose up -d --force-recreate backend
```

## 7. (Optional) Delegate Forgejo login to LDAP or Keycloak

**Skip this for the proof of concept.** With `FORGEJO_AUTH_SOURCE_ID=0` the backend creates each user's
Forgejo account itself and issues them a random password on their account page, so nothing external is
needed. See `DECISIONS.md`.

Do this only when you want single sign-on for git as well. `FORGEJO_AUTH_SOURCE_ID` binds accounts to a
login source; once set, the app stops managing passwords and says so on the account page.

### Option A — LDAP source (needs nothing from the Keycloak administrator)

Forgejo binds to the same directory the backend syncs from, and students sign in with their school
credentials:

```sh
podman compose exec -u git forgejo forgejo admin auth add-ldap \
  --name school-ldap \
  --security-protocol ldaps --host "$LDAP_HOST" --port 636 \
  --bind-dn "$LDAP_BIND_DN" --bind-password "$(cat /run/secrets/Ldap__BindPassword)" \
  --user-search-base "$LDAP_STUDENT_BASE_DN" \
  --user-filter '(uid=%s)' \
  --username-attribute uid --email-attribute mail
```

### Option B — OIDC source (single sign-on, needs a second Keycloak client)

Only if the Keycloak administrator gives you a confidential client for Forgejo. Its secret goes into the
`Keycloak__ForgejoClientSecret` secret, and its redirect URI is
`http://<server-ip>:<FORGEJO_PORT>/user/oauth2/keycloak/callback` — note the **port differs** from the
app's, so a wildcard registered for the app alone will not cover it.

```sh
podman compose exec -u git forgejo forgejo admin auth add-oauth \
  --name keycloak --provider openidConnect \
  --key <client-id> --secret "$(cat /run/secrets/Keycloak__ForgejoClientSecret)" \
  --auto-discover-url https://auth.htl-leonding.ac.at/realms/htlleonding/.well-known/openid-configuration
```

### Either way

```sh
podman compose exec -u git forgejo forgejo admin auth list
```

Take the **id** into `.env` as `FORGEJO_AUTH_SOURCE_ID`, then
`podman compose up -d --force-recreate backend`.

Switching later is just a different id: existing accounts are re-bound by the next sync.

## 8. The webhook registers itself

Nothing to do. When the backend provisions a course's Forgejo organisation it calls
`EnsureOrgWebhookAsync`, which creates the org webhook pointing at `Forgejo__WebhookTargetUrl` with
`Forgejo__WebhookSecret` — both from configuration, so they cannot drift from what the backend verifies
signatures against. A pre-existing hook comes back 422 and is treated as success, so it is safe to re-run.

Confirm after creating your first course:

```sh
podman compose logs backend | grep -i webhook
```

Then push to any student repository; the event should appear in the backend log. A mismatch would show as
`Rejected Forgejo webhook with missing or invalid signature`, which at this point could only mean the
secret file changed without recreating the backend.

## 9. Verify

- [ ] `https://APP_DOMAIN` loads the SPA and redirects to Keycloak.
- [ ] A teacher logs in and reaches the dashboard. (`GET /api/courses` returning 401 means the token's
      issuer or audience does not match `KEYCLOAK_AUTHORITY` / `KEYCLOAK_AUDIENCE`; 403 means the `roles`
      claim is missing or empty.)
- [ ] `https://FORGEJO_DOMAIN` loads and "Sign in with keycloak" works.
- [ ] Creating a course creates a Forgejo organisation.
- [ ] A student accepts an assignment and gets a repository they can clone over HTTPS.
- [ ] `podman compose logs backup` shows the cron installed; force one run and confirm a restic snapshot:
      `podman compose exec backup /opt/backup/backup.sh`.
- [ ] Then exercise `RESTORE.md` once, before there is data you care about.

## Enabling LDAP and mail later

Fill in the `LDAP_*` / `SMTP_*` values in `.env`, put the passwords into the `Ldap__BindPassword` /
`Smtp__Password` secrets, and recreate the backend. Until then the nightly LDAP job logs a failed read and
skips — including the destructive pass, so it cannot mass-disable anyone by accident — and notification mail
stays queued rather than being lost.
