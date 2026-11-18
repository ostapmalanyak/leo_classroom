import { Component, computed, inject, input, InputSignal, OnInit, signal, WritableSignal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Duration, Instant, LocalDateTime, ZoneId } from '@js-joda/core';
import { MatIcon } from '@angular/material/icon';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { MatProgressBar } from '@angular/material/progress-bar';
import {
  Assignment, AssignmentService, AssignmentWrite, DeadlineKind, DownloadSnapshotMode, StarterSourceKind
} from '../../../core/services/assignment-service';
import { SnackbarService } from '../../../core/services/snackbar-service';
import { MarkdownEditor } from '../../shared/markdown-editor/markdown-editor';

/** How many issues the form has: the summary and the save button use it. */
type Issue = { message: string; field: 'title' | 'date' | 'time' | 'url' };

const MAX_TITLE_LENGTH = 100;
const MAX_YEARS_AHEAD = 5;

/**
 * The assignment form in four steps: basics, deadline, starter and lifecycle. Everything
 * is validated as it is typed, so the summary can say "1 issue to fix" while the teacher
 * is still in step one.
 */
@Component({
  selector: 'app-assignment-edit',
  imports: [FormsModule, RouterLink, MatIcon, MatSlideToggle, MatProgressBar, MarkdownEditor],
  templateUrl: './assignment-edit.html',
  styleUrl: './assignment-edit.scss'
})
export class AssignmentEdit implements OnInit {
  private readonly service = inject(AssignmentService);
  private readonly snackbar = inject(SnackbarService);
  private readonly router = inject(Router);

  public readonly id: InputSignal<string | undefined> = input<string>();
  public readonly courseId: InputSignal<string | undefined> = input<string>();

  protected readonly DeadlineKind = DeadlineKind;
  protected readonly StarterSourceKind = StarterSourceKind;
  protected readonly DownloadSnapshotMode = DownloadSnapshotMode;
  protected readonly maxTitleLength = MAX_TITLE_LENGTH;
  protected readonly steps = ['Basics', 'Starter', 'Deadline', 'Lifecycle'];

  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly saving: WritableSignal<boolean> = signal(false);
  protected readonly step: WritableSignal<number> = signal(0);

  protected readonly title: WritableSignal<string> = signal('');
  protected readonly description: WritableSignal<string> = signal('');
  protected readonly hints: WritableSignal<string> = signal('');
  protected readonly deadlineDate: WritableSignal<string> = signal('');
  protected readonly deadlineTime: WritableSignal<string> = signal('');
  protected readonly deadlineKind: WritableSignal<DeadlineKind> = signal(DeadlineKind.None);
  protected readonly hardRevokesRead: WritableSignal<boolean> = signal(false);
  protected readonly starterSource: WritableSignal<StarterSourceKind> = signal(StarterSourceKind.DescriptionOnly);
  protected readonly starterRepoUrl: WritableSignal<string> = signal('');
  protected readonly readme: WritableSignal<string> = signal('');
  protected readonly autoDeleteEnabled: WritableSignal<boolean> = signal(false);
  protected readonly autoDeleteOn: WritableSignal<string> = signal('');
  protected readonly snapshotMode: WritableSignal<DownloadSnapshotMode> = signal(DownloadSnapshotMode.Deadline);

  /** Today, so the date input cannot offer a day that has already passed. */
  protected readonly minDate = computed(() => this.formatDate(new Date()));

  /** Five years ahead, so a year like 44444 has no way into the form. */
  protected readonly maxDate = computed(() => `${new Date().getFullYear() + MAX_YEARS_AHEAD}-12-31`);

  protected readonly isEditMode = computed(() => this.id() !== undefined);
  protected readonly isCopyRepo = computed(() => this.starterSource() === StarterSourceKind.CopyRepo);
  protected readonly isForkRepo = computed(() => this.starterSource() === StarterSourceKind.ForkOwnRepo);

  /** Both starter kinds that point at an existing repository need its address. */
  protected readonly needsRepoUrl = computed(() => this.isCopyRepo() || this.isForkRepo());

  /**
   * A hard deadline closes the repository, so a fork that keeps receiving pushes from the
   * student cannot work with one. The backend rejects that combination; the form prevents it.
   */
  protected readonly forkBlocksHardDeadline = this.isForkRepo;
  protected readonly hasDeadline = computed(() => this.deadlineKind() !== DeadlineKind.None);
  /** The picked date and time as an instant; null while either field is still empty. */
  protected readonly deadline = computed<Instant | null>(() => {
    const date = this.deadlineDate();
    const time = this.deadlineTime();
    if (date === '' || time === '') {
      return null;
    }
    try {
      return LocalDateTime.parse(`${date}T${time}:00`)
                          .atZone(ZoneId.systemDefault())
                          .toInstant();
    } catch {
      return null;
    }
  });
  // ---- validation, recomputed on every change ----

  protected readonly titleError = computed<string | null>(() => {
    const title = this.title().trim();
    if (title === '') {
      return 'A title is required.';
    }

    return title.length > MAX_TITLE_LENGTH
      ? `The title is ${title.length} characters; ${MAX_TITLE_LENGTH} is the limit.`
      : null;
  });

