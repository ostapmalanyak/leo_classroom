# LEO Classroom Teacher CLI (`leo-cli`)

Publish the current folder as an assignment in one guided flow: log in, pick a course, enter a
title/deadline/instructions, push the folder as the starter repo, create the assignment, and get the accept
link. It adds **no new privileged capability** — every action is one a teacher can already do in the web app,
authorized by the backend the same way.

## Download

Get the binary for your platform from the **Teacher CLI** page in the web app (it is a single,
self-contained native executable — no .NET runtime needed). Each binary targets a specific backend API
version and refuses to run against an incompatible backend.

| Platform | RID |
|---|---|
| Windows x64 | `win-x64` |
| Linux x64 | `linux-x64` |
| macOS (Apple Silicon) | `osx-arm64` |

`git` must be on your `PATH` with HTTPS access to Forgejo. Get your Forgejo username and password from
**My account** in the web app — the app generates it and shows it once. No token is ever embedded in a URL
or written to logs. See **Git credentials** below for storing it so git stops asking.

## Git credentials

Adding a Forgejo credential cannot disturb the ones you already have, including GitHub over SSH. The two
are unrelated mechanisms: SSH authenticates with a key pair from `~/.ssh`, HTTPS asks a **credential
helper** for a username and password. Nothing you configure here is read when you push to an SSH remote.

Helpers are keyed by **protocol, host and port**, so one helper holds many credentials and git picks the
matching one per remote. GitHub over HTTPS and Forgejo can sit side by side with no ambiguity.

This deployment runs Forgejo with SSH switched off (`DISABLE_SSH: true`), so HTTPS with the generated
password is the only route to it.

### Store it in the OS keychain

Set a helper once, globally, and let it hold every host:

```sh
# Fedora / RHEL
sudo dnf install git-credential-libsecret
git config --global credential.helper /usr/libexec/git-core/git-credential-libsecret

# Debian / Ubuntu
sudo apt install libsecret-1-0 libsecret-1-dev
git config --global credential.helper /usr/share/doc/git/contrib/credential/libsecret/git-credential-libsecret

# macOS
git config --global credential.helper osxkeychain

# Windows
git config --global credential.helper manager
```

The next clone or push over HTTPS asks once and stores the answer, encrypted, under that host. Saving the
Forgejo password there does not touch a GitHub entry, and vice versa.

Prefer `libsecret`, `osxkeychain` or `manager` over `credential.helper store`, which writes the password in
plain text to `~/.git-credentials`. On a shared or backed-up machine that file is a liability.

### Pin the helper and username to Forgejo only

If you would rather not change your global helper, scope it to this host. The config key is the remote's
protocol, host **and port**, exactly as it appears in the clone URL:

```sh
git config --global credential.http://10.191.18.44:3000.helper \
  /usr/libexec/git-core/git-credential-libsecret
git config --global credential.http://10.191.18.44:3000.username EDUFS-USER
```

The `username` line means git only ever prompts for the password.

### Two accounts on the same host

Host-level keying is not enough when one host holds two identities. Make git include the repository path in
the lookup:

```sh
git config --global credential.https://git.example.com.useHttpPath true
```

Credentials are then stored per repository path rather than per host. Putting the user in the remote URL —
`https://someone@git.example.com/org/repo.git` — works too and is the more explicit option for a single
repository.

### While the deployment is on plain HTTP

`http://` means the password crosses the network unencrypted. That is accepted for the pilot on the school
LAN and is why `DECISIONS.md` treats the missing hostname as the thing to fix before real use. Some helpers
warn when asked to store a credential for an `http://` remote; that warning is correct.

## Usage

```sh
cd path/to/assignment-material
leo-cli
```

1. **Login** — the tool prints a Keycloak verification URL and a code; approve it in your browser. The
   token is cached (owner-only) and refreshed silently next time. The backend's `/api/me` checks that the
   account is active and has teacher or administrator access; no roles claim is required in the token.
2. **Pick a course** from your courses.
3. **Enter** a title, an optional deadline (none/soft/hard; a hard deadline also asks whether it revokes
   read access; parsed in the school timezone), and optional markdown instructions.
4. **Confirm** — nothing remote happens until you confirm.
5. **Publish** — prepares git in the folder (if a `.git` already exists it asks whether to push as-is or
   start fresh; it never wipes history without your choice), pushes the Forgejo source repo over HTTPS,
   creates the assignment, and prints the **accept link** + repo URL.

The CLI appends its exclusions to `.gitignore`, preserving the project's existing rules for secrets and
build output.

If a step fails it reports which steps completed and is safe to re-run with the same inputs.

## Configuration (non-secret)

Endpoints default to localhost dev values; override via environment variables:

| Variable | Default | Meaning |
|---|---|---|
| `LEO_BACKEND_URL` | `http://localhost:5080` | Classroom backend base URL |
| `LEO_FORGEJO_URL` | `http://localhost:3000` | Forgejo base URL (for the source repo push) |
| `LEO_KEYCLOAK_URL` | `http://localhost:8080` | Keycloak base URL |
| `LEO_REALM` | `leo` | Keycloak realm |
| `LEO_CLIENT_ID` | `leo-classroom-cli` | Public device-grant client id |

## Build

```sh
dotnet build application/cli                 # IL build + AOT analyzers
dotnet publish application/cli -r linux-x64  # self-contained, trimmed, AOT single file (per RID)
```

`PublishAot`/`PublishTrimmed`/`SelfContained`/`InvariantGlobalization` are set in the csproj; the published
binary runs with no .NET runtime installed. (A per-RID AOT publish needs that platform's native toolchain.)
