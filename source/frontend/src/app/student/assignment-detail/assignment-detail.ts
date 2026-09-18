import { Component, inject, input, InputSignal, OnDestroy, OnInit, signal, WritableSignal } from '@angular/core';
import { MatCard, MatCardContent, MatCardTitle } from '@angular/material/card';
import { MatButton } from '@angular/material/button';
import { MatProgressBar } from '@angular/material/progress-bar';
import {
  AssignmentInfo, AssignmentService, FeedbackState, Submission, SubmissionStatus
} from '../../../core/services/assignment-service';
import { SnackbarService } from '../../../core/services/snackbar-service';
import { MarkdownView } from '../../shared/markdown-view/markdown-view';

const POLL_INTERVAL_MS = 2000;

@Component({
  selector: 'app-student-assignment-detail',
  imports: [MatCard, MatCardContent, MatCardTitle, MatButton, MatProgressBar, MarkdownView],
  templateUrl: './assignment-detail.html',
  styleUrl: './assignment-detail.scss'
})
export class StudentAssignmentDetail implements OnInit, OnDestroy {
  private readonly service = inject(AssignmentService);
  private readonly snackbar = inject(SnackbarService);

  public readonly id: InputSignal<string> = input.required<string>();

  protected readonly SubmissionStatus = SubmissionStatus;
  protected readonly FeedbackState = FeedbackState;
  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly working: WritableSignal<boolean> = signal(false);
  protected readonly info: WritableSignal<AssignmentInfo | null> = signal(null);
  protected readonly submission: WritableSignal<Submission | null> = signal(null);

  private pollTimer: ReturnType<typeof setTimeout> | null = null;

  public async ngOnInit(): Promise<void> {
    this.loading.set(true);
    const info = await this.service.getInfo(Number(this.id()));
    this.loading.set(false);
    if (info === 'forbidden' || info === null) {
      this.snackbar.show('Assignment not available');

      return;
    }
    this.info.set(info);
    if (info.accepted) {
      await this.refreshSubmission();
    }
  }

  public ngOnDestroy(): void {
    this.clearPoll();
  }

  protected async handleAccept(): Promise<void> {
    this.working.set(true);
    const result = await this.service.accept(Number(this.id()));
    this.working.set(false);
    if (result === 'conflict') {
      await this.refreshSubmission();

      return;
    }
    if (result === 'forbidden' || result === 'not-found' || result === null) {
      this.snackbar.show('Could not accept the assignment');

      return;
    }
    this.submission.set(result);
    this.schedulePollIfProvisioning(result);
  }

  protected async handleRetry(): Promise<void> {
    this.working.set(true);
    const result = await this.service.retry(Number(this.id()));
    this.working.set(false);
    if (typeof result === 'string' || result === null) {
      this.snackbar.show('Could not retry provisioning');

      return;
    }
    this.submission.set(result);
    this.schedulePollIfProvisioning(result);
  }

  protected async handleConfirmRead(): Promise<void> {
    this.working.set(true);
    const result = await this.service.confirmFeedbackRead(Number(this.id()));
    this.working.set(false);
    if (result !== 'ok') {
      this.snackbar.show('Could not confirm the feedback');

      return;
    }
    await this.refreshSubmission();
  }

  private async refreshSubmission(): Promise<void> {
    const result = await this.service.getSubmission(Number(this.id()));
    if (result === 'forbidden' || result === null) {
      return;
    }
    this.submission.set(result);
    this.schedulePollIfProvisioning(result);
  }

  private schedulePollIfProvisioning(submission: Submission): void {
    this.clearPoll();
    if (submission.status === SubmissionStatus.Provisioning) {
      this.pollTimer = setTimeout(() => void this.refreshSubmission(), POLL_INTERVAL_MS);
    }
  }

  private clearPoll(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }
}
