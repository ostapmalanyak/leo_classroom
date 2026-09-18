import { computed, effect, inject, Injectable, signal, Signal, WritableSignal } from '@angular/core';
import Keycloak from 'keycloak-js';
import { KEYCLOAK_EVENT_SIGNAL, KeycloakEventType, typeEventArgs, ReadyArgs } from 'keycloak-angular';
import { Role } from './roles';
import { rolesFromLdapEntryDn } from './leo-roles';
import { MeService } from '../services/me-service';

interface Identity {
  studentId: string;
  displayName: string;
  email: string | null;
  schoolClass: string | null;
}

@Injectable({
  providedIn: 'root'
})
export class SessionService {
  private readonly keycloak = inject(Keycloak);
  private readonly events = inject(KEYCLOAK_EVENT_SIGNAL);
  private readonly me = inject(MeService);

  private readonly state: WritableSignal<number> = signal(0);

  public readonly authenticated: Signal<boolean> = computed(() => {
    this.state();

    return this.keycloak.authenticated ?? false;
  });

  /**
   * The backend's answer once it has arrived, falling back to what the token itself allows deriving.
   *
   * This realm has no roles claim, and administrators are named in server configuration rather than in the
   * token, so `/api/me` is authoritative. The fallback only avoids an empty shell on the first paint.
   */
  public readonly roles: Signal<readonly Role[]> = computed(() => {
    this.state();
    const fromBackend = this.me.roles();
    if (fromBackend.length > 0) {
      return fromBackend;
    }

    return rolesFromLdapEntryDn(this.keycloak.tokenParsed?.['ldap_entry_dn'] as string | undefined);
  });

  public readonly identity: Signal<Identity | null> = computed(() => {
    this.state();
    const token = this.keycloak.tokenParsed;
    if (!this.authenticated() || token === undefined) {
      return null;
    }

    return {
      studentId: token['preferred_username'] as string,
      displayName: (token['name'] as string | undefined) ?? (token['preferred_username'] as string),
      email: (token['email'] as string | undefined) ?? null,
      schoolClass: (token['class'] as string | undefined) ?? null
    };
  });

  public readonly isTeacher: Signal<boolean> = computed(() => this.hasRole(Role.Teacher) || this.hasRole(Role.Admin));
  public readonly isAdmin: Signal<boolean> = computed(() => this.hasRole(Role.Admin));

  public constructor() {
    effect(() => {
      const event = this.events();
      if (event.type === KeycloakEventType.Ready
        || event.type === KeycloakEventType.AuthSuccess
        || event.type === KeycloakEventType.AuthRefreshSuccess
        || event.type === KeycloakEventType.AuthLogout) {
        if (event.type === KeycloakEventType.Ready) {
          typeEventArgs<ReadyArgs>(event.args);
        }
        this.state.update(v => v + 1);
        if (this.keycloak.authenticated === true) {
          void this.me.load();
        }
      }
    });
  }

  public hasRole(role: Role): boolean {
    return this.roles().includes(role);
  }

  public async login(): Promise<void> {
    await this.keycloak.login();
  }

  public async logout(): Promise<void> {
    await this.keycloak.logout({ redirectUri: window.location.origin });
  }
}
