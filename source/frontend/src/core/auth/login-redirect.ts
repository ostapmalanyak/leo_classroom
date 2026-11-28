/**
 * Where Keycloak sends the user back to after signing in.
 *
 * Only the origin and the path. keycloak-js defaults to the whole `href`, and Keycloak itself puts
 * `#state=…&session_state=…&id_token=…` into the fragment, so a second login can carry a fragment inside the
 * `redirect_uri`. Keycloak stores a rejected authorization request in its `KC_RESTART` cookie, and logging out
 * does not clear that cookie: the browser then fails with "Invalid parameter: redirect_uri" on every attempt
 * until its cookies are deleted.
 *
 * The cost is that query-string filters are not restored after the round-trip. Deep links — the route paths —
 * are.
 */
export function loginRedirectUri(): string {
  return `${window.location.origin}${window.location.pathname}`;
}
