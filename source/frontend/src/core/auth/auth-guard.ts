import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { inject } from '@angular/core';
import { AuthGuardData, createAuthGuard } from 'keycloak-angular';
import { Role } from './roles';
import { rolesFromLdapEntryDn } from './leo-roles';
import { MeService } from '../services/me-service';

export const authGuard: CanActivateFn = createAuthGuard<CanActivateFn>(
  async (route, _state, authData: AuthGuardData) => {
    const { authenticated, keycloak } = authData;

    // injected up front: `inject` only works inside the injection context, which is lost after the first
    // await below
    const meService = inject(MeService);
    const router = inject(Router);

    // An expired/absent session defers to Keycloak (silent refresh already attempted), then login.
    if (!authenticated) {
      await keycloak.login({ redirectUri: window.location.href });

      return false;
    }

    const required = route.data['role'] as Role | undefined;
    if (required === undefined) {
      return true;
    }

    // the backend is authoritative: this realm has no roles claim, and administrators are configured
    // server-side rather than carried in the token
    const me = await meService.load();
    const roles = me?.roles ?? rolesFromLdapEntryDn(keycloak.tokenParsed?.['ldap_entry_dn'] as string | undefined);
    if (roles.includes(required) || roles.includes(Role.Admin)) {
      return true;
    }

    // Authenticated but unauthorized → friendly forbidden page (the menu never offered this).
    const forbidden: UrlTree = router.createUrlTree(['/forbidden']);

    return forbidden;
  });
