# Decisions

Choices that are not readable from the code, with the reasoning and what would change them. Newest first.

---

## 2026-09-01 — The entry point owns the transaction

**Status:** accepted; `TransactionProviderExtensions.ExecuteAsync` is the shape.

Endpoints already opened an explicit transaction and committed it. Nothing else did: the provisioning
middleware and every scheduled job and background worker relied on `SaveChangesAsync`'s implicit
transactions, so a run that failed halfway left its earlier saves committed.

The rule is now uniform: **whatever starts the work opens the transaction** - an endpoint, the provisioning
middleware, or a job - and the services it calls simply save as often as they need to. Those saves enlist in
the open transaction, so a service calling `SaveChangesAsync` twice is not a problem and never was.

### Where a transaction is deliberately absent

Three shapes make one wrong, and each carries a comment saying so at the class:

- **Remote calls in the loop.** `DeadlineReconciliationJob`, `AssignmentAutoDeleteJob`, `ProvisioningWorker`,
  `DownloadWorker`, `MoodleSyncWorker`, `NotificationWorker`. A transaction spanning a Forgejo call, an SMTP
  delivery or a multi-minute clone holds a database connection and its row locks for as long as that takes -
  so an unreachable Forgejo would block unrelated requests until it timed out. These are per-item units with
  a status column for recovery, and they are idempotent or retried.
- **Work that has already left the process.** A sent mail and a completed Moodle call cannot be rolled back.
  Rolling the row back would repeat the side effect, not undo it.
- **File deletion.** `DownloadCleanupJob` removes artifacts and then clears the paths. The files are gone
  before any commit, so a rollback would leave rows pointing at artifacts that no longer exist - worse than
  what it protects against.

`UserProvisioningPrimer` writes nothing at all.

### One asymmetry worth knowing

The provisioning middleware runs before the endpoints, so its transaction commits before the endpoint's even
begins. A user row and their class-roster membership therefore survive an endpoint that later rolls back.
That is correct - "this person exists and is in 5AHIF" is not part of whatever the request was doing - but it
is the one place where a write is not covered by the request's own transaction.

---

## 2026-09-01 — Class rosters are built from sign-ins until the LDAP sync exists

**Status:** a stopgap, and explicitly one.

A course needs a roster, and rosters of kind `Auto` are normally reconciled by the nightly LDAP sync, which
sees the whole school at once. Without directory credentials that sync never runs, which left the pilot with
**no rosters at all** and therefore no way to create a course — the feature simply looked broken.

`UserProvisioningService` now does incrementally what the sync does in bulk: a user who signs in is stored
with the class from their token, and joined to the `Auto` roster for that class, which is created the first
time somebody from it appears. Each class roster starts empty and fills as its students sign in.

Two bugs were in the way and are fixed with it: `ClaimUserData` carried the class and provisioning **never
wrote it to the user**, so `User.Class` was always null; and the membership work now shares the user's own
`SaveChangesAsync`, so a sign-in stays one transaction.

### What it is not

Not a replacement for the sync. A student who has never signed in does not exist here, so a roster is only
ever as complete as attendance, and **nobody is ever removed** — a user who leaves the class keeps their
membership until the real sync corrects it. When the sync is configured it adopts these same rosters,
matched on `ClassKey`, and its membership is authoritative from then on.

**What would change it:** LDAP credentials. See the app-managed-password entry below — the same credentials
also remove the second Forgejo account.

---

## 2026-09-01 — The interface is English, with no translation system

**Status:** accepted.

The shell carried a DE/EN switcher and the navigation held translation keys (`nav.home`, `nav.courses`)
against a translation system that was never built — so the navigation read `nav.home` on screen, while the
switcher changed nothing but date and number formatting. Every other label in the application is hardcoded
English.

Both are gone. Labels are plain English text where they are used, and `LOCALE_ID` is pinned to **de-AT**:
the interface language is English, but the users are at an Austrian school and dates, numbers and the euro
amounts should read the way they expect. It is one line in `app.config.ts` if that turns out to be wrong.

Adding real i18n later means a translation library and a pass over every template — not reviving these
keys, which covered only the eight navigation entries.

---

## 2026-09-01 — A 422 from Forgejo is not proof that the account exists

**Status:** accepted; the code enforces it.

`EnsureUserAsync` runs before every git-credential reset, so on all but the first call Forgejo answers
`422`/`409` — the account is already there. Treating those statuses as success was the obvious reading and
was wrong: Forgejo returns 422 for a **refused** request too, and the one that happens is a duplicate
e-mail, because addresses are `UNIQUE NOT NULL` across the whole instance.

