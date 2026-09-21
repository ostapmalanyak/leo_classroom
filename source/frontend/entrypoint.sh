#!/bin/sh
# Writes the deployment's runtime configuration for the SPA, then serves it.
#
# The Angular bundle fetches /config.json before bootstrapping, so the same built image works in every
# environment: changing a hostname or a Keycloak realm needs a restart, not a rebuild.
set -eu
: "${KEYCLOAK_URL:?KEYCLOAK_URL is required}"
: "${KEYCLOAK_REALM:?KEYCLOAK_REALM is required}"
: "${KEYCLOAK_FRONTEND_CLIENT_ID:?KEYCLOAK_FRONTEND_CLIENT_ID is required}"
KEYCLOAK_FLOW="${KEYCLOAK_FLOW:-implicit}"
SESSION_TIMEOUT_MINUTES="${SESSION_TIMEOUT_MINUTES:-360}"
# backendOrigin stays empty: Caddy serves the SPA and proxies /api on the same origin, so the browser talks
# to its own origin and no CORS preflight is involved.
cat > /srv/config.json <<JSON
{
  "backendOrigin": "",
  "keycloak": {
    "url": "${KEYCLOAK_URL}",
    "realm": "${KEYCLOAK_REALM}",
    "clientId": "${KEYCLOAK_FRONTEND_CLIENT_ID}",
    "flow": "${KEYCLOAK_FLOW}",
    "sessionTimeoutMinutes": ${SESSION_TIMEOUT_MINUTES}
  }
}
JSON
echo "frontend: config.json written for realm '${KEYCLOAK_REALM}' at ${KEYCLOAK_URL}"
exec caddy run --config /etc/caddy/Caddyfile --adapter caddyfile
