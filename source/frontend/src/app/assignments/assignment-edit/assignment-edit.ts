import { Component, computed, inject, input, InputSignal, OnInit, signal, WritableSignal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Duration, Instant, LocalDateTime, ZoneId } from '@js-joda/core';
import { MatIcon } from '@angular/material/icon';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { MatProgressBar } from '@angular/material/progress-bar';
import {
  Assignment, AssignmentService, AssignmentWrite, DeadlineKind, DownloadSnapshotMode, RepositoryPreview,
  StarterArchivePreview, StarterSourceKind,
} from '../../../core/services/assignment-service';
import { SnackbarService } from '../../../core/services/snackbar-service';
import { isHiddenNow } from '../../../core/util/assignment-state';
import { MarkdownEditor } from '../../shared/markdown-editor/markdown-editor';

/** How many issues the form has: the summary and the save button use it. */
type Issue = { message: string; field: 'title' | 'date' | 'time' | 'url' | 'publish' | 'archive' };

/** When students get to see the assignment. */
type Visibility = 'now' | 'date' | 'manual';

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
  protected readonly repoStarterMode: WritableSignal<'repository' | 'archive'> = signal('repository');
  protected readonly archivePreview: WritableSignal<StarterArchivePreview | null> = signal(null);
  protected readonly archiveUploading: WritableSignal<boolean> = signal(false);
  protected readonly archiveUploadError: WritableSignal<string | null> = signal(null);
  protected readonly archiveConfirmed: WritableSignal<boolean> = signal(false);
  protected readonly archiveConfigured: WritableSignal<boolean> = signal(false);
  protected readonly archiveHasTemplate: WritableSignal<boolean> = signal(false);
  protected readonly readme: WritableSignal<string> = signal('');
  protected readonly autoDeleteEnabled: WritableSignal<boolean> = signal(false);
  protected readonly autoDeleteOn: WritableSignal<string> = signal('');
  protected readonly snapshotMode: WritableSignal<DownloadSnapshotMode> = signal(DownloadSnapshotMode.Deadline);
  protected readonly visibility: WritableSignal<Visibility> = signal('now');
  protected readonly publishDate: WritableSignal<string> = signal('');
  protected readonly publishTime: WritableSignal<string> = signal('');
  /** A published assignment stays published: students may already have accepted it. */
  protected readonly alreadyPublished: WritableSignal<boolean> = signal(false);

  /** Today, so the date input cannot offer a day that has already passed. */
  protected readonly minDate = computed(() => this.formatDate(new Date()));

  /** Five years ahead, so a year like 44444 has no way into the form. */
  protected readonly maxDate = computed(() => `${new Date().getFullYear() + MAX_YEARS_AHEAD}-12-31`);

  protected readonly isEditMode = computed(() => this.id() !== undefined);
  protected readonly isCopyRepo = computed(() => this.starterSource() === StarterSourceKind.CopyRepo);
  protected readonly isForkRepo = computed(() => this.starterSource() === StarterSourceKind.ForkOwnRepo);
  protected readonly isArchive = computed(() => this.starterSource() === StarterSourceKind.Archive);

  /** Copy and fork starters use either a repository URL or a ZIP upload. */
  protected readonly needsRepoUrl = computed(() => this.isCopyRepo() || this.isForkRepo());
  protected readonly usesArchiveUpload = computed(() =>
    this.isArchive() || (!this.isEditMode() && this.needsRepoUrl() && this.repoStarterMode() === 'archive'));

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

  protected readonly publishError = computed<string | null>(() => {
    if (this.visibility() !== 'date') {
      return null;
    }

    const at = this.parseLocal(this.publishDate(), this.publishTime());
    if (at === null) {
      return 'Pick a date and a time, or choose to publish it yourself.';
    }
    if (at.getTime() <= Date.now()) {
      return 'That moment has passed. Pick a time in the future.';
    }

    const deadline = this.deadlineError() === null && this.hasDeadline()
      ? this.parseLocal(this.deadlineDate(), this.deadlineTime())
      : null;

    return deadline !== null && at.getTime() >= deadline.getTime()
      ? 'The assignment must appear before its deadline.'
      : null;
  });

  protected readonly repoError = computed<string | null>(() => {
    if (!this.needsRepoUrl() || this.usesArchiveUpload()) {
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

  protected readonly archiveError = computed<string | null>(() => {
    if (!this.usesArchiveUpload()) {
      return null;
    }
    if (this.isEditMode() && this.isArchive() && this.archiveConfigured()) {
      return null;
    }
    if (this.archiveUploadError() !== null) {
      return this.archiveUploadError();
    }
    if (this.archivePreview() === null) {
      return 'Choose a ZIP archive to preview its contents.';
    }

    return this.archiveConfirmed() ? null : 'Review and confirm the archive contents.';
  });

  protected readonly issues = computed<Issue[]>(() => {
    const issues: Issue[] = [];
    const title = this.titleError();
    const deadline = this.deadlineError();
    const repo = this.repoError();
    const archive = this.archiveError();
    const publish = this.publishError();
    if (title !== null) {
      issues.push({ message: title, field: 'title' });
    }
    if (deadline !== null) {
      issues.push({ message: deadline, field: this.deadlineDate() === '' ? 'date' : 'time' });
    }
    if (publish !== null) {
      issues.push({ message: publish, field: 'publish' });
    }
    if (repo !== null) {
      issues.push({ message: repo, field: 'url' });
    }
    if (archive !== null) {
      issues.push({ message: archive, field: 'archive' });
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
      (!this.hasDeadline() || this.deadlineError() === null) && this.publishError() === null,
      (!this.needsRepoUrl() || this.repoError() === null) && this.archiveError() === null,
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

  protected readonly visibilitySummary = computed(() => {
    if (this.alreadyPublished() || this.visibility() === 'now') {
      return 'Visible to students';
    }
    if (this.visibility() === 'manual') {
      return 'Hidden until you publish';
    }

    return this.parseLocal(this.publishDate(), this.publishTime()) === null
      ? 'Hidden, date missing'
      : `Appears ${this.messageDate(new Date(`${this.publishDate()}T00:00:00`))}, ${this.publishTime()}`;
  });

  protected readonly starterSummary = computed(() => {
    if (this.isArchive()) {
      const entries = this.archivePreview()?.entries.length;

      return entries === undefined ? 'Downloadable ZIP archive' : `Downloadable ZIP · ${entries} entries`;
    }
    if (this.usesArchiveUpload()) {
      return this.isForkRepo() ? 'Fork repository from ZIP' : 'Copy repository from ZIP';
    }
    if (this.isForkRepo()) {
      return 'Fork own repository';
    }

    return this.isCopyRepo() ? 'Copy repository' : 'Description only';
  });

  /** Picking a fork drops a hard deadline, because that pairing is not allowed. */
  // Layout B: one bar takes a dropped ZIP, a browse click, and (fork only) a pasted link.
  protected readonly dragging = signal(false);
  protected readonly starterMessage = signal<string | null>(null);
  protected readonly resolvingLink = signal(false);
  protected readonly archiveBytes = signal<number | null>(null);
  protected readonly repoPreview = signal<RepositoryPreview | null>(null);

  private dragDepth = 0;

  protected readonly starterFilled = computed(() =>
    this.isArchive() ? this.archivePreview() !== null : this.repoPreview() !== null);

  protected readonly starterPlaceholder = computed(() =>
    this.isArchive() ? 'Drop a ZIP here' : 'Drop a ZIP or paste a repository link');

  protected readonly starterIcon = computed(() => this.isArchive() ? 'folder_zip' : 'repository');

  protected readonly starterName = computed(() => this.isArchive()
    ? (this.archivePreview()?.fileName ?? '')
    : (this.repoPreview()?.fullName ?? ''));

  protected readonly starterMeta = computed(() => {
    if (this.isArchive()) {
      const bytes = this.archiveBytes();
      const entries = this.archivePreview()?.entries.length ?? 0;

      return [bytes === null ? null : AssignmentEdit.sizeLabel(bytes), `${entries} ${entries === 1 ? 'entry' : 'entries'}`]
        .filter(part => part !== null)
        .join(' · ');
    }

    const repo = this.repoPreview();
    if (repo === null) {
      return '';
    }

    return `${repo.defaultBranch} · ${repo.private ? 'private' : 'public'} · ${AssignmentEdit.plural(repo.files, 'file')}`;
  });

  protected readonly treeShown = computed(() => (this.archivePreview()?.entries ?? []).slice(0, 12));

  protected readonly treeHidden = computed(() => Math.max(0, (this.archivePreview()?.entries.length ?? 0) - 12));

  protected readonly treeTotal = computed(() => {
    const entries = this.archivePreview()?.entries ?? [];
    const folders = entries.filter(entry => entry.endsWith('/')).length;

    return `${AssignmentEdit.plural(entries.length - folders, 'file')} · ${AssignmentEdit.plural(folders, 'folder')}`;
  });

  private static plural(count: number, word: string): string {
    return `${count} ${word}${count === 1 ? '' : 's'}`;
  }

  protected onDragEnter(event: DragEvent): void {
    event.preventDefault();
    this.dragDepth += 1;
    this.dragging.set(true);
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    if (event.dataTransfer !== null) {
      event.dataTransfer.dropEffect = 'copy';
    }
  }

  // Counting the leaves keeps the highlight steady while the pointer crosses the children.
  protected onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragDepth = Math.max(0, this.dragDepth - 1);
    if (this.dragDepth === 0) {
      this.dragging.set(false);
    }
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragDepth = 0;
    this.dragging.set(false);
    const files = event.dataTransfer?.files ?? null;
    if (files === null || files.length === 0) {
      return;
    }
    if (files.length > 1) {
      this.starterMessage.set('Drop one ZIP at a time');

      return;
    }
    void this.acceptArchive(files[0]);
  }

  protected onFilePicked(event: Event): void {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file === undefined) {
      return;
    }
    void this.acceptArchive(file);
  }

  protected onLinkInput(value: string): void {
    this.starterRepoUrl.set(value);
    this.repoPreview.set(null);
    this.starterMessage.set(null);
  }

  protected async resolveLink(): Promise<void> {
    const url = this.starterRepoUrl().trim();
    if (url === '' || this.resolvingLink()) {
      return;
    }

    this.resolvingLink.set(true);
    this.starterMessage.set(null);
    const result = await this.service.previewRepository(url);
    this.resolvingLink.set(false);
    if (typeof result === 'string') {
      this.starterMessage.set(result);

      return;
    }

    this.archivePreview.set(null);
    this.archiveConfirmed.set(false);
    this.repoPreview.set(result);
  }

  protected removeStarter(): void {
    this.archivePreview.set(null);
    this.archiveConfirmed.set(false);
    this.archiveBytes.set(null);
    this.archiveUploadError.set(null);
    this.repoPreview.set(null);
    this.starterRepoUrl.set('');
    this.starterMessage.set(null);
  }

  /** Step two is never blocked; the reason lives next to the starter bar instead. */
  protected next(): void {
    if (this.step() === 1 && (this.isArchive() || this.isForkRepo()) && !this.starterFilled()) {
      this.starterMessage.set(this.isArchive()
        ? 'Choose a ZIP to continue'
        : 'Choose a ZIP or paste a link to continue');

      return;
    }

    this.starterMessage.set(null);
    this.goTo(this.step() + 1);
  }

  private async acceptArchive(file: File): Promise<void> {
    this.starterMessage.set(null);
    if (!file.name.toLowerCase().endsWith('.zip')) {
      this.starterMessage.set('That file is not a ZIP. Choose a .zip file');

      return;
    }
    if (file.size > 50 * 1024 * 1024) {
      this.starterMessage.set('That ZIP is larger than 50 MB. Choose a smaller one');

      return;
    }

    this.archivePreview.set(null);
    this.archiveConfirmed.set(false);
    this.archiveUploadError.set(null);
    this.repoPreview.set(null);
    this.archiveUploading.set(true);
    const result = await this.service.previewStarterArchive(file);
    this.archiveUploading.set(false);
    if (typeof result === 'string') {
      this.archiveUploadError.set(result);
      this.starterMessage.set('Could not read that ZIP. Check that it opens on your computer and is under 50 MB');

      return;
    }

    this.archiveBytes.set(file.size);
    this.archivePreview.set(result);
  }

  private static sizeLabel(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }
    const units = ['kB', 'MB'];
    let value = bytes / 1024;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
      value /= 1024;
      unit += 1;
    }

    return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unit]}`;
  }

  protected setStarterSource(kind: StarterSourceKind): void {
    this.starterSource.set(kind);
    if (kind === StarterSourceKind.ForkOwnRepo && this.deadlineKind() === DeadlineKind.Hard) {
      this.setDeadlineKind(DeadlineKind.Soft);
    }
  }

  protected setRepoStarterMode(mode: 'repository' | 'archive'): void {
    this.repoStarterMode.set(mode);
  }

  protected async handleArchiveFileChanged(event: Event): Promise<void> {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];
    this.archivePreview.set(null);
    this.archiveConfirmed.set(false);
    this.archiveUploadError.set(null);
    if (file === undefined) {
      return;
    }

    this.archiveUploading.set(true);
    const result = await this.service.previewStarterArchive(file);
    this.archiveUploading.set(false);
    if (typeof result === 'string') {
      this.archiveUploadError.set(result);

      return;
    }

    this.archivePreview.set(result);
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
      return this.repoError() === null && this.archiveError() === null;
    }
    if (step === 2) {
      return this.deadlineError() === null && this.publishError() === null;
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

  protected setVisibility(visibility: Visibility): void {
    this.visibility.set(visibility);
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
      this.step.set(first.field === 'title' ? 0 : first.field === 'url' || first.field === 'archive' ? 1 : 2);
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
      : await this.service.create({
        ...body,
        courseId: Number(this.courseId()),
        starterArchiveUploadId: this.usesArchiveUpload() ? this.archivePreview()?.uploadId ?? null : null
      });
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
      starterRepoUrl: this.needsRepoUrl() && !this.usesArchiveUpload()
        ? this.emptyToNull(this.starterRepoUrl())
        : null,
      readmeMarkdown: this.isCopyRepo() ? this.emptyToNull(this.readme()) : null,
      autoDeleteEnabled: this.autoDeleteEnabled(),
      autoDeleteOn: this.emptyToNull(this.autoDeleteOn()),
      downloadSnapshotMode: this.effectiveSnapshot(),
      isPublished: this.alreadyPublished() || this.visibility() === 'now',
      publishAt: this.buildPublishAt()
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

  private buildPublishAt(): string | null {
    if (this.alreadyPublished() || this.visibility() !== 'date') {
      return null;
    }

    return this.parseLocal(this.publishDate(), this.publishTime())?.toISOString() ?? null;
  }

  /** A local date and time from the two inputs, or null while either is empty or impossible. */
  private parseLocal(date: string, time: string): Date | null {
    if (date === '' || time === '') {
      return null;
    }

    const [year, month, day] = date.split('-').map(Number);
    const [hours, minutes] = time.split(':').map(Number);
    const at = new Date(year, month - 1, day, hours, minutes);

    return Number.isNaN(at.getTime()) ? null : at;
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
    this.archiveConfigured.set(a.hasStarterArchive);
    this.archiveHasTemplate.set(a.starterTemplateRepoUrl !== null);
    this.readme.set(a.readmeMarkdown ?? '');
    this.autoDeleteEnabled.set(a.autoDeleteEnabled);
    this.autoDeleteOn.set(a.autoDeleteOn?.toString() ?? '');
    this.snapshotMode.set(a.downloadSnapshotMode);
    // a scheduled time that has passed already counts as published: students can see it
    const hidden = isHiddenNow(a, Instant.now());
    this.alreadyPublished.set(!hidden);
    if (hidden) {
      const publishAt = a.publishAt === null ? null : new Date(a.publishAt.toString());
      if (publishAt !== null && !Number.isNaN(publishAt.getTime())) {
        this.visibility.set('date');
        this.publishDate.set(this.formatDate(publishAt));
        this.publishTime.set(this.formatTime(publishAt));
      } else {
        this.visibility.set('manual');
      }
    }
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
