import {
  Component, computed, inject, input, InputSignal, OnDestroy, OnInit, signal, viewChild, WritableSignal
} from '@angular/core';
import { Instant } from '@js-joda/core';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import {
  AssignmentInfo, AssignmentService, DeadlineKind, FeedbackState, Submission, SubmissionStatus
} from '../../../core/services/assignment-service';
import { NowService } from '../../../core/services/now-service';
import { SnackbarService } from '../../../core/services/snackbar-service';
import { relativeDue } from '../../../core/util/assignment-state';
import { DeadlineTimeline } from '../../shared/deadline-timeline/deadline-timeline';
import { GitLoginStep } from '../../shared/git-login-step/git-login-step';
import { MarkdownView } from '../../shared/markdown-view/markdown-view';

/** What the student has to do next; only one of them is ever on screen. */
type Step = 'archive' | 'accept' | 'clone' | 'push' | 'feedback';

/**
 * The student's page for one assignment. It answers a single question — what do I do next — and
 * everything else (instructions, reminder of the deadline) sits behind that answer.
 */
@Component({
  selector: 'app-assignment-detail',
  imports: [MatButton, MatIcon, MatProgressBar, DeadlineTimeline, GitLoginStep, MarkdownView],
  templateUrl: './assignment-detail.html',
  styleUrl: './assignment-detail.scss'
})
export class StudentAssignmentDetail implements OnInit, OnDestroy {
  private readonly service = inject(AssignmentService);
  private readonly snackbar = inject(SnackbarService);
  private readonly clock = inject(NowService);

  public readonly id: InputSignal<string> = input.required<string>();

  protected readonly loading: WritableSignal<boolean> = signal(true);
  protected readonly working: WritableSignal<boolean> = signal(false);
  protected readonly failed: WritableSignal<boolean> = signal(false);
  protected readonly archiveDownloadFailed: WritableSignal<boolean> = signal(false);
  protected readonly info: WritableSignal<AssignmentInfo | null> = signal(null);
  protected readonly submission: WritableSignal<Submission | null> = signal(null);
  protected readonly courseTitle: WritableSignal<string> = signal('');
  protected readonly description: WritableSignal<string> = signal('');
  protected readonly lastPushAt: WritableSignal<number | null> = signal(null);
  protected readonly copied: WritableSignal<boolean> = signal(false);
  protected readonly instructionsOpen: WritableSignal<boolean> = signal(false);
  protected readonly provisioning: WritableSignal<boolean> = signal(false);

  private poll: ReturnType<typeof setInterval> | null = null;
  private static readonly POLL_MS = 2000;

  protected readonly DeadlineKind = DeadlineKind;
  protected readonly SubmissionStatus = SubmissionStatus;

  protected readonly title = computed(() => this.info()?.title ?? '');
  protected readonly deadline = computed(() => this.info()?.deadline ?? null);
  protected readonly deadlineKind = computed(() => this.info()?.deadlineKind ?? DeadlineKind.None);
  protected readonly accepted = computed(() => this.info()?.accepted === true);
  protected readonly hasStarterArchive = computed(() => this.info()?.hasStarterArchive === true);
  protected readonly downloadOnly = computed(() => this.info()?.downloadOnly === true);

  /** A hard deadline that has passed closes the repository: nothing can be pushed any more. */
  protected readonly locked = computed(() => {
    const deadline = this.deadline();

    return this.deadlineKind() === DeadlineKind.Hard
      && deadline !== null
      && this.clock.now().isAfter(deadline);
  });

  protected readonly hasPushed = computed(() => this.lastPushAt() !== null);

  /** The login step only matters once there is a repository to clone from. */
  protected readonly needsGitLogin = computed(() => this.step() !== 'accept' && this.step() !== 'archive');

  private readonly gitStep = viewChild(GitLoginStep);

  /** Unlocked once git remembers the password, or once the student chose to type it by hand. */
  protected readonly gitReady = computed(() => this.gitStep()?.ready() ?? true);

  private readonly gitAuthorized = computed(() => this.gitStep()?.authorized() ?? false);

  /** Lock while the login is still missing, an open lock while it is only unconfirmed, nothing afterwards. */
  protected readonly gitIcon = computed<string | null>(() => {
    if (!this.gitReady()) {
      return 'lock';
    }

    return this.gitAuthorized() ? null : 'lock_open';
  });

  protected readonly step = computed<Step>(() => {
    if (this.downloadOnly()) {
      return this.accepted() ? 'archive' : 'accept';
    }
    const submission = this.submission();
    if (!this.accepted() || submission === null) {
      return 'accept';
    }

    // Until the backend has finished the repository there is nothing to clone yet, so the page keeps
    // the set-up state (and its progress bar) on screen instead of an empty git command.
    if (submission.status !== SubmissionStatus.Ready) {
      return 'accept';
    }
    if (submission.feedbackState !== FeedbackState.None) {
      return 'feedback';
    }

    return this.hasPushed() ? 'push' : 'clone';
  });

