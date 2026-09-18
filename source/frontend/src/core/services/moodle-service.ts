import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { z } from 'zod';
import { BackendServiceBase } from './backend-service-base';

export type MoodleResult<T> = T | 'forbidden' | 'not-found' | null;

@Injectable({
  providedIn: 'root'
})
export class MoodleService extends BackendServiceBase {
  protected override get controller(): string {
    return 'courses';
  }

  public async get(courseId: number): Promise<MoodleResult<MoodleLink>> {
    try {
      const response = await firstValueFrom(this.http.get<unknown>(this.buildUrl(`${courseId}/moodle-link`)));

      return moodleLinkZod.parse(response);
    } catch (error) {
      return this.mapError(error);
    }
  }

  public async set(courseId: number, request: MoodleLinkWrite): Promise<MoodleResult<MoodleLink>> {
    try {
      const response = await firstValueFrom(
        this.http.put<unknown>(this.buildUrl(`${courseId}/moodle-link`), request));

      return moodleLinkZod.parse(response);
    } catch (error) {
      return this.mapError(error);
    }
  }

  public async disable(courseId: number): Promise<MoodleResult<'ok'>> {
    try {
      await firstValueFrom(this.http.delete(this.buildUrl(`${courseId}/moodle-link`)));

      return 'ok';
    } catch (error) {
      return this.mapError(error);
    }
  }

  private mapError(error: unknown): 'forbidden' | 'not-found' | null {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 403) {
        return 'forbidden';
      }
      if (error.status === 404) {
        return 'not-found';
      }

      return null;
    }
    throw error;
  }
}

export interface MoodleLinkWrite {
  enabled: boolean;
  moodleBaseUrl: string;
  moodleCourseId: number;
  token: string | null;
}

const moodleLinkZod = z.object({
  enabled: z.boolean(),
  moodleBaseUrl: z.string(),
  moodleCourseId: z.int(),
  hasToken: z.boolean()
});
export type MoodleLink = z.infer<typeof moodleLinkZod>;