  protected readonly deadlineError = computed<string | null>(() => {
    if (!this.hasDeadline()) {
      return null;
    }

    const date = this.deadlineDate();
    const time = this.deadlineTime();
    if (date === '' || time === '') {
      return 'Pick a date and a time, or switch the deadline to None.';
    }

    const [year, month, day] = date.split('-').map(Number);
    const [hours, minutes] = time.split(':').map(Number);
    const at = new Date(year, month - 1, day, hours, minutes);
    if (Number.isNaN(at.getTime())) {
      return 'That date does not exist. Pick another one.';
    }

    const latestYear = new Date().getFullYear() + MAX_YEARS_AHEAD;
    if (year > latestYear) {
      return `That year is not realistic. Pick a date up to ${latestYear}.`;
    }

    const now = new Date();
    if (at.getTime() <= now.getTime()) {
      return `That date has passed. Pick ${this.messageDate(now)} or later.`;
    }

    return null;
  });

  protected readonly repoError = computed<string | null>(() => {
    if (!this.needsRepoUrl()) {
      return null;
    }

    const url = this.starterRepoUrl().trim();
    if (url === '') {
      return 'A repository URL is required when you copy a repository.';
    }

    return /^https:\/\/[^\s]+\.[^\s]+$/i.test(url)
      ? null
      : 'The repository URL must be a full https address, for example https://forgejo.example/owner/repo.';
  });

  protected readonly issues = computed<Issue[]>(() => {
    const issues: Issue[] = [];
    const title = this.titleError();
    const deadline = this.deadlineError();
    const repo = this.repoError();
    if (title !== null) {
      issues.push({ message: title, field: 'title' });
    }
    if (deadline !== null) {
      issues.push({ message: deadline, field: this.deadlineDate() === '' ? 'date' : 'time' });
    }
    if (repo !== null) {
      issues.push({ message: repo, field: 'url' });
    }

    return issues;
  });

  protected readonly hasIssues = computed(() => this.issues().length > 0);

  /** The line under the summary: what the form still needs. */
  protected readonly issueLabel = computed(() => {
    const count = this.issues().length;

    return count === 0 ? 'Ready to save' : `${count} issue${count === 1 ? '' : 's'} to fix`;
  });

  /** How much of the form is answered, for the bar of the summary. */
  protected readonly progress = computed(() => {
    const done = [
      this.titleError() === null,
      !this.hasDeadline() || this.deadlineError() === null,
      !this.needsRepoUrl() || this.repoError() === null,
      true
    ].filter(Boolean).length;

    return Math.round((done / 4) * 100);
  });

  protected readonly deadlineSummary = computed(() => {
    if (!this.hasDeadline()) {
      return 'None';
    }

    if (this.deadlineError() !== null) {
      return 'Invalid';
    }

    return `${this.messageDate(new Date(`${this.deadlineDate()}T00:00:00`))}, ${this.deadlineTime()}`;
  });

  protected readonly starterSummary = computed(() => {
    if (this.isForkRepo()) {
      return 'Fork own repository';
    }

    return this.isCopyRepo() ? 'Copy repository' : 'Description only';
  });

  /** Picking a fork drops a hard deadline, because that pairing is not allowed. */
  protected setStarterSource(kind: StarterSourceKind): void {
    this.starterSource.set(kind);
    if (kind === StarterSourceKind.ForkOwnRepo && this.deadlineKind() === DeadlineKind.Hard) {
      this.setDeadlineKind(DeadlineKind.Soft);
    }
  }

  // ---- the page ----

  protected get backRoute(): (string | number)[] {
    const id = this.id();

    return id === undefined
      ? ['/my-assignments/course', Number(this.courseId())]
      : ['/assignments', Number(id), 'students'];
  }

  public async ngOnInit(): Promise<void> {
    if (!this.isEditMode()) {
      return;
    }

    this.loading.set(true);
    const result = await this.service.getForEdit(Number(this.id()));
    this.loading.set(false);
    if (result === 'forbidden') {
      this.snackbar.show('You are not allowed to edit this assignment');
      await this.router.navigate(['courses']);

      return;
    }
    if (result === null) {
      this.snackbar.show('Assignment not found');
      await this.router.navigate(['courses']);

      return;
    }
    this.apply(result);
  }

  protected goTo(step: number): void {
    this.step.set(step);
  }

  protected isDone(step: number): boolean {
    if (step === 0) {
      return this.titleError() === null;
    }
    if (step === 1) {
      return this.repoError() === null;
    }
    if (step === 2) {
      return this.deadlineError() === null;
    }

    return false;
  }

  protected setDeadlineKind(kind: DeadlineKind): void {
    this.deadlineKind.set(kind);
    if (kind === DeadlineKind.None) {
      this.deadlineDate.set('');
      this.deadlineTime.set('');
      this.snapshotMode.set(DownloadSnapshotMode.Head);
    }
  }

