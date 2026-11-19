import { Component, computed, inject, input, InputSignal, OnInit, signal, WritableSignal } from '@angular/core';
import { Router } from '@angular/router';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { MatProgressBar } from '@angular/material/progress-bar';
import {
  Assignment, AssignmentService, DeadlineKind, FeedbackState, StudentSubmission, SubmissionStatus
} from '../../../core/services/assignment-service';
import { CourseService } from '../../../core/services/course-service';
import { NowService } from '../../../core/services/now-service';
import { SnackbarService } from '../../../core/services/snackbar-service';
import { relativeDue, timestampLabel } from '../../../core/util/assignment-state';
import { DeadlineTimeline } from '../../shared/deadline-timeline/deadline-timeline';

type BadgeTone = 'neutral' | 'accent' | 'warning' | 'success' | 'danger';

interface StudentRow {
  acceptanceId: number | null;
  studentId: number;
  name: string;
  studentNumber: string | null;
  initials: string;
  commits: number | null;
  pushedLabel: string;
  pushedTitle: string;
  badge: { label: string; tone: BadgeTone; icon: string | null } | null;
  action: { label: string; kind: 'feedback' | 'repository' } | null;
  repoUrl: string | null;
  accepted: boolean;
  needsReview: boolean;
}

/** How many avatars the collapsed "not accepted" group shows before it counts the rest. */
const AVATAR_STACK = 3;

/**
 * One assignment as the teacher sees it: who handed in, what still waits for a review, and who
 * has not started. Everything else about the assignment lives behind the edit page.
 */
