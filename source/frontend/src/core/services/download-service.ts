import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { z } from 'zod';
import { BackendServiceBase } from './backend-service-base';
import { DownloadSnapshotMode } from './assignment-service';

export type DownloadResult<T> = T | 'forbidden' | 'not-found' | null;

export enum DownloadJobStatus {
  Pending = 'Pending',
  Running = 'Running',
  Ready = 'Ready',
  Failed = 'Failed'
}

@Injectable({
  providedIn: 'root'
})
export class DownloadService extends BackendServiceBase {
  protected override get controller(): string {
    return 'assignments';
  }

  public async trigger(
    assignmentId: number, mode: DownloadSnapshotMode | null): Promise<DownloadResult<number>> {
    try {
      const response = await firstValueFrom(
        this.http.post<unknown>(this.buildUrl(`${assignmentId}/download`), { mode }));

      return acceptedZod.parse(response).id;
    } catch (error) {
      return this.mapError(error);
    }
  }

  public async getStatus(assignmentId: number, jobId: number): Promise<DownloadResult<DownloadJob>> {
    try {
      const response = await firstValueFrom(
        this.http.get<unknown>(this.buildUrl(`${assignmentId}/download/${jobId}`)));

      return downloadJobZod.parse(response);
    } catch (error) {
      return this.mapError(error);
    }
  }

  public async downloadArtifact(assignmentId: number, jobId: number): Promise<DownloadResult<'ok'>> {
    try {
      const blob = await firstValueFrom(
        this.http.get(this.buildUrl(`${assignmentId}/download/${jobId}/artifact`), { responseType: 'blob' }));
      this.saveBlob(blob, `assignment-${assignmentId}-submissions.zip`);

      return 'ok';
    } catch (error) {
      return this.mapError(error);
    }
  }

  private saveBlob(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    URL.revokeObjectURL(url);
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

const acceptedZod = z.object({ id: z.int() });

const downloadJobZod = z.object({
  id: z.int(),
  assignmentId: z.int(),
  status: z.enum(DownloadJobStatus),
  mode: z.enum(DownloadSnapshotMode),
  notes: z.string().nullable(),
  error: z.string().nullable(),
  createdAt: z.string(),
  completedAt: z.string().nullable(),
  expiresAt: z.string().nullable()
});
export type DownloadJob = z.infer<typeof downloadJobZod>;
