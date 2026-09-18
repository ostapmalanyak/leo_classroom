#!/bin/sh
# Applies the EF migration bundle to completion and exits 0 on success (non-zero aborts stack startup).
# The connection string is a Tier-1 secret mounted by KeyPerFile convention at /run/secrets.
set -eu

CONN_FILE="/run/secrets/ConnectionStrings__Postgres"
if [ ! -f "$CONN_FILE" ]; then
  echo "migrator: missing $CONN_FILE" >&2
  exit 1
fi

# also exported, so the design-time context factory can configure the provider before --connection
# overrides the value it used
ConnectionStrings__Postgres="$(cat "$CONN_FILE")"
export ConnectionStrings__Postgres

echo "migrator: applying EF migration bundle"
./efbundle --connection "$ConnectionStrings__Postgres"
echo "migrator: migrations applied"
