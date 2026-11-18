import { Component, computed, inject, input, InputSignal, OnInit, signal, WritableSignal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatTooltip } from '@angular/material/tooltip';
import {
  Assignment, AssignmentService, DeadlineKind, FeedbackState, StudentSubmission, SubmissionStatus
} from '../../../core/services/assignment-service';
import { CourseService } from '../../../core/services/course-service';
import { NowService } from '../../../core/services/now-service';
import { SnackbarService } from '../../../core/services/snackbar-service';
import { dueLabel, relativeDue, timestampLabel } from '../../../core/util/assignment-state';
import { DeadlineTimeline } from '../../shared/deadline-timeline/deadline-timeline';

type StatusTone = 'success' | 'accent' | 'danger' | 'neutral' | 'info';
type FilterKey = 'all' | 'not-accepted' | 'needs-feedback' | 'late';
type SortKey = 'last-commit' | 'name' | 'status';

interface SubmissionRow {
  acceptanceId: number | null;
  studentId: number;
  name: string;
  studentNumber: string | null;
  initials: string;
  statusLabel: string;
  statusTone: StatusTone;
  accepted: boolean;
  late: boolean;
  repoUrl: string | null;
  lastCommitLabel: string;
  lastCommitTitle: string;
  lastCommitAt: number | null;
  feedbackLabel: string;
  feedbackTone: 'warning' | 'success' | 'none';
  /** Pushed work that the teacher has not reviewed yet. */
  needsFeedback: boolean;
}

const STATUS_RANK: Record<string, number> = {
  'Late': 0,
  'Submitted': 1,
  'Ready': 2,
  'Setting up': 3,
  'Setup failed': 4,
  'Not accepted': 5
};

@Component({
  selector: 'app-assignment-students',
  imports: [RouterLink, MatButton, MatIcon, MatProgressBar, MatTooltip, DeadlineTimeline],
  templateUrl: './assignment-students.html',
  styleUrl: './assignment-students.scss'
})
export class AssignmentStudents implements OnInit {
  private readonly service = inject(AssignmentService);
  private readonly courses = inject(CourseService);
  private readonly snackbar = inject(SnackbarService);
  private readonly router = inject(Router);
  private readonly clock = inject(NowService);

  public readonly id: InputSignal<string> = input.required<string>();

  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly assignment: WritableSignal<Assignment | null> = signal(null);
  protected readonly courseTitle: WritableSignal<string> = signal('');
  protected readonly rows: WritableSignal<SubmissionRow[]> = signal([]);
  protected readonly selected: WritableSignal<FilterKey> = signal('all');
  protected readonly sort: WritableSignal<SortKey> = signal('last-commit');
  protected readonly search: WritableSignal<string> = signal('');
  protected readonly copied: WritableSignal<boolean> = signal(false);

  protected readonly DeadlineKind = DeadlineKind;
  /** Students open (and accept) the assignment here, so this is the page to send them. */
  protected readonly studentUrl = computed(() => `${window.location.origin}/my-assignments/${this.id()}`);

  protected get backRoute(): (string | number)[] {
    const assignment = this.assignment();

    return assignment === null ? ['/my-courses'] : ['/my-assignments/course', assignment.courseId];
  }

  protected readonly deadline = computed(() => this.assignment()?.deadline ?? null);
  protected readonly deadlineKind = computed(() => this.assignment()?.deadlineKind ?? DeadlineKind.None);
  protected readonly deadlineLabel = computed(() => {
    const deadline = this.deadline();

    return deadline === null ? '' : dueLabel(deadline);
  });
  protected readonly title = computed(() => this.assignment()?.title ?? '');

  /** Everything the table shows, before the toolbar narrows it. */
  protected readonly allRows = computed(() => this.rows());

  protected readonly acceptedCount = computed(() => this.allRows().filter(row => row.accepted).length);
  protected readonly totalCount = computed(() => this.allRows().length);
  protected readonly pushedCount = computed(() => this.allRows().filter(row => row.lastCommitAt !== null).length);
  protected readonly lateCount = computed(() => this.allRows().filter(row => row.late).length);
  protected readonly needsFeedbackCount = computed(() => this.allRows().filter(row => row.needsFeedback).length);