  /** The three chips of the deadline step. */
  protected quickPick(days: number, tomorrow: boolean): void {
    const at = new Date();
    if (tomorrow) {
      at.setDate(at.getDate() + 1);
    } else {
      at.setDate(at.getDate() + days);
    }
    at.setHours(23, 59, 0, 0);
    this.deadlineDate.set(this.formatDate(at));
    this.deadlineTime.set('23:59');
  }

  protected setSnapshotMode(mode: DownloadSnapshotMode): void {
    if (mode === DownloadSnapshotMode.Deadline && !this.hasDeadline()) {
      return;
    }

    this.snapshotMode.set(mode);
  }

  protected isSnapshotDisabled(mode: DownloadSnapshotMode): boolean {
    return mode === DownloadSnapshotMode.Deadline && !this.hasDeadline();
  }

  // Without a deadline the stored value must not stay on Deadline, otherwise the toggle would show a disabled option as chosen.
  protected readonly effectiveSnapshot = computed<DownloadSnapshotMode>(() =>
    this.hasDeadline() ? this.snapshotMode() : DownloadSnapshotMode.Head);

  /** The save button is always enabled; it points at the first thing that is wrong. */
  protected async handleSaveClicked(): Promise<void> {
    const issues = this.issues();
    if (issues.length > 0) {
      const first = issues[0];
      this.step.set(first.field === 'title' ? 0 : first.field === 'url' ? 1 : 2);
      setTimeout(() => {
        const element = document.getElementById(`field-${first.field}`);
        element?.scrollIntoView({ block: 'center' });
        element?.focus();
      });

      return;
    }

    this.saving.set(true);
    const body = this.buildWrite();
    const result = this.isEditMode()
      ? await this.service.update(Number(this.id()), body)
      : await this.service.create({ ...body, courseId: Number(this.courseId()) });
    this.saving.set(false);

    if (result === 'forbidden') {
      this.snackbar.show('You are not allowed to save this assignment');

      return;
    }
    if (result === null || result === 'not-found' || result === 'conflict') {
      this.snackbar.show('Could not save the assignment');

      return;
    }
    this.snackbar.show('Assignment saved');
    await this.router.navigate(['assignments', result.id, 'students']);
  }

  private buildWrite(): AssignmentWrite {
    return {
      title: this.title().trim(),
      description: this.emptyToNull(this.description()),
      hintsInstructions: this.emptyToNull(this.hints()),
      deadline: this.buildDeadline(),
      deadlineKind: this.deadlineKind(),
      hardDeadlineRevokesRead: this.hardRevokesRead(),
      starterSourceKind: this.starterSource(),
      starterRepoUrl: this.needsRepoUrl() ? this.emptyToNull(this.starterRepoUrl()) : null,
      readmeMarkdown: this.isCopyRepo() ? this.emptyToNull(this.readme()) : null,
      autoDeleteEnabled: this.autoDeleteEnabled(),
      autoDeleteOn: this.emptyToNull(this.autoDeleteOn()),
      downloadSnapshotMode: this.effectiveSnapshot()
    };
  }

  private buildDeadline(): string | null {
    if (!this.hasDeadline() || this.deadlineError() !== null) {
      return null;
    }

    const [year, month, day] = this.deadlineDate().split('-').map(Number);
    const [hours, minutes] = this.deadlineTime().split(':').map(Number);

    return new Date(year, month - 1, day, hours, minutes).toISOString();
  }

  private apply(a: Assignment): void {
    this.title.set(a.title);
    this.description.set(a.description ?? '');
    this.hints.set(a.hintsInstructions ?? '');
    const parsed = a.deadline === null ? null : new Date(a.deadline.toString());
    if (parsed !== null && !Number.isNaN(parsed.getTime())) {
      this.deadlineDate.set(this.formatDate(parsed));
      this.deadlineTime.set(this.formatTime(parsed));
    }
    this.deadlineKind.set(a.deadlineKind);
    this.hardRevokesRead.set(a.hardDeadlineRevokesRead);
    this.starterSource.set(a.starterSourceKind);
    this.starterRepoUrl.set(a.starterRepoUrl ?? '');
    this.readme.set(a.readmeMarkdown ?? '');
    this.autoDeleteEnabled.set(a.autoDeleteEnabled);
    this.autoDeleteOn.set(a.autoDeleteOn?.toString() ?? '');
    this.snapshotMode.set(a.downloadSnapshotMode);
  }

  private formatDate(date: Date): string {
    return `${date.getFullYear()}-${(date.getMonth() + 1).toString().padStart(2, '0')}-${date.getDate().toString().padStart(2, '0')}`;
  }

  // Messages read like the form does in Europe: 18.11.2026, not 2026-11-18.
  private messageDate(date: Date): string {
    return `${date.getDate().toString().padStart(2, '0')}.${(date.getMonth() + 1).toString().padStart(2, '0')}.${date.getFullYear()}`;
  }

  private formatTime(date: Date): string {
    return `${date.getHours().toString().padStart(2, '0')}:${date.getMinutes().toString().padStart(2, '0')}`;
  }

  private emptyToNull(value: string): string | null {
    const trimmed = value.trim();

    return trimmed === '' ? null : trimmed;
  }
}
