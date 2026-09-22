import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom, Observable } from 'rxjs';
import { z } from 'zod';
import { BackendServiceBase } from './backend-service-base';
import { InstantSchema } from '../util/zod-schemas';

export type WriteResult<T> = T | 'forbidden' | 'not-found' | 'conflict' | null;
export type ActionResult = 'ok' | 'forbidden' | 'not-found' | 'conflict' | null;

@Injectable({
  providedIn: 'root'
})
export class AssignmentService extends BackendServiceBase {
  protected override get controller(): string {
    return 'assignments';
  }

  public async create(request: AssignmentWrite & { courseId: number }): Promise<WriteResult<Assignment>> {
    return this.write(this.http.post<unknown>(this.buildUrl(''), request));
  }

  public async listForCourse(courseId: number): Promise<Assignment[] | null> {
    try {
      const response = await firstValueFrom(this.http.get<unknown>(this.buildUrl(`course/${courseId}`)));

      return assignmentZod.array().parse(response);
    } catch (error) {
      if (error instanceof HttpErrorResponse || error instanceof z.ZodError) {
        return null;
      }
      throw error;
    }
  }

  public async getForEdit(id: number): Promise<Assignment | 'forbidden' | null> {
    try {
      const response = await firstValueFrom(this.http.get<unknown>(this.buildUrl(`${id}/edit`)));

      return assignmentZod.parse(response);
    } catch (error) {
      return this.mapReadError(error);
    }
  }

  public async update(id: number, request: AssignmentWrite): Promise<WriteResult<Assignment>> {
    return this.write(this.http.put<unknown>(this.buildUrl(`${id}`), request));
  }

  public async remove(id: number): Promise<ActionResult> {
    return this.action(this.http.delete(this.buildUrl(`${id}`)));
  }

  public async addTeacher(id: number, userId: number): Promise<ActionResult> {
    return this.action(this.http.post(this.buildUrl(`${id}/teachers`), { userId }));
  }

  public async removeTeacher(id: number, userId: number): Promise<ActionResult> {
    return this.action(this.http.delete(this.buildUrl(`${id}/teachers/${userId}`)));
  }

  public async getStudents(id: number): Promise<StudentSubmission[] | null> {
    try {
      const response = await firstValueFrom(this.http.get<unknown>(this.buildUrl(`${id}/students`)));

      return studentSubmissionZod.array().parse(response);
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        return null;
      }
      throw error;
    }
  }

  public async listMine(): Promise<StudentAssignmentList | null> {
    try {
      const response = await firstValueFrom(this.http.get<unknown>(this.buildUrl('mine')));

      return studentListZod.parse(response);
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        return null;
      }
      throw error;
    }
  }

  public async getInfo(id: number): Promise<AssignmentInfo | 'forbidden' | null> {
    try {
      const response = await firstValueFrom(this.http.get<unknown>(this.buildUrl(`${id}/info`)));

      return assignmentInfoZod.parse(response);
    } catch (error) {
      return this.mapReadError(error);
    }
  }

  public async getSubmission(id: number): Promise<Submission | 'forbidden' | null> {
    try {
      const response = await firstValueFrom(this.http.get<unknown>(this.buildUrl(`${id}/submission`)));

      return submissionZod.parse(response);
    } catch (error) {
      return this.mapReadError(error);
    }
  }

  public async getAnalytics(acceptanceId: number): Promise<CommitAnalytics | 'forbidden' | null> {
    try {
      const response = await firstValueFrom(
        this.http.get<unknown>(this.buildUrl(`acceptances/${acceptanceId}/analytics`)));

      return commitAnalyticsZod.parse(response);
    } catch (error) {
      return this.mapReadError(error);
    }
  }

  public async openFeedbackPr(acceptanceId: number): Promise<FeedbackPr | 'forbidden' | 'not-found' | null> {
    try {
      const response = await firstValueFrom(
        this.http.post<unknown>(this.buildUrl(`acceptances/${acceptanceId}/feedback-pr`), null));

      return feedbackPrZod.parse(response);
    } catch (error) {
      const mapped = this.mapWriteError(error);

      return mapped === 'conflict' ? null : mapped;
    }
  }

  public async confirmFeedbackRead(assignmentId: number): Promise<ActionResult> {
    return this.action(this.http.post(this.buildUrl(`${assignmentId}/feedback/read`), null));
  }

  public async accept(id: number): Promise<WriteResult<Submission>> {
    try {
      const response = await firstValueFrom(this.http.post<unknown>(this.buildUrl(`${id}/accept`), null));

      return submissionZod.parse(response);
    } catch (error) {
      return this.mapWriteError(error);
    }
  }

  public async retry(id: number): Promise<WriteResult<Submission>> {
    try {
      const response = await firstValueFrom(this.http.post<unknown>(this.buildUrl(`${id}/retry`), null));

      return submissionZod.parse(response);
    } catch (error) {
      return this.mapWriteError(error);
    }
  }

  private async write(call: Observable<unknown>): Promise<WriteResult<Assignment>> {
    try {
      return assignmentZod.parse(await firstValueFrom(call));
    } catch (error) {
      return this.mapWriteError(error);
    }
  }

  private async action(call: Observable<unknown>): Promise<ActionResult> {
    try {
      await firstValueFrom(call);

      return 'ok';
    } catch (error) {
      return this.mapWriteError(error);
    }
  }

  private mapReadError(error: unknown): 'forbidden' | null {
    if (error instanceof HttpErrorResponse) {
      return error.status === 403 ? 'forbidden' : null;
    }
    throw error;
  }

  private mapWriteError(error: unknown): 'forbidden' | 'not-found' | 'conflict' | null {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 403) {
        return 'forbidden';
      }
      if (error.status === 404) {
        return 'not-found';
      }
      if (error.status === 409) {
        return 'conflict';
      }

      return null;
    }
    throw error;
  }
}

