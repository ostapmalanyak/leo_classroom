import {
  Component, computed, DestroyRef, inject, input, InputSignal, OnInit, signal, WritableSignal
} from '@angular/core';
import { Instant } from '@js-joda/core';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import {
  AssignmentInfo, AssignmentService, DownloadSnapshotMode, StudentSubmission, SubmissionStatus
} from '../../../core/services/assignment-service';
import { DownloadJob, DownloadJobStatus, DownloadService } from '../../../core/services/download-service';
import { dueLabel } from '../../../core/util/assignment-state';

/** The card shows exactly one of these; everything else on the page stays put. */
type Phase = 'choose' | 'failed' | 'ready' | 'running';

/**
 * The teacher's "download all submissions" page. It answers three questions in a single card:
 * what goes into the zip, how far the export is, and where the finished file is.
 */
@Component({
  selector: 'app-assignment-download',
  imports: [MatIcon, MatProgressBar],
  templateUrl: './assignment-download.html',
  styleUrl: './assignment-download.scss'
})
export class AssignmentDownload implements OnInit {
  protected readonly DownloadSnapshotMode = DownloadSnapshotMode;
  protected readonly DownloadJobStatus = DownloadJobStatus;

  private readonly assignments = inject(AssignmentService);
  private readonly downloads = inject(DownloadService);
  private readonly destroyRef = inject(DestroyRef);

  public readonly id: InputSignal<string> = input.required<string>();

  /** `null` is the assignment's own configured snapshot, so it is a real choice, not "nothing". */
  protected readonly mode: WritableSignal<DownloadSnapshotMode | null> = signal(null);
  protected readonly job: WritableSignal<DownloadJob | null> = signal(null);
  protected readonly students: WritableSignal<StudentSubmission[] | null> = signal(null);
  protected readonly info: WritableSignal<AssignmentInfo | null> = signal(null);
  protected readonly busy: WritableSignal<boolean> = signal(false);
  protected readonly notice: WritableSignal<string | null> = signal(null);

  private timer: ReturnType<typeof setTimeout> | null = null;

  protected readonly phase = computed<Phase>(() => {
    const job = this.job();
    if (job === null) {
      return 'choose';
    }
    switch (job.status) {
      case DownloadJobStatus.Pending:
      case DownloadJobStatus.Running:
        return 'running';
      case DownloadJobStatus.Ready:
        return 'ready';
      default:
        return 'failed';
    }
  });

  /** Acceptances that own a repository; a download-only acceptance has nothing to clone. */
  private readonly repositories = computed(() => (this.students() ?? [])
    .filter(student => student.status !== SubmissionStatus.DownloadOnly));

  protected readonly repositoryCount = computed(() => this.repositories().length);
  protected readonly repositoryWord = computed(() => this.repositoryCount() === 1 ? 'repository' : 'repositories');
  protected readonly noCommitCount = computed(
    () => this.repositories().filter(student => student.lastPushAt === null).length);

  protected readonly modeSummary = computed(() => {
    switch (this.mode()) {
      case DownloadSnapshotMode.Deadline:
        return 'last commit before the deadline';
      case DownloadSnapshotMode.Head:
        return 'everything pushed';
      default:
        return 'latest commit on main';
    }
  });

  protected readonly runningLabel = computed(
    () => this.job()?.status === DownloadJobStatus.Running ? 'Cloning repositories' : 'Waiting to start');

  protected readonly fileName = computed(
    () => this.job()?.fileName ?? `assignment-${this.id()}-submissions.zip`);

  protected readonly fileMeta = computed(() => {
    const folders = this.repositoryCount();
    const parts: string[] = [`${folders} ${folders === 1 ? 'folder' : 'folders'}`];
    const size = this.job()?.sizeBytes ?? null;
    if (size !== null) {
      parts.push(AssignmentDownload.formatSize(size));
    }
    const expires = this.job()?.expiresAt ?? null;
    if (expires !== null) {
      parts.push(`expires ${dueLabel(Instant.parse(expires)).toLowerCase()}`);
    }

    return parts.join(' · ');
  });

  protected readonly failureText = computed(() => {
    const error = (this.job()?.error ?? '').replace(/\.$/, '');
    if (error === '') {
      return 'The export stopped before the zip was written';
    }

    return `The export stopped: ${error.charAt(0).toUpperCase()}${error.slice(1)}`;
  });

  public constructor() {
    this.destroyRef.onDestroy(() => this.clearTimer());
  }

  public ngOnInit(): void {
    void this.load();
  }

  protected select(mode: DownloadSnapshotMode | null): void {
    this.mode.set(mode);
    this.notice.set(null);
  }

  protected async start(): Promise<void> {
    if (this.busy()) {
      return;
    }
    if (this.repositoryCount() === 0) {
      this.notice.set('There is nothing to export yet, no one has a repository');

      return;
    }

    this.busy.set(true);
    this.notice.set(null);
    const result = await this.downloads.trigger(Number(this.id()), this.mode());
    this.busy.set(false);
    if (result === 'forbidden' || result === 'not-found' || result === null) {
      this.notice.set('Could not start the export');

      return;
    }

    this.remember(result);
    await this.refresh(result);
  }

  protected async download(): Promise<void> {
    const current = this.job();
    if (current === null) {
      return;
    }
    const result = await this.downloads.downloadArtifact(Number(this.id()), current.id);
    if (result !== 'ok') {
      this.notice.set('Could not download the zip');
    }
  }

  /** Back to the choose state, so a second export starts from a clean card. */
  protected reset(): void {
    this.clearTimer();
    this.forget();
    this.job.set(null);
    this.notice.set(null);
  }

  private async load(): Promise<void> {
    const [students, info] = await Promise.all([
      this.assignments.getStudents(Number(this.id())),
      this.assignments.getInfo(Number(this.id()))
    ]);
    this.students.set(students);
    if (info !== 'forbidden' && info !== null) {
      this.info.set(info);
    }

    // The job keeps running on the server, so a page opened earlier in the day picks it up again.
    const remembered = this.remembered();
    if (remembered !== null) {
      await this.refresh(remembered);
    }
  }

  private async refresh(jobId: number): Promise<void> {
    const result = await this.downloads.getStatus(Number(this.id()), jobId);
    if (result === 'forbidden' || result === 'not-found' || result === null) {
      this.forget();
      this.job.set(null);

      return;
    }

    this.job.set(result);
    if (result.status === DownloadJobStatus.Ready) {
      return;
    }
    if (result.status === DownloadJobStatus.Failed) {
      this.forget();

      return;
    }

    this.clearTimer();
    this.timer = setTimeout(() => void this.refresh(jobId), 2000);
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }

  private get storageKey(): string {
    return `leo-classroom.download-job.${this.id()}`;
  }

  private remember(jobId: number): void {
    try {
      localStorage.setItem(this.storageKey, String(jobId));
    } catch {
      // A private window without storage only loses the resume, not the export.
    }
  }

  private remembered(): number | null {
    try {
      const stored = localStorage.getItem(this.storageKey);
      const jobId = stored === null ? Number.NaN : Number(stored);

      return Number.isFinite(jobId) && jobId > 0 ? jobId : null;
    } catch {
      return null;
    }
  }

  private forget(): void {
    try {
      localStorage.removeItem(this.storageKey);
    } catch {
      // Nothing to clean up when storage is unavailable.
    }
  }

  private static formatSize(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }
    const units = ['kB', 'MB', 'GB'];
    let value = bytes / 1024;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
      value /= 1024;
      unit += 1;
    }

    return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unit]}`;
  }
}
