import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { z } from 'zod';
import { BackendServiceBase } from './backend-service-base';
import { userSummaryZod } from './user-service';

export type TeacherWriteResult = 'ok' | 'forbidden' | 'not-found' | null;

@Injectable({
  providedIn: 'root'
})
export class CourseTeacherService extends BackendServiceBase {
  protected override get controller(): string {
    return 'courses';
  }

  public async getTeachers(courseId: number): Promise<CourseTeachers | null> {
    try {
      const response = await firstValueFrom(this.http.get<unknown>(this.teachersUrl(courseId)));

      return courseTeachersZod.parse(response);
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        return null;
      }
      throw error;
    }
  }

  public async addTeacher(courseId: number, userId: number): Promise<TeacherWriteResult> {
    try {
      await firstValueFrom(this.http.post(this.teachersUrl(courseId), { userId }));

      return 'ok';
    } catch (error) {
      return this.mapWriteError(error);
    }
  }

  public async removeTeacher(courseId: number, userId: number): Promise<TeacherWriteResult> {
    try {
      await firstValueFrom(this.http.delete(`${this.teachersUrl(courseId)}/${userId}`));

      return 'ok';
    } catch (error) {
      return this.mapWriteError(error);
    }
  }

  private teachersUrl(courseId: number): string {
    return this.buildUrl(`${courseId}/teachers`);
  }

  private mapWriteError(error: unknown): TeacherWriteResult {
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

const courseTeachersZod = z.object({
  owner: userSummaryZod,
  coTeachers: userSummaryZod.array()
});
export type CourseTeachers = z.infer<typeof courseTeachersZod>;
