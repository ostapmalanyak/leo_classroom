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
  rosterId: z.number().int().positive()
});

export type ClassRoster = z.infer<typeof classRosterSchema>;
export type CreatedClass = z.infer<typeof createdClassSchema>;
export type CreateClassResult = { readonly kind: 'created'; readonly value: CreatedClass }
  | { readonly kind: 'error'; readonly message: string };

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

  public getAvatar(name: string): Observable<Blob> {
    const params = new HttpParams().set('name', name);

    return this.http.get(`${backendApiBaseUrl()}/courses/avatar`, { params, responseType: 'blob' });
  }

  public async createClass(name: string, rosterId: string): Promise<CreateClassResult> {
    try {
      const response = await firstValueFrom(this.http.post<unknown>(
        `${backendApiBaseUrl()}/courses`,
        { name: name.trim(), rosterIds: [rosterId] }
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
