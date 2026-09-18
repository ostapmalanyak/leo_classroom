import { Component, DestroyRef, inject, input, InputSignal, signal, WritableSignal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatCardModule } from '@angular/material/card';
import { DownloadJob, DownloadJobStatus, DownloadService } from '../../../core/services/download-service';
import { DownloadSnapshotMode } from '../../../core/services/assignment-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

@Component({
  selector: 'app-assignment-download',
  imports: [MatButtonModule, MatButtonToggleModule, MatProgressBar, MatCardModule],
  templateUrl: './assignment-download.html',
  styleUrl: './assignment-download.scss'
})
export class AssignmentDownload {
  protected readonly DownloadJobStatus = DownloadJobStatus;
  protected readonly DownloadSnapshotMode = DownloadSnapshotMode;

  private readonly service = inject(DownloadService);
  private readonly snackbar = inject(SnackbarService);
  private readonly destroyRef = inject(DestroyRef);

  public readonly id: InputSignal<string> = input.required<string>();

  protected readonly mode: WritableSignal<DownloadSnapshotMode | null> = signal(null);
  protected readonly job: WritableSignal<DownloadJob | null> = signal(null);
  protected readonly busy: WritableSignal<boolean> = signal(false);

  private pollTimer: ReturnType<typeof setTimeout> | null = null;

  public constructor() {
    this.destroyRef.onDestroy(() => this.clearTimer());
  }

  protected async start(): Promise<void> {
    this.busy.set(true);
    this.job.set(null);
    const result = await this.service.trigger(Number(this.id()), this.mode());
    this.busy.set(false);
    if (result === 'forbidden' || result === 'not-found' || result === null) {
      this.snackbar.show('Could not start the download');

      return;
    }
    this.poll(result);
  }

  protected async download(): Promise<void> {
    const current = this.job();
    if (current === null) {
      return;
    }
    const result = await this.service.downloadArtifact(Number(this.id()), current.id);
    if (result !== 'ok') {
      this.snackbar.show('Could not download the archive');
    }
  }

  private poll(jobId: number): void {
    this.clearTimer();
    void this.refresh(jobId);
  }

  private async refresh(jobId: number): Promise<void> {
    const result = await this.service.getStatus(Number(this.id()), jobId);
    if (result === 'forbidden' || result === 'not-found' || result === null) {
      this.snackbar.show('Could not read the download status');

      return;
    }
    this.job.set(result);
    if (result.status === DownloadJobStatus.Pending || result.status === DownloadJobStatus.Running) {
      this.pollTimer = setTimeout(() => void this.refresh(jobId), 2000);
    }
  }

  private clearTimer(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }
}
