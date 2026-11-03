import { HttpErrorResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { z } from 'zod';
import { BackendServiceBase } from './backend-service-base';

const syncOutcomeZod = z.object({
  created: z.int().nonnegative(),
  updated: z.int().nonnegative(),
  softDeleted: z.int().nonnegative(),
  reactivated: z.int().nonnegative(),
  destructivePassSkipped: z.boolean(),
  ldapExitCode: z.int().nullable().optional(),
  ldapResultCode: z.int().nullable().optional(),
  ldapError: z.string().nullable().optional()
});
export type LdapSyncOutcome = z.infer<typeof syncOutcomeZod>;

@Injectable({
  providedIn: 'root'
})
export class LdapSyncService extends BackendServiceBase {
  protected override get controller(): string {
    return 'admin/ldap-sync';
  }

  public async run(): Promise<LdapSyncOutcome | null> {
    try {
      const response = await firstValueFrom(this.http.post<unknown>(this.buildUrl(''), null));

      return syncOutcomeZod.parse(response);
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        return null;
      }
      throw error;
    }
  }
}
