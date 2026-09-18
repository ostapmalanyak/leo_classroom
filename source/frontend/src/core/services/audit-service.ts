import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { z } from 'zod';
import { BackendServiceBase, QueryParam } from './backend-service-base';
import { InstantSchema } from '../util/zod-schemas';

export interface AuditFilter {
  from: string | null;
  to: string | null;
  actor: string | null;
  action: AuditAction | null;
}

@Injectable({
  providedIn: 'root'
})
export class AuditService extends BackendServiceBase {
  protected override get controller(): string {
    return 'audit';
  }

  public async query(filter: AuditFilter): Promise<AuditEvent[] | null> {
    try {
      const params = this.createHttpParams(
        ['from', filter.from] as QueryParam,
        ['to', filter.to] as QueryParam,
        ['actor', filter.actor] as QueryParam,
        ['action', filter.action] as QueryParam);
      const response = await firstValueFrom(this.http.get<unknown>(this.buildUrl(''), { params }));

      return auditEventZod.array().parse(response);
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        return null;
      }
      throw error;
    }
  }
}

export enum AuditAction {
  AssignmentCreated = 'AssignmentCreated',
  AssignmentDeleted = 'AssignmentDeleted',
  AssignmentAutoDeleted = 'AssignmentAutoDeleted',
  RepositoryDeleted = 'RepositoryDeleted',
  AcceptanceDeleted = 'AcceptanceDeleted',
  CourseTeacherAdded = 'CourseTeacherAdded',
  CourseTeacherRemoved = 'CourseTeacherRemoved',
  AssignmentTeacherAdded = 'AssignmentTeacherAdded',
  AssignmentTeacherRemoved = 'AssignmentTeacherRemoved',
  CourseReadOnlyToggled = 'CourseReadOnlyToggled',
  UserSoftDeleted = 'UserSoftDeleted',
  UserReactivated = 'UserReactivated',
  RejectedSoftDeletedLogin = 'RejectedSoftDeletedLogin',
  RetentionPurge = 'RetentionPurge',
  UserHardDeleted = 'UserHardDeleted',
  CourseDeleted = 'CourseDeleted'
}

const auditEventZod = z.object({
  id: z.int().positive(),
  at: InstantSchema,
  actorStudentId: z.string(),
  actorRoles: z.string(),
  action: z.enum(AuditAction),
  targetType: z.string(),
  targetId: z.string().nullable(),
  metadata: z.string()
});
export type AuditEvent = z.infer<typeof auditEventZod>;
