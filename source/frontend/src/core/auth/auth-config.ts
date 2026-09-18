/**
 * Runtime configuration.
 *
 * These values differ per deployment, so they are fetched from `/config.json` before the application
 * bootstraps rather than compiled into the bundle. The container writes that file from its environment on
 * boot, which means one built image serves every environment and changing a hostname needs no rebuild.
 *
 * The defaults below are the local development setup and are used when `/config.json` is absent.
 */
export interface RuntimeConfig {
  /** Origin of the API. Empty means "same origin as this page", which is how the stack is deployed. */
  readonly backendOrigin: string;
  readonly keycloak: {
    readonly url: string;
    readonly realm: string;
    readonly clientId: string;
    /**
     * The HTL Leonding realm's shared client returns 401 when the standard flow exchanges the auth code,
     * so it is driven with the implicit flow. Switch to 'standard' against a realm that supports it.
     */
    readonly flow: 'standard' | 'implicit' | 'hybrid';
    /**
     * Minutes of user inactivity after which the SPA logs out. This is the only one of the three limits on
     * a session that we control: the access token's lifespan and the SSO session's idle and maximum
     * lifetimes belong to the realm, and on the shared realm we do not administer them. Under the implicit
     * flow there is no refresh token either, so a session can end sooner than this no matter what it says.
     */
    readonly sessionTimeoutMinutes: number;
  };
}

const developmentDefaults: RuntimeConfig = {
  backendOrigin: 'http://localhost:5080',
  keycloak: {
    url: 'http://localhost:8080',
    realm: 'leo',
    clientId: 'leo-classroom-frontend',
    flow: 'implicit',
    sessionTimeoutMinutes: 360
  }
};

let current: RuntimeConfig = developmentDefaults;

/** Loads `/config.json`. Call once, before bootstrapping the application. */
export async function loadRuntimeConfig(): Promise<void> {
  try {
    const response = await fetch('/config.json', { cache: 'no-store' });
    if (!response.ok) {
      return;
    }

    const loaded = await response.json() as Partial<RuntimeConfig>;
    current = {
      backendOrigin: loaded.backendOrigin ?? developmentDefaults.backendOrigin,
      keycloak: { ...developmentDefaults.keycloak, ...loaded.keycloak }
    };
  } catch {
    // no config.json (local `ng serve`) or malformed: keep the development defaults
  }
}

/** Origin the API is served from; the page's own origin when the deployment serves both together. */
export function backendOrigin(): string {
  return current.backendOrigin.length > 0 ? current.backendOrigin.replace(/\/$/, '') : window.location.origin;
}

export function backendApiBaseUrl(): string {
  return `${backendOrigin()}/api`;
}

export function keycloakConfig(): RuntimeConfig['keycloak'] {
  return current.keycloak;
}
