import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { z } from 'zod';
import { BackendServiceBase } from './backend-service-base';

@Injectable({
  providedIn: 'root'
})
export class UserService extends BackendServiceBase {
  protected override get controller(): string {
    return 'users';
  }

  public async search(term: string, role: UserRole | null = null): Promise<UserSummary[] | null> {
    try {
      const params = this.createHttpParams(['q', term], ['role', role]);
      const response = await firstValueFrom(this.http.get<unknown>(this.buildUrl('search'), { params }));

      return userSummaryZod.array().parse(response);
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        return null;
      }
      throw error;
    }
  }
}

export enum UserRole {
  Student = 'Student',
  Teacher = 'Teacher',
  Admin = 'Admin'
}

export const userSummaryZod = z.object({
  id: z.int().positive(),
  studentId: z.string().min(1),
  firstName: z.string(),
  lastName: z.string(),
  class: z.string().nullable(),
  role: z.enum(UserRole)
});
export type UserSummary = z.infer<typeof userSummaryZod>;
