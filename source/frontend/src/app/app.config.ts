import {
  ApplicationConfig,
  DEFAULT_CURRENCY_CODE,
  LOCALE_ID,
  provideZonelessChangeDetection
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideDateFnsAdapter } from '@angular/material-date-fns-adapter';
import { registerLocaleData } from '@angular/common';
import localeDeAt from '@angular/common/locales/de-AT';
import {
  AutoRefreshTokenService,
  createInterceptorCondition,
  includeBearerTokenInterceptor,
  INCLUDE_BEARER_TOKEN_INTERCEPTOR_CONFIG,
  IncludeBearerTokenCondition,
  provideKeycloak,
  UserActivityService,
  withAutoRefreshToken
} from 'keycloak-angular';
import { routes } from './app.routes';
import { backendOrigin, keycloakConfig } from '../core/auth/auth-config';

registerLocaleData(localeDeAt, 'de-AT');

/**
 * Built after `loadRuntimeConfig` has run: the Keycloak provider and the bearer-token interceptor both need
 * the deployment's URLs at construction time, and those are only known once `/config.json` has been read.
 */
export function createAppConfig(): ApplicationConfig {
  const backendBearerCondition = createInterceptorCondition<IncludeBearerTokenCondition>({
    urlPattern: new RegExp(`^${backendOrigin().replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(/.*)?$`, 'i')
  });

  return {
    providers: [
      provideKeycloak({
        config: keycloakConfig(),
        initOptions: {
          onLoad: 'check-sso',
          // PKCE belongs to the standard flow; the implicit flow this realm requires has no code to protect
          flow: keycloakConfig().flow,
          ...(keycloakConfig().flow === 'standard' ? { pkceMethod: 'S256' as const } : {}),
          silentCheckSsoRedirectUri: `${window.location.origin}/silent-check-sso.html`
        },
        features: [
          // sessionTimeout is the idle window, in milliseconds. It was 60_000 - one minute - which logged
          // people out while they read the page they had just asked for.
          withAutoRefreshToken({
            onInactivityTimeout: 'logout',
            sessionTimeout: keycloakConfig().sessionTimeoutMinutes * 60_000
          })
        ],
        // withAutoRefreshToken depends on these two and does not provide them itself; without them the
        // Keycloak app initializer fails with NG0201 and nothing renders
        providers: [AutoRefreshTokenService, UserActivityService]
      }),
      {
        provide: INCLUDE_BEARER_TOKEN_INTERCEPTOR_CONFIG,
        useValue: [backendBearerCondition]
      },
      provideZonelessChangeDetection(),
      provideRouter(routes, withComponentInputBinding()),
      provideHttpClient(withInterceptors([includeBearerTokenInterceptor])),
      provideDateFnsAdapter(),
      // The interface is English and there is no translation system, but the users are at an Austrian
      // school: dates, numbers and the euro amounts below read natively in de-AT. Change this one line to
      // 'en-US' to format the American way; nothing else depends on it.
      { provide: LOCALE_ID, useValue: 'de-AT' },
      { provide: DEFAULT_CURRENCY_CODE, useValue: 'EUR' }
    ]
  };
}
