import { Component, computed, inject, input, InputSignal, OnDestroy, OnInit, signal, WritableSignal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import {
  AssignmentInfo, AssignmentService, DeadlineKind, FeedbackState, Submission, SubmissionStatus
} from '../../../core/services/assignment-service';
import { NowService } from '../../../core/services/now-service';
import { SnackbarService } from '../../../core/services/snackbar-service';
import { timestampLabel } from '../../../core/util/assignment-state';
import { DeadlineTimeline } from '../../shared/deadline-timeline/deadline-timeline';
import { MarkdownView } from '../../shared/markdown-view/markdown-view';

const POLL_INTERVAL_MS = 2000;
const COPY_RESET_MS = 2000;

type Step = 'accept' | 'clone' | 'push' | 'review';

@Component({
  selector: 'app-student-assignment-detail',
  imports: [RouterLink, MatButton, MatIcon, MatProgressBar, MarkdownView, DeadlineTimeline],
  templateUrl: './assignment-detail.html',
  styleUrl: './assignment-detail.scss'
})
export class StudentAssignmentDetail implements OnInit, OnDestroy {
  private readonly service = inject(AssignmentService);
  private readonly snackbar = inject(SnackbarService);
  private readonly clock = inject(NowService);

  public readonly id: InputSignal<string> = input.required<string>();

  protected readonly DeadlineKind = DeadlineKind;
  protected readonly FeedbackState = FeedbackState;
  protected readonly SubmissionStatus = SubmissionStatus;
  protected readonly steps: readonly { key: Step; label: string }[] = [
    { key: 'accept', label: 'Accept' },
    { key: 'clone', label: 'Clone' },
    { key: 'push', label: 'Push' },
    { key: 'review', label: 'Review' }
  ];

  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly working: WritableSignal<boolean> = signal(false);
  protected readonly failed: WritableSignal<boolean> = signal(false);
  protected readonly info: WritableSignal<AssignmentInfo | null> = signal(null);
  protected readonly submission: WritableSignal<Submission | null> = signal(null);
  protected readonly copiedUrl: WritableSignal<boolean> = signal(false);
  protected readonly copiedBlock: WritableSignal<boolean> = signal(false);
  /** The detail endpoints do not carry the course, but the student's own list does. */
  protected readonly courseTitle: WritableSignal<string> = signal('');

  private pollTimer: ReturnType<typeof setTimeout> | null = null;

  protected readonly cloneUrl = computed(() => this.submission()?.repoUrl ?? '');
  protected readonly repositoryName = computed(() => {
    const url = this.cloneUrl().replace(/\.git$/, '');
    const name = url.split('/').filter(part => part !== '').pop();

    return name ?? '';
  });
  protected readonly commands = computed(() =>
    [
      `git clone ${this.cloneUrl()}`,
      `cd ${this.repositoryName()}`,
      'git add . && git commit -m "Add my work"',
      'git push'
    ].join('\n'));
  protected readonly vsCodeUrl = computed(() =>
    `vscode://vscode.git/clone?url=${encodeURIComponent(this.cloneUrl())}`);

  /** The step the student is on, derived from what actually happened, not from a stored flag. */
  protected readonly currentStep = computed(() => {
    const submission = this.submission();
    if (submission === null || this.info()?.accepted !== true) {
      return 0;
    }
    if (submission.feedbackState !== FeedbackState.None) {
      return 3;
    }
    if (submission.late) {
      return 2;
    }
    const deadline = this.info()?.deadline ?? null;

    return deadline !== null && this.clock.now().isAfter(deadline) ? 2 : 1;
  });

  // The raw instant would read "2026-11-12T20:40:00Z" in the template.
  protected readonly lateSinceLabel = computed(() => {
    const since = this.submission()?.lateSince ?? null;

    return since === null ? '' : timestampLabel(since);
  });

  protected readonly status = computed(() => {
    const submission = this.submission();
    if (this.info()?.accepted !== true || submission === null) {
      return { label: 'Not accepted', tone: 'neutral' };
    }
    if (submission.status === SubmissionStatus.Failed) {
      return { label: 'Setup failed', tone: 'danger' };
    }
    if (submission.status === SubmissionStatus.Provisioning) {
      return { label: 'Setting up', tone: 'info' };
    }
    if (submission.feedbackState !== FeedbackState.None) {
      return { label: 'Reviewed', tone: 'success' };
    }
    if (submission.late) {
      return { label: 'Submitted late', tone: 'late' };
    }
    return { label: 'Ready to start', tone: 'success' };
  });

  /** The submission payload carries no push timestamp, so the row stays an em dash. */
  protected readonly lastPushRelative = computed(() => '—');

  protected readonly feedback = computed(() => {
    switch (this.submission()?.feedbackState ?? FeedbackState.None) {
      case FeedbackState.Unread:
        return 'Feedback waiting';
      case FeedbackState.Read:
        return 'Feedback read';
      default:
        return 'Not available';
    }
  });

  public async ngOnInit(): Promise<void> {
    this.loading.set(true);
    const info = await this.service.getInfo(Number(this.id()));
    this.loading.set(false);
    if (info === 'forbidden' || info === null) {
      this.snackbar.show('Assignment not available');

      return;
    }
    this.info.set(info);
    await this.loadCourseTitle();
    if (info.accepted) {
      await this.refreshSubmission();
    }
  }

  public ngOnDestroy(): void {
    this.clearPoll();
  }

  protected isDone(index: number): boolean {
    return index < this.currentStep();
  }

  protected isCurrent(index: number): boolean {
    return index === this.currentStep();
  }

  protected async handleAccept(): Promise<void> {
    this.working.set(true);
    this.failed.set(false);
    const result = await this.service.accept(Number(this.id()));
    this.working.set(false);
    if (result === 'conflict') {
      await this.refreshSubmission();

      return;
    }
    if (result === 'forbidden' || result === 'not-found' || result === null) {
      this.failed.set(true);

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

  protected async copyUrl(): Promise<void> {
    this.copiedUrl.set(await this.copyToClipboard(this.cloneUrl()));
    if (this.copiedUrl()) {
      setTimeout(() => this.copiedUrl.set(false), COPY_RESET_MS);
    }
  }

  protected async copyCommands(): Promise<void> {
    this.copiedBlock.set(await this.copyToClipboard(this.commands()));
    if (this.copiedBlock()) {
      setTimeout(() => this.copiedBlock.set(false), COPY_RESET_MS);
    }
  }

  private async loadCourseTitle(): Promise<void> {
    const list = await this.service.listMine();
    if (list === null) {
      return;
    }
    const match = [...list.accepted, ...list.unaccepted].find(item => item.id === Number(this.id()));
    if (match !== undefined) {
      this.courseTitle.set(match.courseTitle);
    }
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

  private async copyToClipboard(text: string): Promise<boolean> {
    try {
      await navigator.clipboard.writeText(text);

      return true;
    } catch {
      this.snackbar.show('Could not copy — select the text and copy it manually');

      return false;
    }
  }
}
