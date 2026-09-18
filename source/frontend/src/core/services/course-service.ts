import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { z } from 'zod';
import { BackendServiceBase } from './backend-service-base';
import { InstantSchema } from '../util/zod-schemas';

export type WriteResult<T> = T | 'conflict' | 'not-found' | null;

@Injectable({
  providedIn: 'root'
})
export class CourseService extends BackendServiceBase {
  protected override get controller(): string {
    return 'courses';
  }

  public async getCourses(): Promise<CourseOverview[] | null> {
    try {
      const response = await firstValueFrom(this.http.get<unknown>(this.buildUrl('')));

      return courseOverviewZod.array().parse(response);
    } catch (error) {
      return this.mapReadError(error);
    }
  }

  public async getCourseById(id: number): Promise<Course | null> {
    try {
      const response = await firstValueFrom(this.http.get<unknown>(this.buildUrl(`${id}`)));

      return courseZod.parse(response);
    } catch (error) {
      return this.mapReadError(error);
    }
  }

  public async createCourse(title: string, rosterId: number): Promise<WriteResult<Course>> {
    try {
      const response = await firstValueFrom(
        this.http.post<unknown>(this.buildUrl(''), { title, rosterId }));

      return courseZod.parse(response);
    } catch (error) {
      return this.mapWriteError(error);
    }
  }

  public async updateCourse(id: number, title: string, isReadOnly: boolean,
                            studentsRetainAccess: boolean): Promise<WriteResult<Course>> {
    try {
      const response = await firstValueFrom(
        this.http.put<unknown>(this.buildUrl(`${id}`), { title, isReadOnly, studentsRetainAccess }));

      return courseZod.parse(response);
    } catch (error) {
      return this.mapWriteError(error);
    }
  }

  public async deleteCourse(id: number): Promise<boolean> {
    try {
      await firstValueFrom(this.http.delete(this.buildUrl(`${id}`)));

      return true;
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        return false;
      }
      throw error;
    }
  }

  private mapReadError(error: unknown): null {
    if (error instanceof HttpErrorResponse) {
      return null;
    }
    throw error;
  }

  private mapWriteError(error: unknown): 'conflict' | 'not-found' | null {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 409) {
        return 'conflict';
      }
      if (error.status === 404) {
        return 'not-found';
      }

      return null;
    }
    throw error;
  }
}

const courseOverviewZod = z.object({
  id: z.int().positive(),
  title: z.string().min(1),
  rosterId: z.int().positive(),
  rosterName: z.string(),
  memberCount: z.int().nonnegative(),
  isReadOnly: z.boolean(),
  studentsRetainAccess: z.boolean()
});
export type CourseOverview = z.infer<typeof courseOverviewZod>;

const courseZod = z.object({
  id: z.int().positive(),
  title: z.string().min(1),
  rosterId: z.int().positive(),
  ownerId: z.int().positive(),
  isReadOnly: z.boolean(),
  studentsRetainAccess: z.boolean(),
  createdAt: InstantSchema
});
export type Course = z.infer<typeof courseZod>;
