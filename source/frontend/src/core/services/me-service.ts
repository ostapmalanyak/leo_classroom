import { computed, inject, Injectable, signal, Signal, WritableSignal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Instant } from '@js-joda/core';
import { z } from 'zod';
import { Role } from '../auth/roles';
import { backendApiBaseUrl } from '../auth/auth-config';
import { InstantSchema } from '../util/zod-schemas';

/**
 * The caller's account as the backend sees it.
 *
 * Roles come from here rather than from the token: the school realm carries no roles claim - a user's group
 * is derived from `ldap_entry_dn`, and administrators are named in server configuration, which is not in the
 * token at all. This endpoint is the only place the SPA and the backend agree on who someone is.
 */
export interface Me {
  readonly studentId: string;
  readonly schoolClass: string | null;
  readonly roles: readonly Role[];
  readonly gitCredentialIssuedAt: Instant | null;
  readonly gitCredentialManaged: boolean;
}

export interface IssuedGitCredential {
  readonly username: string;
  readonly password: string;
  readonly issuedAt: Instant;
}

const meZod = z.object({
  studentId: z.string(),
  class: z.string().nullable(),
  roles: z.array(z.enum(Role)),
  gitCredentialIssuedAt: InstantSchema.nullable(),
  gitCredentialManaged: z.boolean()
});

const issuedZod = z.object({
  username: z.string(),
  password: z.string(),
  issuedAt: InstantSchema
});

@Injectable({
  providedIn: 'root'
})
export class MeService {
  private readonly http = inject(HttpClient);

  private readonly me: WritableSignal<Me | null> = signal(null);
  private inFlight: Promise<Me | null> | null = null;

  public readonly value: Signal<Me | null> = this.me.asReadonly();
  public readonly roles: Signal<readonly Role[]> = computed(() => this.me()?.roles ?? []);

  /** Loads the account once and caches it; concurrent callers share the same request. */
  public load(): Promise<Me | null> {
    this.inFlight ??= this.fetch();

    return this.inFlight;
  }

  public reload(): Promise<Me | null> {
    this.inFlight = this.fetch();

    return this.inFlight;
  }

  /** Issues a new random git password. Shown once and never stored, so it cannot be fetched again. */
  public async resetGitCredential(): Promise<IssuedGitCredential> {
    const response = await firstValueFrom(
      this.http.post<unknown>(`${backendApiBaseUrl()}/me/git-credential/reset`, {}));
    const issued = issuedZod.parse(response);
    await this.reload();

    return issued;
  }

  private async fetch(): Promise<Me | null> {
    try {
      const response = await firstValueFrom(this.http.get<unknown>(`${backendApiBaseUrl()}/me`));
      const parsed = meZod.parse(response);
      const me: Me = {
        studentId: parsed.studentId,
        schoolClass: parsed.class,
        roles: parsed.roles,
        gitCredentialIssuedAt: parsed.gitCredentialIssuedAt,
        gitCredentialManaged: parsed.gitCredentialManaged
      };
      this.me.set(me);

      return me;
    } catch (error) {
      // A failed request is expected - not signed in yet, or the backend is unreachable - and the shell
      // falls back to the token. A *parse* failure is not: it means this schema and the backend's DTO have
      // drifted apart, and swallowing it silently renders every field as its empty state, which reads like
      // a dozen unrelated bugs rather than one. It is still not thrown, because `authGuard` awaits this and
      // an exception there would blank the application, but it must not pass unremarked.
      if (!(error instanceof HttpErrorResponse)) {
        console.error('GET /api/me did not match the expected shape - the frontend schema and the backend '
                      + 'DTO have drifted apart. Every field on this page will render empty.', error);
      }
      this.me.set(null);
      this.inFlight = null;

      return null;
    }
  }
}