  protected readonly pushLabel = computed(() => {
    const pushed = this.lastPushAt();

    return pushed === null ? '' : `Last push ${relativeDue(Instant.ofEpochMilli(pushed), this.clock.now())}`;
  });

  protected readonly cloneUrl = computed(() => this.submission()?.repoUrl ?? '');

  /** The block shows and copies the same thing: the whole URL, never a shortened one. */
  protected readonly cloneCommands = computed(() => `git clone ${this.cloneUrl()}`);

  /** A push that landed after a soft deadline still counts, and the row says so. */
  protected readonly pushedLate = computed(() => {
    const deadline = this.deadline();
    const pushed = this.lastPushAt();

    return this.deadlineKind() !== DeadlineKind.Hard
      && deadline !== null
      && pushed !== null
      && Instant.ofEpochMilli(pushed).isAfter(deadline);
  });
  protected readonly pushCommands = 'git add . && git commit -m "Describe your change"\ngit push';

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

    // Always look at the submission: the acceptance may have been started somewhere else, and the
    // page has to show the set-up bar instead of the clone step until the repository exists.
    await this.refreshSubmission();
  }

  protected async handleAccept(): Promise<void> {
    this.working.set(true);
    this.failed.set(false);
    const result = await this.service.accept(Number(this.id()));
    this.working.set(false);

    if (result === 'forbidden' || result === 'not-found' || result === 'conflict' || result === null) {
      this.failed.set(true);

      return;
    }

    this.submission.set(result);
    this.instructionsOpen.set(false);
    this.applyStatus(result);
  }

  /** The backend provisions in the background, so the page asks until it is done. */
  private startPolling(): void {
    this.stopPolling();
    this.poll = setInterval(() => void this.checkProvisioning(), StudentAssignmentDetail.POLL_MS);
  }

  private stopPolling(): void {
    if (this.poll !== null) {
      clearInterval(this.poll);
      this.poll = null;
    }
  }

  private async checkProvisioning(): Promise<void> {
    const submission = await this.service.getSubmission(Number(this.id()));
    if (submission === 'forbidden' || submission === null) {
      return;
    }

    this.submission.set(submission);
    this.applyStatus(submission);
  }

  /**
   * Mirrors the backend status onto the screen. A page opened (or reloaded) while the repository is
   * still being created has to pick the provisioning state up by itself, otherwise it would show a
   * clone command for a repository that does not exist yet.
   */
  private applyStatus(submission: Submission): void {
    if (submission.status === SubmissionStatus.Provisioning) {
      this.provisioning.set(true);
      if (this.poll === null) {
        this.startPolling();
      }

      return;
    }

    this.stopPolling();
    this.provisioning.set(false);
    this.failed.set(submission.status === SubmissionStatus.Failed);

    if (submission.status === SubmissionStatus.Ready) {
      this.info.update(current => (current === null ? current : { ...current, accepted: true }));
    }
  }

  public ngOnDestroy(): void {
    this.stopPolling();
  }

  protected async handleStarterArchiveDownload(): Promise<void> {
    this.working.set(true);
    this.archiveDownloadFailed.set(false);
    const result = await this.service.downloadStarterArchive(Number(this.id()));
    this.working.set(false);

    if (result !== 'ok') {
      this.archiveDownloadFailed.set(true);
      this.snackbar.show('Could not download the starter archive');
    }
  }

  protected openRepository(): void {
    if (this.cloneUrl() !== '') {
      window.open(this.cloneUrl(), '_blank', 'noopener');
    }
  }

  /** The feedback lives on the pull request in the repository, so that is where this leads. */
  protected async viewFeedback(): Promise<void> {
    this.openRepository();
    await this.service.confirmFeedbackRead(Number(this.id()));
    await this.refreshSubmission();
  }

  protected async copyBlock(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      this.snackbar.show('Could not copy the commands');
    }
  }

  protected toggleInstructions(): void {
    this.instructionsOpen.update(open => !open);
  }

  private async loadCourseTitle(): Promise<void> {
    const list = await this.service.listMine();
    if (list === null) {
      return;
    }
    const own = [...list.accepted, ...list.unaccepted].find(entry => entry.id === Number(this.id()));
    if (own === undefined) {
      return;
    }
    this.courseTitle.set(own.courseTitle);
    this.description.set(own.description ?? '');
    this.lastPushAt.set(own.lastPushAt?.toEpochMilli() ?? null);
  }

  private async refreshSubmission(): Promise<void> {
    const submission = await this.service.getSubmission(Number(this.id()));
    if (submission === 'forbidden' || submission === null) {
      return;
    }
    this.submission.set(submission);
    this.applyStatus(submission);
  }
}
