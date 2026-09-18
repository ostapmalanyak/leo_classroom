# LEO Classroom

GitHub-Classroom-style assignment handling for HTL Leonding, built on a self-hosted Forgejo. A teacher
creates a course and an assignment; every student who accepts it gets their own private repository, seeded
from the starter material; the deadline is enforced by taking write access away rather than by trusting a
timestamp; the teacher pulls every submission down as one archive and answers with a feedback pull request.

**Read this file first, then the guide for the part you are touching.** The backend has considerably more
detail of its own in `backend/AGENTS.md` - it is binding for everything under `backend/`, and this file does
not repeat it. Choices that are deliberate but not readable from the code are in `DECISIONS.md`, newest
first: consult it before re-litigating a design, and add to it when you make one.

## Layout

| Path | What it is |
|---|---|
| `backend/` | ASP.NET Core 10 minimal API, four projects plus two test projects. Own rules in `backend/AGENTS.md`. |
| `frontend/` | Angular 21 SPA (Material, Keycloak, zod). |
| `cli/` | `leo-cli`, a single-file native teacher CLI that publishes the current folder as an assignment. |
| `tests/OfflineChecks/` | Cross-cutting regression checks that compile production sources directly and need no packages or network. |
| `compose.yaml`, `Caddyfile`, `init-secrets.sh` | The Podman/compose deployment. Caddy is the only service that publishes host ports. |
| `DEPLOY.md`, `RESTORE.md`, `SECRETS.md`, `DECISIONS.md` | Operating and decision documentation. |

Three systems sit behind the application and none of them may be assumed reachable in a test: **Forgejo**
(git hosting, the source of truth for repositories), **Keycloak** (authentication; the school's LDAP is
behind it), and **Moodle** (optional grade-item sync, per course).

## Rules that hold everywhere

### Expected alternatives are a union, and a union is read by matching

An operation that can legitimately end more than one way returns `OneOf<...>` - never an exception, a
`null`, a `bool`, or a magic value. Every one of those outcomes is then read with **`Match`** (it produces a
value) or **`Switch`** (it only has side effects).

**Positional accessors - `IsT0`, `IsT1`, `AsT2` and friends - are not used anywhere in this repository,
including tests.** They keep compiling when a case is inserted into a union and silently name a different
case from then on, which is exactly how a failure gets handled as a success. `Match` and `Switch` force
every case to be named, so the same change becomes a compile error at each place that has to decide about
it. `backend/AGENTS.md` has the section **Reading a `OneOf` outcome** with the supporting helpers
(`Outcome.Failure`, `Util/Outcomes`, and `ShouldBe<TCase>()` for tests) and worked examples.

### The identity is the IF number

`preferred_username` from Keycloak - the IF number - is the caller's identity everywhere: backend
ownership checks, Forgejo account names, CLI authorization. Never fall back to a display name.

### Nothing outside the process is assumed to work

Every call to Forgejo, Moodle, LDAP, SMTP or `git` returns a domain error type instead of throwing, is
retried or recorded rather than rolled back, and reaches the client as a 502 with no internal detail in it.
A transaction never spans one of those calls.

### Secrets are files, never arguments

Secrets arrive as files under `/run/secrets` (created by `init-secrets.sh`). A credential never goes on a
command line - `/proc/<pid>/cmdline` is world-readable - and never into a URL, a log line, or a stored
error string.

### Time is NodaTime

Domain time is NodaTime with an injected `IClock`; the school's zone is `Const.TimeZone`
(`Europe/Vienna`). The frontend uses `@js-joda/core` for the same reason. No `DateTime.Now`.

### Documentation is part of the change

A behaviour change that a reader could not infer from the code belongs in `DECISIONS.md`; a rule that a
future agent must follow belongs in the relevant `AGENTS.md`. Comments explain *why*, not *what*.

## Backend (`backend/`)

`backend/AGENTS.md` is the authority. The short version: `LeoClassroom` is transport and composition,
`LeoClassroom.Services` holds the workflows and the integrations, `LeoClassroom.Persistence` holds EF
entities and repositories, `LeoClassroom.Shared` holds small cross-layer primitives, and dependencies only
ever point inward. Endpoints own validation, the transaction boundary and the HTTP mapping; services own
the business rules and the data-level authorization; repositories only query and stage.

Long-running work is a `BackgroundService` or a Quartz job and an endpoint answers 202. `IDeletionCascade`
is the only thing that deletes a course, an assignment or an acceptance, because each of those rows names
something in Forgejo that becomes unreachable once the row is gone.

Build and test with `dotnet build backend/LeoClassroom.slnx`; unit tests are `LeoClassroom.Test`,
HTTP/database integration tests are `LeoClassroom.TestInt` (Testcontainers, so they need Docker/Podman).

## Frontend (`frontend/`)

Angular 21, standalone components, Angular Material, `keycloak-angular` for the session. Feature folders
under `src/app/`, cross-cutting code under `src/core/` (`auth/`, `services/`, `util/`).

- HTTP services extend `BackendServiceBase` and expose one method per endpoint; they do not build URLs by
  hand elsewhere.
- Responses are validated with **zod** (`core/util/zod-schemas.ts`) before they reach a component - the
  backend contract is checked at the boundary, not assumed.
- Markdown is rendered through `core/util/markdown.ts`, which sanitises with DOMPurify. Never bind
  untrusted HTML directly.
- Roles derived in the SPA (`core/auth/leo-roles.ts`, from `ldap_entry_dn`) exist **only** to render the
  shell before `/api/me` answers. They authorise nothing; the backend's own derivation does.
- The container writes `/config.json` at boot, so one built image serves every environment and a hostname
  change needs no rebuild.

## CLI (`cli/`)

A published-as-native, single-file teacher tool. It adds **no privileged capability**: every action is one
the teacher could already perform in the web app, authorized by the backend the same way.

- Device-code login against Keycloak; the token cache is written with owner-only permissions.
- `CliConfig.ExpectedApiVersion` is checked against `/api/version` at startup, and the CLI refuses to
  run against an incompatible backend. Bump both when the contract changes.
- git talks to Forgejo over HTTPS with the password issued by the web app. No token is ever embedded in a
  remote URL or written to a log.

## Deployment

Podman/compose. `caddy` is the only service publishing host ports; everything else is reachable only on the
internal `leo` network, and Forgejo runs with SSH disabled. Startup order is enforced through healthchecks:
databases -> migrator -> forgejo + backend -> frontend + caddy. `SchemaGuard` refuses to serve traffic
against an unmigrated database, so a schema change needs its migration committed with it.

## Before you finish

- `dotnet build backend/LeoClassroom.slnx` clean, with the warnings-as-errors and Leo analyzers intact -
  fix warnings, do not suppress them.
- Run the affected `LeoClassroom.Test` and, where the change touches routing, binding or persistence,
  `LeoClassroom.TestInt`.
- `cd frontend && npm run build` for frontend changes.
- The offline checks in `tests/OfflineChecks/` when you touch the CLI, the LDAP/OU parsing, or the
  repository path guards.
- Never weaken a production check or a test to get a green build.
