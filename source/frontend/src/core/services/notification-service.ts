import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { z } from 'zod';
import { BackendServiceBase } from './backend-service-base';

@Injectable({
  providedIn: 'root'
})
export class NotificationService extends BackendServiceBase {
  protected override get controller(): string {
    return 'notifications';
  }

  public async getPreferences(): Promise<NotificationPreference | null> {
    try {
      const response = await firstValueFrom(this.http.get<unknown>(this.buildUrl('preferences')));

      return notificationPreferenceZod.parse(response);
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        return null;
      }
      throw error;
    }
  }

  public async updatePreferences(preference: NotificationPreference): Promise<NotificationPreference | null> {
    try {
      const response = await firstValueFrom(
        this.http.put<unknown>(this.buildUrl('preferences'), preference));

      return notificationPreferenceZod.parse(response);
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        return null;
      }
      throw error;
    }
  }
}

const notificationPreferenceZod = z.object({
  newAssignment: z.boolean(),
  deadlineChanged: z.boolean()
});
export type NotificationPreference = z.infer<typeof notificationPreferenceZod>;