The cost of guessing was not a wrong message but a wrong *place*: the create was reported as success, the
caller went on to `PATCH` a user that had never been created, and the operator saw a 404 against
`/admin/users/{name}` — with nothing logged, because neither response counted as a failure.

`EnsureUserAsync` now asks `GET /api/v1/users/{name}` before calling a 422 a success. One extra request on a
path that is not hot, in exchange for an error that names its own cause.

**The operational half of this**, which the code cannot enforce: operator accounts (`leo-admin`, the bot)
must hold role addresses, never a person's. Whatever address they hold is one no app user can be created
with. `DEPLOY.md` step 5 says so at the point where the mistake gets made.

---

## 2026-09-01 — Volumes holding unrecoverable data are `external`, the rest are not

**Status:** accepted.

`forgejo_data`, `forgejo_db_data`, `backend_db_data` and `caddy_data` are declared `external: true`. Compose
neither creates nor deletes them; `DEPLOY.md` step 3b creates them once with `podman volume create`.

### Why

Two failure modes, both cheap to hit and both unrecoverable without a restore:

1. **`podman compose down -v`.** It is part of the normal working rhythm here, not a rare event — the stack
   gets torn down and brought back regularly, and the `-v` takes every compose-managed volume with it. On an
   external volume it is a no-op.
2. **The project name is the directory name.** Compose prefixes volumes with its project name, which it
   derives from the directory it runs in. Extracting a bundle into `/opt/leoclassroom` instead of
   `~/leoclassroom` — or into a dated directory — makes compose look for `leoclassroom_backend_db_data`,
   not find it, create it empty, and start a stack with no data in it. Nothing fails; the databases are
   simply new. An explicit `name:` removes the directory from the equation entirely.

The names default to `${VOLUME_PREFIX:-leoclassrooms}_<volume>`, which is exactly what compose had already
created, so the change binds to the existing volumes and nothing had to be migrated.

The failure mode when a volume is genuinely absent is compose refusing to start:
`volume … declared as external, but could not be found`. That is strictly better than a silently empty
database.

### Why not the other two

`caddy_config` and `backend_downloads` hold nothing that cannot be rebuilt: Caddy's autosaved config, and
download artifacts with a 24-hour TTL. Making them external would add manual setup for no protection.

`caddy_data` is external even though it is *currently* empty — plain HTTP issues no certificates. It holds
the ACME account key and the issued certificates the moment this deployment gets a hostname, and Let's
Encrypt rate-limits re-issuance. Promoting it now costs one `podman volume create` and removes a thing to
remember at the same moment `PUBLIC_SCHEME` changes.

### Not to be confused with external *secrets*

`compose.yaml` deliberately does **not** use `external: true` secrets: `podman compose` delegates to Docker
Compose, where external secrets are a Swarm feature and are rejected outright. External *volumes* are plain
Compose spec and work normally. Same keyword, unrelated support.

---

## 2026-09-01 — The backend logs to stdout *and* to a bind-mounted file

**Status:** accepted.

Production carried a single Serilog File sink writing to `Logs/LeoClassroom-.log`. That path is relative, so
it resolved to `/app/Logs` inside the container: no volume, invisible from the host, and discarded on every
`--force-recreate`. `AddLogging` calls `ClearProviders()` before handing over to Serilog, so with no console
sink configured for Production, `podman compose logs backend` was empty as well. The first deployment
therefore produced **no readable log of any kind**, which is why the 502 on `git-credential/reset` could not
be diagnosed from the server.

Both sinks are now on in Production:

- **Console**, because that is what `podman compose logs` reads and what every runbook here assumes.
- **File**, at the absolute `/var/log/leo-classroom/`, daily rolling, 100 MB per file, 30 files retained.

The file sink is a **bind mount to `./logs`**, not a named volume. A named volume would match
`backend_downloads`, but rootless podman stores it under the user's container storage where the files sit
behind `podman unshare` — and the whole point is to read them with `tail -f`. The backend image sets no
`USER`, so it runs as root, which rootless podman maps to the invoking user; the files land owned by
whoever runs `podman compose`.

The Dockerfile creates `/var/log/leo-classroom` so that the sink still works unmounted. Serilog drops a sink
whose directory does not exist without saying so, which is exactly the failure this entry is about.

**What would change it:** shipping logs to a collector. Then the console sink is the only one that matters
and the file sink can go, along with the bind mount.

---

## 2026-08-31 — Forgejo accounts get an app-managed password instead of SSO

**Status:** accepted for the proof of concept, deliberately temporary.

### What we do