@Component({
  selector: 'app-assignment-students',
  imports: [MatIcon, MatMenu, MatMenuItem, MatMenuTrigger, MatProgressBar, DeadlineTimeline],
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
  protected readonly rows: WritableSignal<StudentRow[]> = signal([]);
  protected readonly copied: WritableSignal<boolean> = signal(false);
  protected readonly pendingOpen: WritableSignal<boolean> = signal(false);

  protected readonly DeadlineKind = DeadlineKind;

  protected readonly studentUrl = computed(() => `${window.location.origin}/my-assignments/${this.id()}`);
  protected readonly title = computed(() => this.assignment()?.title ?? '');
  protected readonly deadline = computed(() => this.assignment()?.deadline ?? null);
  protected readonly deadlineKind = computed(() => this.assignment()?.deadlineKind ?? DeadlineKind.None);

  protected readonly isHard = computed(() => this.deadlineKind() === DeadlineKind.Hard);
  protected readonly isSoft = computed(() => this.deadlineKind() !== DeadlineKind.Hard);
  protected readonly deadlinePassed = computed(() => {
    const deadline = this.deadline();

    return deadline !== null && this.clock.now().isAfter(deadline);
  });

  protected readonly acceptedRows = computed(() => this.rows().filter(row => row.accepted));
  protected readonly pendingRows = computed(() => this.rows().filter(row => !row.accepted));
  protected readonly nothingAccepted = computed(() => !this.loading() && this.acceptedRows().length === 0);
  protected readonly everyoneAccepted = computed(() => this.pendingRows().length === 0);

  protected readonly acceptedCount = computed(() => this.acceptedRows().length);
  protected readonly totalCount = computed(() => this.rows().length);
  protected readonly toReviewCount = computed(() => this.acceptedRows().filter(row => row.needsReview).length);
  protected readonly lateCount = computed(() => (this.isSoft()
    ? this.acceptedRows().filter(row => row.badge?.label === 'Submitted late').length
    : 0));

  /** The first few avatars of the people who have not accepted, for the collapsed group. */
  protected readonly pendingPreview = computed(() => this.pendingRows().slice(0, AVATAR_STACK));
  protected readonly pendingMore = computed(() => Math.max(0, this.pendingRows().length - AVATAR_STACK));

  public async ngOnInit(): Promise<void> {
    await this.loadAssignment();
    await this.reload();
  }

  protected async copyLink(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.studentUrl());
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      this.snackbar.show('Could not copy the link');
    }
  }

  /** No reminder endpoint exists, so "Remind all" hands the teacher the link to pass on. */
  protected async remindAll(): Promise<void> {
    await this.copyLink();
    this.snackbar.show('Student link copied — send it to the class');
  }

  protected togglePending(): void {
    this.pendingOpen.update(open => !open);
  }

  protected async handleEditClicked(): Promise<void> {
    await this.router.navigate(['assignments', this.id(), 'edit']);
  }

  protected async handleDownloadAllClicked(): Promise<void> {
    await this.router.navigate(['assignments', this.id(), 'download']);
  }

  protected async viewAnalytics(row: StudentRow): Promise<void> {
    if (row.acceptanceId === null) {
      return;
    }
    await this.router.navigate(['assignments', this.id(), 'acceptances', row.acceptanceId, 'analytics']);
  }

  protected async runAction(row: StudentRow): Promise<void> {
    if (row.action === null) {
      return;
    }
    if (row.action.kind === 'repository') {
      this.openRepository(row);

      return;
    }
    await this.openFeedbackPr(row);
  }

  protected openRepository(row: StudentRow): void {
    if (row.repoUrl !== null) {
      window.open(row.repoUrl, '_blank', 'noopener');
    }
  }

  private async openFeedbackPr(row: StudentRow): Promise<void> {
    if (row.acceptanceId === null) {
      return;
    }
    const result = await this.service.openFeedbackPr(row.acceptanceId);
    if (result === 'forbidden' || result === 'not-found' || result === null) {
      this.snackbar.show('Could not open the review pull request');

      return;
    }
    window.open(result.htmlUrl, '_blank', 'noopener');
    this.snackbar.show('Review pull request ready');
    await this.reload();
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

    const counts = new Map<number, number>();
    await Promise.all(submissions.map(async submission => {
      if (submission.status === SubmissionStatus.DownloadOnly) {
        return;
      }
      const analytics = await this.service.getAnalytics(submission.acceptanceId);
      if (analytics !== null && analytics !== 'forbidden') {
        counts.set(submission.acceptanceId, analytics.commitCount);
      }
    }));

    const accepted = submissions.map(submission => this.toRow(submission, counts));
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

  private toRow(submission: StudentSubmission, counts: Map<number, number>): StudentRow {
    const now = this.clock.now();

    return {
      acceptanceId: submission.acceptanceId,
      studentId: submission.studentId,
      name: `${submission.lastName} ${submission.firstName}`,
      studentNumber: submission.studentNumber,
      initials: this.initialsOf(submission.firstName, submission.lastName),
      commits: counts.get(submission.acceptanceId) ?? null,
      pushedLabel: submission.lastPushAt === null ? 'no push yet' : `pushed ${relativeDue(submission.lastPushAt, now)}`,
      pushedTitle: submission.lastPushAt === null ? '' : timestampLabel(submission.lastPushAt),
      badge: this.badgeOf(submission),
      action: this.actionOf(submission),
      repoUrl: submission.repoUrl,
      accepted: true,
      needsReview: submission.feedbackState === FeedbackState.None && submission.lastPushAt !== null
    };
  }

  private toUnacceptedRow(id: number, firstName: string, lastName: string): StudentRow {
    return {
      acceptanceId: null,
      studentId: id,
      name: `${lastName} ${firstName}`,
      studentNumber: null,
      initials: this.initialsOf(firstName, lastName),
      commits: null,
      pushedLabel: '',
      pushedTitle: '',
      badge: null,
      action: null,
      repoUrl: null,
      accepted: false,
      needsReview: false
    };
  }

  /** At most one badge per row, and none when there is nothing to say. */
  private badgeOf(submission: StudentSubmission): { label: string; tone: BadgeTone; icon: string | null } | null {
    const deadline = this.deadline();
    const pushed = submission.lastPushAt;
    const late = deadline !== null && pushed !== null && pushed.isAfter(deadline);

    // A hard deadline never labels anybody late: after it passes, nothing can be pushed at all.
    if (late && this.isSoft()) {
      return { label: 'Submitted late', tone: 'warning', icon: 'hourglass_bottom' };
    }
    if (submission.feedbackState !== FeedbackState.None) {
      return { label: 'Reviewed', tone: 'success', icon: null };
    }
    if (submission.status === SubmissionStatus.Failed) {
      return { label: 'Setup failed', tone: 'danger', icon: null };
    }
    if (submission.status === SubmissionStatus.Provisioning) {
      return { label: 'Setting up', tone: 'neutral', icon: null };
    }
    if (submission.status === SubmissionStatus.DownloadOnly) {
      return { label: 'Accepted', tone: 'neutral', icon: null };
    }
    if (pushed !== null) {
      return { label: 'Submitted', tone: 'accent', icon: null };
    }

    return null;
  }

  private actionOf(submission: StudentSubmission): { label: string; kind: 'feedback' | 'repository' } | null {
    if (submission.status === SubmissionStatus.DownloadOnly
        || submission.feedbackState !== FeedbackState.None) {
      return null;
    }
    if (submission.lastPushAt === null) {
      return { label: 'Review', kind: 'repository' };
    }

    return { label: 'Review', kind: 'feedback' };
  }

  private initialsOf(firstName: string, lastName: string): string {
    const initials = `${firstName.trim().charAt(0)}${lastName.trim().charAt(0)}`.toUpperCase();

    return initials === '' ? '?' : initials;
  }
}
