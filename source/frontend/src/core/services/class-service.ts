import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, map, Observable } from 'rxjs';
import { z } from 'zod';
import { backendApiBaseUrl } from '../auth/auth-config';

const classRosterSchema = z.object({
  id: z.number().int().positive().transform(id => id.toString()),
  name: z.string().min(1),
  memberCount: z.number().int().nonnegative()
});

const createdClassSchema = z.object({
  id: z.number().int().positive(),
  title: z.string().min(1),
  rosterId: z.number().int().positive(),
  iconSeed: z.string().nullable().optional()
});

export type ClassRoster = z.infer<typeof classRosterSchema>;
export type CreatedClass = z.infer<typeof createdClassSchema>;
export type CreateClassResult = { readonly kind: 'created'; readonly value: CreatedClass }
  | { readonly kind: 'error'; readonly message: string };
export type DeleteCourseResult = { readonly kind: 'deleted' }
  | { readonly kind: 'error'; readonly status: number; readonly message: string };

@Injectable({
  providedIn: 'root'
})
export class ClassService {
  private readonly http = inject(HttpClient);

  public searchRosters(query: string): Observable<ClassRoster[]> {
    const params = new HttpParams().set('query', query);

    return this.http.get<unknown>(`${backendApiBaseUrl()}/rosters`, { params }).pipe(
      map(response => classRosterSchema.array().parse(response))
    );
  }

  public getAvatar(seed: string): Observable<Blob> {
    return this.http.get(this.avatarUrl(seed), { responseType: 'blob' });
  }

  public avatarUrl(seed: string): string {
    return `${backendApiBaseUrl()}/courses/avatar?seed=${encodeURIComponent(seed)}`;
  }

  public async createClass(name: string, rosterId: string, iconSeed: string): Promise<CreateClassResult> {
    try {
      const response = await firstValueFrom(this.http.post<unknown>(
        `${backendApiBaseUrl()}/courses`,
        { name: name.trim(), rosterIds: [rosterId], iconSeed }
      ));

      return { kind: 'created', value: createdClassSchema.parse(response) };
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        return { kind: 'error', message: this.createErrorMessage(error) };
      }
      if (error instanceof z.ZodError) {
        console.error('POST /api/courses returned an invalid course response.', error);

        return { kind: 'error', message: 'The server returned an invalid response. Please try again.' };
      }
      throw error;
    }
  }

  public async deleteCourse(id: number): Promise<DeleteCourseResult> {
    try {
      await firstValueFrom(this.http.delete(`${backendApiBaseUrl()}/courses/${id}`));

      return { kind: 'deleted' };
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        const status = error.status;
        const body = error.error as { detail?: string; title?: string } | null;
        const message = body?.detail ?? body?.title ?? (status === 403
          ? 'You are not allowed to delete this course.'
          : status === 404
            ? 'This course no longer exists.'
            : 'Could not delete the course. Please try again.');

        return { kind: 'error', status, message };
      }

      throw error;
    }
  }

  private createErrorMessage(error: HttpErrorResponse): string {
    if (error.status === 409) {
      return 'A class with this name already exists for the selected roster.';
    }

    const body = error.error as { errors?: Record<string, string[]>; detail?: string; title?: string } | null;
    const fieldErrors = body?.errors;
    const nameError = fieldErrors?.['name']?.[0] ?? fieldErrors?.['Name']?.[0];
    if (nameError !== undefined) {
      return nameError;
    }
    if (body?.detail) {
      return body.detail;
    }
    if (body?.title) {
      return body.title;
    }

    return 'Could not create class. Please try again.';
  }
}