The backend is Forgejo administrator, so it creates each user's Forgejo account itself and sets a random
password on it (`IGitCredentialService`, `POST /api/me/git-credential/reset`). The password is shown to the
user **once**, on their account page, and is never stored — only `User.GitCredentialIssuedAt`, a timestamp.
Losing it means generating a new one, not recovering the old one. Users may change the password inside
Forgejo afterwards; nothing here tracks that, and generating a new one overrides it.

`Forgejo:AuthSourceId` stays `0`, which is what "local account, app-managed password" means to Forgejo.

### Why not SSO

Student repositories are private, so every user needs a credential for git over HTTPS. The original design
(see the `git-credential-oauth` note in `cli/README.md`) assumed Forgejo would delegate login to Keycloak,
so git would reuse the SSO session and nobody would manage a second password.

That needs a **second, confidential Keycloak client** for Forgejo, from the administrators of the school's
shared realm at `auth.htl-leonding.ac.at` — which we do not run. The shared `htlleonding-service` client
looks like a public client (the school's own demos use it with no secret and the implicit flow), and
Forgejo's OIDC provider needs a client id *and* secret for a server-side code exchange. An LDAP source in
Forgejo would also have worked and needs nothing from Keycloak, but needs LDAP bind credentials we did not
have either.

For a proof of concept that will change a lot over the year, taking an external dependency on another
team's identity infrastructure was not worth it. Everything the school has to do for us today is
**one line**: allow this deployment's redirect URI on the existing client.

### What this costs

- Users hold a second credential. It behaves like a GitHub personal access token, which is a familiar
  shape, but it is a second thing to lose.
- We are responsible for its lifecycle: issue, reset, and revocation on soft-delete (the last one already
  happens through `SetUserActiveAsync`).
- The credential travels over whatever `PUBLIC_SCHEME` is. On `http` (see the IP-only decision below) it is
  shown over plain HTTP on the school LAN.

### One set of LDAP credentials removes all of this

**We do not have LDAP access today**, and that single missing thing costs us twice over:

1. The **nightly directory sync** (`LdapSyncService`) never runs, so no user data and no class rosters
   arrive on their own — see "Class rosters are built from sign-ins" below for the stopgap.
2. Forgejo has to keep **its own local account** for every user, with a password this application generates,
   shows once and cannot recover.

Both are the same missing dependency. Given a bind DN and password for the school's directory, Forgejo can
take its accounts straight from LDAP with `forgejo admin auth add-ldap` (`DEPLOY.md` step 7): accounts are
created on first login, users sign in to Forgejo and to git over HTTPS with **their ordinary school
password**, and the whole app-managed credential — the account page section, `GitCredentialIssuedAt`, the
reset endpoint, the once-only display — stops being needed. `GitCredentialService.ResetAsync` already
refuses to issue a password once `Forgejo:AuthSourceId` is non-zero, so the switch is configuration, not
code.

That makes LDAP credentials the highest-value thing to ask the school for. It is also a *smaller* ask than
the Keycloak alternative: an LDAP source needs nothing from the Keycloak administrators, no second
confidential client, and no coordination with another team's identity infrastructure.

### How to undo it

Deliberately a one-way door that is easy to walk back through:

1. Get either an LDAP source or an OIDC client, and add it with `forgejo admin auth add-ldap` /
   `add-oauth` (`DEPLOY.md` step 7).
2. Put the source id in `FORGEJO_AUTH_SOURCE_ID` and recreate the backend.
3. The next LDAP sync re-binds existing accounts to that source. The generated passwords simply stop being
   consulted; `GitCredentialService.ResetAsync` already refuses to issue one while a source is configured,
   and the account page tells users their password now comes from the school login.
4. Update the account page's instructions and the `git-credential-oauth` note in `cli/README.md`.

Nothing needs migrating and no data is orphaned — `GitCredentialIssuedAt` just stops advancing.

---

## 2026-08-31 — The SPA had never been run before this deployment

**Status:** note for whoever picks this up.

The Angular app compiled cleanly from the start, so it looked finished. It had never been **loaded in a
browser**, and the first attempt failed at bootstrap with `NG0201` — `withAutoRefreshToken` depends on
`AutoRefreshTokenService` and `UserActivityService`, which it does not provide itself; they must be listed
in `provideKeycloak({ providers: [...] })`. A compile-time-clean build says nothing about the injector
graph.

Treat "it builds" and "it runs" as separate claims for this project. The backend has real integration tests
(`LeoClassroom.TestInt`); the SPA has none, so the browser is currently the only thing that exercises it.
If it grows, a smoke test that bootstraps the app config would have caught this in CI.

---

## 2026-08-31 — Base image versions, audited

**Status:** current as of this date; re-check when the next LTS of each lands.

| Image | Pinned | Status |
|---|---|---|
| `codeberg.org/forgejo/forgejo` | `15` | LTS to 2027-07-15. Was `11`, EOL since 2026-07-16. |
| `docker.io/library/postgres` | `18-alpine` | Current major. Was `16`. |
| `mcr.microsoft.com/dotnet/*` | `10.0` | LTS to 2028-11-14. .NET 11 not released. |
| `docker.io/library/node` | `24-alpine` | Active LTS. Build-time only, never shipped. |
| `docker.io/library/caddy` | `2-alpine` | Major tag tracks 2.11.x, the current line. No Caddy 3. |
| `docker.io/library/alpine` | `3.23` | Supported to 2027-11-01. First release carrying `postgresql18-client`. |

Two dates to diary: **Node 26 becomes LTS on 2026-10-28** (bump then; Node 24 leaves active support
2026-10-20, though it only builds the bundle so the risk is low), and **Alpine 3.24** exists since
2026-06-09 — no reason to move while 3.23 is supported and carries the Postgres 18 client.

Floating major tags (`caddy:2-alpine`, `node:24-alpine`, `postgres:18-alpine`, `forgejo:15`) pick up patch
and security releases on `podman compose pull`. That is deliberate: pinning digests would mean a manual step
for every CVE, which nobody will do on a proof of concept.

---

## 2026-08-31 — Forgejo pinned to the LTS (v15), not to the newest release

**Status:** accepted; revisit each January when the next LTS appears.

`compose.yaml` pinned `forgejo:11`, which reached **end of life on 2026-07-16** and receives no security
fixes. It was bumped to **v15**, the current LTS, supported until **2027-07-15** — one school year of
coverage.

Not v16, even though it is newer. Forgejo ships a stable release every quarter with **three months** of
support, and an LTS in the first quarter of each year with **fifteen months**. v16.0 is supported only to
2026-10-29, so tracking stable means a forced major upgrade mid-term, during teaching. The LTS line is the
right cadence for a school deployment.

The tag is the major (`:15`), not a patch, so `podman compose pull forgejo` picks up security patches within
the line without another decision.

Upgrading was free here because it happened before any repositories existed. Once there is data, follow the
pre-upgrade procedure in `RESTORE.md`: Forgejo self-migrates its database on a version bump and there is no
automated rollback.

---

## 2026-08-31 — Roles come from `ldap_entry_dn`, admins from configuration

**Status:** forced by the shared realm; revisit only if we get our own.

The `htlleonding` realm carries **no roles claim**. A user's group is readable from `ldap_entry_dn`
(`OU=Students` / `OU=Teachers` / `OU=TestUsers`), which is what the school's own `LeoAuth` samples key on,
and `AuthClaims.ReadRoles` does the same. `OU=TestUsers` maps to Student — least privilege.

The realm has **no administrator concept at all**, so administrators are named by username in
`Keycloak:AdminUsers` (`ADMIN_USER_0`, `ADMIN_USER_1`, … in `.env`). Everyone listed there gets the audit
log, the LDAP sync trigger and permanent user deletion.

The realm also sets **no `aud`** on access tokens, so `Keycloak:ValidateAudience` is off — as in the school's
own sample. Issuer, signature and lifetime are still validated.

Consequence for the SPA: it must not read roles from the token either. `GET /api/me` returns the roles the
backend actually enforces with, and that is what the shell and the route guards use. The
`ldap_entry_dn` derivation in `core/auth/leo-roles.ts` exists only to render the shell before `/api/me`
answers — it never authorises anything.

---

## 2026-08-31 — Served over plain HTTP on a bare IP

**Status:** accepted for the proof of concept; fix before real use.

The server has an IP (`10.191.18.44`) and no hostname. No CA issues certificates for a bare address, so
`PUBLIC_SCHEME=http`. Combined with the implicit flow the realm requires, the access token comes back in the
URL fragment over plain HTTP on the school LAN, and the git password above is shown over the same.

Acceptable for a pilot on an internal network. **Not acceptable once this holds real submissions.**

The fix is a hostname — even a DDNS one. Then set `PUBLIC_SCHEME=https` and change nothing else: the
Caddyfile site addresses, `General:ClientOrigin`, Forgejo's `ROOT_URL` and the SPA's config all derive from
it. Caddy's internal CA is the other option but is worse here: browsers warn, and the backend's own
`git clone` would have to trust that CA too.

Forgejo needs its own entry point and there is no second hostname to give it, so it is on
`FORGEJO_PORT` (3000) rather than a name. With a hostname it can move back to 443.
