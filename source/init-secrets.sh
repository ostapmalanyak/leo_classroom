#!/usr/bin/env bash
# Prepares the Tier-1 secret files in ./secrets, which compose mounts read-only at /run/secrets.
#
#   ./init-secrets.sh --scaffold   create ./secrets with generated values for the generatable secrets
#                                  and REPLACE_ME placeholders for the externally-sourced ones
#   ./init-secrets.sh              verify every required secret exists and is non-empty, and fix permissions
#
# `secrets/` is .gitignored and MUST NOT be committed. See SECRETS.md for the inventory + rotation.
#
# These are plain files rather than `podman secret create` entries on purpose: `external: true` secrets are
# a Docker Swarm feature, and the compose implementations in use here reject them.
set -euo pipefail

SECRETS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/secrets"

# The complete Tier-1 inventory. "optional" secrets may be absent (feature disabled), but compose still
# needs the file to exist to mount it, so the scaffold writes an empty placeholder for them.
REQUIRED=(
  ConnectionStrings__Postgres
  Forgejo__AdminToken
  Forgejo__WebhookSecret
  DataProtection__MasterKey
  backend_db_password
  forgejo_db_password
)
OPTIONAL=(
  Ldap__BindPassword
  Smtp__Password
  Keycloak__ForgejoClientSecret
  restic_password
  backup_offsite_credentials
)

# No trailing newline: the value is the whole file, and a stray \n ends up inside an HTTP header or a
# connection string.
write() { printf '%s' "$2" > "$SECRETS_DIR/$1"; }

scaffold() {
  mkdir -p "$SECRETS_DIR"
  chmod 700 "$SECRETS_DIR"

  [ -f "$SECRETS_DIR/Forgejo__WebhookSecret" ]    || write Forgejo__WebhookSecret "$(openssl rand -hex 32)"
  [ -f "$SECRETS_DIR/restic_password" ]           || write restic_password        "$(openssl rand -hex 32)"
  [ -f "$SECRETS_DIR/backend_db_password" ]       || write backend_db_password    "$(openssl rand -hex 32)"
  [ -f "$SECRETS_DIR/forgejo_db_password" ]       || write forgejo_db_password    "$(openssl rand -hex 32)"
  # 256-bit base64 AES-GCM master key for Tier-2 encryption.
  [ -f "$SECRETS_DIR/DataProtection__MasterKey" ] || write DataProtection__MasterKey "$(openssl rand -base64 32)"

  # A placeholder until Forgejo exists to mint a real one - see DEPLOY.md step 6. It is NOT a working token.
  [ -f "$SECRETS_DIR/Forgejo__AdminToken" ]       || write Forgejo__AdminToken "REPLACE_ME_AFTER_FORGEJO_STARTS"

  [ -f "$SECRETS_DIR/ConnectionStrings__Postgres" ] || write ConnectionStrings__Postgres \
    "Host=backend-db;Database=leoclassroom;Username=leoclassroom;Password=$(cat "$SECRETS_DIR/backend_db_password")"

  # Unused on the default setup (no LDAP, no mail, app-managed Forgejo passwords - see DECISIONS.md), but
  # the files must exist for compose to mount them.
  for name in "${OPTIONAL[@]}"; do
    [ -f "$SECRETS_DIR/$name" ] || write "$name" ""
  done

  harden
  echo "scaffolded $SECRETS_DIR"
  echo "  - ConnectionStrings__Postgres was built from BACKEND_DB_* defaults; check it matches your .env"
  echo "  - Forgejo__AdminToken is a placeholder; DEPLOY.md step 6 replaces it"
  echo "  - the optional secrets are empty; fill them only when you enable LDAP, mail or backups"
}

harden() {
  chmod 700 "$SECRETS_DIR"
  # 644 rather than 600: compose bind-mounts these files into containers that drop to their own user
  # (Forgejo runs as uid 1000), which cannot read a file owned by the host user. The 700 on the directory
  # is what keeps other users on the host out.
  find "$SECRETS_DIR" -type f -exec chmod 644 {} +
}

verify() {
  [ -d "$SECRETS_DIR" ] || { echo "missing $SECRETS_DIR - run './init-secrets.sh --scaffold' first" >&2; exit 1; }

  local failed=0
  for name in "${REQUIRED[@]}"; do
    if [ ! -f "$SECRETS_DIR/$name" ]; then
      echo "MISSING: secrets/$name" >&2
      failed=1
    elif [ ! -s "$SECRETS_DIR/$name" ]; then
      echo "EMPTY:   secrets/$name" >&2
      failed=1
    fi
  done

  for name in "${OPTIONAL[@]}"; do
    [ -f "$SECRETS_DIR/$name" ] || { echo "MISSING: secrets/$name (create it empty: touch secrets/$name)" >&2
                                     failed=1; }
  done

  if [ -s "$SECRETS_DIR/Forgejo__AdminToken" ] \
     && [ "$(cat "$SECRETS_DIR/Forgejo__AdminToken")" = "REPLACE_ME_AFTER_FORGEJO_STARTS" ]; then
    echo "note:    Forgejo__AdminToken is still the placeholder (expected before DEPLOY.md step 6)"
  fi

  if grep -rlq 'REPLACE_ME' "$SECRETS_DIR" 2>/dev/null; then
    echo "note:    some secrets still contain REPLACE_ME:"
    grep -rl 'REPLACE_ME' "$SECRETS_DIR" | sed 's|^|           |'
  fi

  [ "$failed" -eq 0 ] || { echo "secrets are incomplete" >&2; exit 1; }

  harden
  echo "secrets OK ($SECRETS_DIR)"
}

if [ "${1:-}" = "--scaffold" ]; then
  scaffold
else
  verify
fi