  protected readonly filters = computed(() => ([
    { key: 'all' as const, label: 'All', count: this.totalCount() },
    { key: 'not-accepted' as const, label: 'Not accepted', count: this.totalCount() - this.acceptedCount() },
    { key: 'needs-feedback' as const, label: 'Needs feedback', count: this.needsFeedbackCount() },
    { key: 'late' as const, label: 'Late', count: this.lateCount() }
  ]));

  /** A hard deadline never marks students late, so the chip disappears with the metric. */
  protected readonly showLate = computed(() => this.deadlineKind() !== DeadlineKind.Hard);

  protected readonly visibleRows = computed(() => {
    const term = this.search().trim().toLowerCase();
    const filter = this.selected();
    const sort = this.sort();

    const filtered = this.allRows().filter(row => {
      if (term !== '' && !`${row.name} ${row.studentNumber ?? ''}`.toLowerCase().includes(term)) {
        return false;
      }
      switch (filter) {
        case 'not-accepted':
          return !row.accepted;
        case 'needs-feedback':
          return row.needsFeedback;
        case 'late':
          return row.late;
        default:
          return true;
      }
    });

    return [...filtered].sort((left, right) => this.compare(left, right, sort));
  });

  public async ngOnInit(): Promise<void> {
    // The reload needs the course id to merge in the students who have not accepted yet,
    // so the assignment (and its course) has to be there first.
    await this.loadAssignment();
    await this.reload();
  }

  protected setFilter(key: FilterKey): void {
    this.selected.set(key);
  }

  protected setSort(event: Event): void {
    this.sort.set((event.target as HTMLSelectElement).value as SortKey);
  }

  protected setSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  protected async copyLink(): Promise<void> {
    const copied = await this.copyToClipboard(this.studentUrl());
    this.copied.set(copied);
    if (copied) {
      setTimeout(() => this.copied.set(false), 2000);
    }
  }

  /** There is no reminder endpoint, so the teacher gets the student link to send it themselves. */
  protected async remind(row: SubmissionRow): Promise<void> {
    const copied = await this.copyToClipboard(this.studentUrl());
    this.snackbar.show(copied
      ? `Student link copied — send it to ${row.name}`
      : 'Could not copy the student link');
  }

  protected async handleEditClicked(): Promise<void> {
    await this.router.navigate(['assignments', this.id(), 'edit']);
  }

  protected async handleDownloadAllClicked(): Promise<void> {
    await this.router.navigate(['assignments', this.id(), 'download']);
  }

  protected async viewAnalytics(row: SubmissionRow): Promise<void> {
    if (row.acceptanceId === null) {
      return;
    }
    await this.router.navigate(['assignments', this.id(), 'acceptances', row.acceptanceId, 'analytics']);
  }

  protected async openFeedbackPr(row: SubmissionRow): Promise<void> {
    if (row.acceptanceId === null) {
      return;
    }
    const result = await this.service.openFeedbackPr(row.acceptanceId);
    if (result === 'forbidden' || result === 'not-found' || result === null) {
      this.snackbar.show('Could not open the feedback pull request');

      return;
    }
    window.open(result.htmlUrl, '_blank', 'noopener');
    this.snackbar.show('Feedback pull request ready');
  }

  protected openRepository(row: SubmissionRow): void {
    if (row.repoUrl !== null) {
      window.open(row.repoUrl, '_blank', 'noopener');
    }
  }

  private compare(left: SubmissionRow, right: SubmissionRow, sort: SortKey): number {
    switch (sort) {
      case 'name':
        return left.name.localeCompare(right.name);
      case 'status':
        return (STATUS_RANK[left.statusLabel] ?? 9) - (STATUS_RANK[right.statusLabel] ?? 9)
          || left.name.localeCompare(right.name);
      default:
        // Newest push first; students without a push sink to the bottom.
        return (right.lastCommitAt ?? -1) - (left.lastCommitAt ?? -1);
    }
  }

