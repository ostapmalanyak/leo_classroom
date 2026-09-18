import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { z } from 'zod';
import { BackendServiceBase } from './backend-service-base';

const forgejoCheckZod = z.object({
  name: z.string(),
  passed: z.boolean(),
  detail: z.string()
});

const forgejoHealthZod = z.object({
  ok: z.boolean(),
  baseUrl: z.string(),
  checks: forgejoCheckZod.array()
});

export type ForgejoCheck = z.infer<typeof forgejoCheckZod>;
export type ForgejoHealthReport = z.infer<typeof forgejoHealthZod>;

/**
 * "Can the application still drive Forgejo?" - the question that took a morning to answer by reading logs
 * the first time it mattered.
 */
@Injectable({
  providedIn: 'root'
})
export class ForgejoHealthService extends BackendServiceBase {
  protected override get controller(): string {
    return 'admin/forgejo';
  }

  public async getHealth(): Promise<ForgejoHealthReport | null> {
    try {
      const response = await firstValueFrom(this.http.get<unknown>(this.buildUrl('health')));

      return forgejoHealthZod.parse(response);
    } catch {
      // the report itself is the diagnosis; a failure to fetch it is a separate problem the caller reports
      return null;
    }
  }
}