export enum DeadlineKind {
  None = 'None',
  Soft = 'Soft',
  Hard = 'Hard'
}

export enum StarterSourceKind {
  DescriptionOnly = 'DescriptionOnly',
  Archive = 'Archive',
  ForkOwnRepo = 'ForkOwnRepo',
  CopyRepo = 'CopyRepo'
}

export enum DownloadSnapshotMode {
  Deadline = 'Deadline',
  Head = 'Head'
}

export enum SubmissionStatus {
  Provisioning = 'Provisioning',
  Ready = 'Ready',
  Failed = 'Failed'
}

export enum FeedbackState {
  None = 'None',
  Unread = 'Unread',
  Read = 'Read'
}

export interface AssignmentWrite {
  title: string;
  description: string | null;
  hintsInstructions: string | null;
  deadline: string | null;
  deadlineKind: DeadlineKind;
  hardDeadlineRevokesRead: boolean;
  starterSourceKind: StarterSourceKind;
  starterRepoUrl: string | null;
  readmeMarkdown: string | null;
  autoDeleteEnabled: boolean;
  autoDeleteOn: string | null;
  downloadSnapshotMode: DownloadSnapshotMode;
}

const assignmentZod = z.object({
  id: z.int().positive(),
  courseId: z.int().positive(),
  ownerId: z.int().positive(),
  title: z.string().min(1),
  slug: z.string(),
  description: z.string().nullable(),
  hintsInstructions: z.string().nullable(),
  deadline: InstantSchema.nullable(),
  deadlineKind: z.enum(DeadlineKind),
  hardDeadlineRevokesRead: z.boolean(),
  starterSourceKind: z.enum(StarterSourceKind),
  starterRepoUrl: z.string().nullable(),
  readmeMarkdown: z.string().nullable(),
  autoDeleteEnabled: z.boolean(),
  autoDeleteOn: InstantSchema.nullable(),
  downloadSnapshotMode: z.enum(DownloadSnapshotMode)
});
export type Assignment = z.infer<typeof assignmentZod>;

const studentSubmissionZod = z.object({
  acceptanceId: z.int().positive(),
  studentId: z.int().positive(),
  studentNumber: z.string(),
  firstName: z.string(),
  lastName: z.string(),
  status: z.enum(SubmissionStatus),
  lastCommitAt: InstantSchema.nullable(),
  lastPushAt: InstantSchema.nullable(),
  repoUrl: z.string().nullable(),
  late: z.boolean(),
  lateSince: InstantSchema.nullable(),
  feedbackState: z.enum(FeedbackState),
  feedbackReadAt: InstantSchema.nullable(),
  feedbackPrNumber: z.int().nullable()
});
export type StudentSubmission = z.infer<typeof studentSubmissionZod>;

const commitAnalyticsZod = z.object({
  pushCount: z.int(),
  commitCount: z.int(),
  commitsPerPush: z.number(),
  firstPushAt: InstantSchema.nullable(),
  lastPushAt: InstantSchema.nullable(),
  activeDayCount: z.int()
});
export type CommitAnalytics = z.infer<typeof commitAnalyticsZod>;

const feedbackPrZod = z.object({
  number: z.int(),
  htmlUrl: z.string()
});
export type FeedbackPr = z.infer<typeof feedbackPrZod>;

const assignedAssignmentZod = z.object({
  id: z.int().positive(),
  courseId: z.int().positive(),
  courseTitle: z.string(),
  title: z.string(),
  deadline: InstantSchema.nullable(),
  deadlineKind: z.enum(DeadlineKind),
  accepted: z.boolean(),
  status: z.enum(SubmissionStatus).nullable()
});
export type AssignedAssignment = z.infer<typeof assignedAssignmentZod>;

const studentListZod = z.object({
  accepted: assignedAssignmentZod.array(),
  unaccepted: assignedAssignmentZod.array()
});
export type StudentAssignmentList = z.infer<typeof studentListZod>;

const assignmentInfoZod = z.object({
  id: z.int().positive(),
  title: z.string(),
  hintsInstructions: z.string().nullable(),
  deadline: InstantSchema.nullable(),
  deadlineKind: z.enum(DeadlineKind),
  accepted: z.boolean()
});
export type AssignmentInfo = z.infer<typeof assignmentInfoZod>;

const submissionZod = z.object({
  assignmentId: z.int().positive(),
  title: z.string(),
  description: z.string().nullable(),
  deadline: InstantSchema.nullable(),
  deadlineKind: z.enum(DeadlineKind),
  status: z.enum(SubmissionStatus).nullable(),
  repoUrl: z.string().nullable(),
  late: z.boolean(),
  lateSince: InstantSchema.nullable(),
  feedbackState: z.enum(FeedbackState),
  feedbackReadAt: InstantSchema.nullable()
});
export type Submission = z.infer<typeof submissionZod>;