  private async loadAssignment(): Promise<void> {
    const assignment = await this.service.getForEdit(Number(this.id()));
    if (assignment === 'forbidden' || assignment === null) {
      return;
    }
    this.assignment.set(assignment);
    const course = await this.courses.getCourseById(assignment.courseId);
    if (course !== null) {
      this.courseTitle.set(course.title);
    }
  }

  private async reload(): Promise<void> {
    this.loading.set(true);
    const submissions = await this.service.getStudents(Number(this.id()));
    this.loading.set(false);
    if (submissions === null) {
      this.snackbar.show('Could not load the student list');

      return;
    }

    const accepted = submissions.map(submission => this.toRow(submission));
    const knownIds = new Set(accepted.map(row => row.studentId));
    const courseId = this.assignment()?.courseId ?? null;
    const participants = courseId === null ? null : await this.courses.getParticipants(courseId);
    const missing = participants === null
      ? []
      : participants.participants
        .filter(participant => !knownIds.has(participant.id))
        .map(participant => this.toUnacceptedRow(participant.id, participant.firstName, participant.lastName));

    this.rows.set([...accepted, ...missing]);
  }

  private toRow(submission: StudentSubmission): SubmissionRow {
    const now = this.clock.now();
    const status = this.statusOf(submission);
    const feedback = this.feedbackOf(submission);

    return {
      acceptanceId: submission.acceptanceId,
      studentId: submission.studentId,
      name: `${submission.lastName} ${submission.firstName}`,
      studentNumber: submission.studentNumber,
      initials: this.initialsOf(submission.firstName, submission.lastName),
      statusLabel: status.label,
      statusTone: status.tone,
      accepted: true,
      late: submission.late,
      repoUrl: submission.repoUrl,
      lastCommitLabel: submission.lastCommitAt === null
        ? 'No push yet'
        : relativeDue(submission.lastCommitAt, now),
      lastCommitTitle: submission.lastCommitAt === null ? '' : timestampLabel(submission.lastCommitAt),
      lastCommitAt: submission.lastCommitAt?.toEpochMilli() ?? null,
      feedbackLabel: feedback.label,
      feedbackTone: feedback.tone,
      needsFeedback: submission.feedbackState === FeedbackState.None && submission.lastPushAt !== null
    };
  }

  private toUnacceptedRow(id: number, firstName: string, lastName: string): SubmissionRow {
    return {
      acceptanceId: null,
      studentId: id,
      name: `${lastName} ${firstName}`,
      studentNumber: null,
      initials: this.initialsOf(firstName, lastName),
      statusLabel: 'Not accepted',
      statusTone: 'neutral',
      accepted: false,
      late: false,
      repoUrl: null,
      lastCommitLabel: '—',
      lastCommitTitle: '',
      lastCommitAt: null,
      feedbackLabel: '—',
      feedbackTone: 'none',
      needsFeedback: false
    };
  }

  private statusOf(submission: StudentSubmission): { label: string; tone: StatusTone } {
    if (submission.status === SubmissionStatus.Failed) {
      return { label: 'Setup failed', tone: 'danger' };
    }
    if (submission.status === SubmissionStatus.Provisioning) {
      return { label: 'Setting up', tone: 'info' };
    }
    if (submission.late) {
      return { label: 'Late', tone: 'danger' };
    }
    if (submission.lastPushAt !== null) {
      return { label: 'Submitted', tone: 'accent' };
    }

    return { label: 'Ready', tone: 'success' };
  }

  private feedbackOf(submission: StudentSubmission): {
    label: string;
    tone: 'warning' | 'success' | 'none'
  } {
    switch (submission.feedbackState) {
      case FeedbackState.Unread:
        return { label: 'Pending', tone: 'warning' };
      case FeedbackState.Read:
        return { label: 'Sent', tone: 'success' };
      default:
        return { label: '—', tone: 'none' };
    }
  }

  private initialsOf(firstName: string, lastName: string): string {
    const initials = `${firstName.trim().charAt(0)}${lastName.trim().charAt(0)}`.toUpperCase();

    return initials === '' ? '?' : initials;
  }

  private async copyToClipboard(text: string): Promise<boolean> {
    try {
      await navigator.clipboard.writeText(text);

      return true;
    } catch {
      return false;
    }
  }
}
