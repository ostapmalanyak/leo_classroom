import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { z } from 'zod';
import { backendApiBaseUrl } from '../auth/auth-config';

/**
 * The caller's one-time git login setup.
 *
 * `configured` means the commands on the page were run on their computer, `authorized` means a git operation
 * already proved that the stored credential works. The token is the opaque part of their setup URL and
 * authorises nothing.
 */
export interface GitSetup {
  readonly configured: boolean;
  readonly authorized: boolean;
  readonly token: string;
  /** The Forgejo host and port, the key the git config is written under. */
  readonly host: string;
  /** The Forgejo OAuth2 application git-credential-oauth authenticates against. Public, not a secret. */
  readonly clientId: string;
}

const gitSetupZod = z.object({
  configured: z.boolean(),
  authorized: z.boolean(),
  token: z.string(),
  host: z.string(),
  clientId: z.string()
});

@Injectable({
  providedIn: 'root'
})
export class GitSetupService {
  private readonly http = inject(HttpClient);

  /** The app origin, not the API prefix: the done route is mounted outside /api. */
  private readonly origin = backendApiBaseUrl().replace(/\/api$/, '');

  public async get(): Promise<GitSetup | null> {
    try {
      const response = await firstValueFrom(
        this.http.get<unknown>(`${backendApiBaseUrl()}/me/git-setup`));

      return gitSetupZod.parse(response);
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        return null;
      }
      throw error;
    }
  }

  /**
   * Records that the user ran the commands. The page cannot see their computer, so this is their word, and it
   * only decides what the page shows: `authorized` still requires a real git operation.
   */
  public async markConfigured(token: string): Promise<boolean> {
    try {
      await firstValueFrom(this.http.post(`${this.origin}/api/git-setup/done/${token}`, null));

      return true;
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        return false;
      }
      throw error;
    }
  }
}
