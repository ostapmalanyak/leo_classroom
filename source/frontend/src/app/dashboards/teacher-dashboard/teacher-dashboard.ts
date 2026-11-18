import { Component, computed, inject, OnInit, signal, Signal, WritableSignal } from '@angular/core';
import { MatCard, MatCardContent } from '@angular/material/card';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { ChronoUnit, Instant } from '@js-joda/core';
import { firstValueFrom } from 'rxjs';
import { forgejoOrigin } from '../../../core/auth/auth-config';
import { Role } from '../../../core/auth/roles';
import { SessionService } from '../../../core/auth/session-service';
import { Assignment, AssignmentService, FeedbackState } from '../../../core/services/assignment-service';
import { CourseService } from '../../../core/services/course-service';
import { NowService } from '../../../core/services/now-service';
import { isHiddenNow, relativeDue } from '../../../core/util/assignment-state';
import { MatDialog } from '@angular/material/dialog';
import { ActionCard } from './action-card/action-card';
import { ChooseCourseDialog } from './choose-course-dialog/choose-course-dialog';
import { MyRostersDialog } from './my-rosters-dialog/my-rosters-dialog';
import { ClosingAssignment, ClosingSoonList } from './closing-soon/closing-soon';
import { OverviewTile } from './overview-tile/overview-tile';

interface TeacherCourse {
  id: number;
  title: string;
  /** The seed of the icon the backend draws for this course. */
  iconSeed: string | null;
}

/** How many past due assignments the page shows at once. */
const PAST_DUE_BATCH = 5;

/** An assignment whose deadline has passed. */
interface TeacherPastDue {
  id: number;
  title: string;
  courseTitle: string;
  courseId: number;
  deadline: Instant;
}

/**
 * The home of a teacher. It answers three questions in this order: what can I do, how
 * much work is open, and what closes next.
 */
@Component({
  selector: 'app-teacher-dashboard',
  imports: [MatCard, MatCardContent, MatIcon, MatProgressBar, ActionCard, ClosingSoonList, OverviewTile, RouterLink],
  templateUrl: './teacher-dashboard.html',
  styleUrl: './teacher-dashboard.scss'
})
export class TeacherDashboard implements OnInit {
  private readonly assignmentService = inject(AssignmentService);
  private readonly courseService = inject(CourseService);
  private readonly session = inject(SessionService);
  private readonly clock = inject(NowService);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);

  protected readonly loading: WritableSignal<boolean> = signal(true);
  protected readonly pastDue: WritableSignal<TeacherPastDue[]> = signal([]);
  private readonly pastDueLimit: WritableSignal<number> = signal(PAST_DUE_BATCH);
  private readonly pastDueCounts: WritableSignal<Map<number, { accepted: number; total: number }>> =
    signal(new Map());
  private readonly courseTotals: Map<number, number> = new Map();
  protected readonly courses: WritableSignal<TeacherCourse[]> = signal([]);
  protected readonly closing: WritableSignal<ClosingAssignment[]> = signal([]);
  protected readonly activeCount: WritableSignal<number> = signal(0);
  protected readonly closingIn24h: WritableSignal<number> = signal(0);

  protected readonly firstName = computed(() =>
    this.session.identity()?.displayName?.split(' ')[0] || 'there');

  protected readonly forgejoUrl = forgejoOrigin();

  /**
   * The assignments that closed in the last week and still hold work nobody has reviewed.
   * Reviewing clears the flag, so a closed assignment leaves this count by itself.
   */
  private readonly toReview = signal(0);
  protected readonly recentlyMissed: Signal<number> = this.toReview.asReadonly();

  /** The past due assignments on the page; the rest waits behind the button. */
  protected readonly visiblePastDue = computed(() => this.pastDue().slice(0, this.pastDueLimit()));

  /** How many of the past due assignments have taken it, for the ones on the page. */
  protected pastDueCount(id: number): { accepted: number; total: number } | null {
    return this.pastDueCounts().get(id) ?? null;
  }

  /** How long ago the deadline passed. */
  protected relative(deadline: Instant): string {
    return relativeDue(deadline, this.clock.now());
  }

  /** Shows the next batch of past due assignments. */
  protected showMorePastDue(): void {
    this.pastDueLimit.update(limit => limit + 5);
    void this.loadPastDueCounts();
  }

  /** Puts the last batch away again. */
  protected showLessPastDue(): void {
    this.pastDueLimit.update(limit => Math.max(PAST_DUE_BATCH, limit - PAST_DUE_BATCH));
  }

  public async ngOnInit(): Promise<void> {
    await this.load();
  }

  /** The teacher wants to see the rosters they created: they open in a popup. */
  protected openMyRosters(): void {
    this.dialog.open(MyRostersDialog, {
      width: '30rem',
      maxWidth: '92%',
      ariaLabelledBy: 'my-rosters-title',
      ariaModal: true,
      autoFocus: true
    });
  }

  /** The teacher wants a new assignment: first the course it belongs to. */
  protected openChooseCourse(): void {
    const dialogRef = this.dialog.open(ChooseCourseDialog, {
      width: '28rem',
      maxWidth: '92%',
      ariaLabelledBy: 'choose-course-title',
      ariaModal: true,
      autoFocus: true,
      data: {
        courses: this.courses().map(course => ({
          id: course.id,
          title: course.title,
          iconSeed: course.iconSeed
        }))
      }
    });

    dialogRef.afterClosed().subscribe((result: number | undefined) => {
      if (typeof result === 'number') {
        void this.router.navigate(['/courses', String(result), 'assignments', 'new']);
      }
    });
  }


  private async load(): Promise<void> {
    const courses = await this.courseService.getCourses();
    if (courses === null) {
      this.loading.set(false);

      return;
    }

    this.courses.set(courses.map(course => ({
      id: course.id,
      title: course.title,
      iconSeed: course.iconSeed ?? null
    })));

    const now = this.clock.now();
    const open: { assignment: Assignment; courseTitle: string }[] = [];
    const missed: TeacherPastDue[] = [];

    for (const course of courses) {
      const assignments = await firstValueFrom(this.assignmentService.listForCourse$(course.id));
      // hidden assignments are still being prepared, so they are neither open nor missed
      for (const assignment of assignments.filter(a => !isHiddenNow(a, now))) {
        if (assignment.deadline !== null && assignment.deadline.isBefore(now)) {
          missed.push({
            id: assignment.id,
            title: assignment.title,
            courseTitle: course.title,
            courseId: course.id,
            deadline: assignment.deadline
          });
          continue;
        }

        open.push({ assignment, courseTitle: course.title });
      }
    }

    // The most recent deadline first: those are the ones a teacher can still talk about.
    missed.sort((left, right) => right.deadline.compareTo(left.deadline));
    this.pastDue.set(missed);

    void this.loadToReview(missed);

    this.activeCount.set(open.length);
    this.closingIn24h.set(open.filter(entry => entry.assignment.deadline !== null
      && now.until(entry.assignment.deadline, ChronoUnit.MINUTES) <= 24 * 60).length);

    const nearest = open
      .filter(entry => entry.assignment.deadline !== null)
      .sort((left, right) => left.assignment.deadline!.compareTo(right.assignment.deadline!))
      .slice(0, 4);

    const rows: ClosingAssignment[] = [];
    for (const entry of nearest) {
      rows.push({
        id: entry.assignment.id,
        title: entry.assignment.title,
        courseTitle: entry.courseTitle,
        accepted: await this.acceptedCount(entry.assignment.id),
        total: await this.studentCount(entry.assignment.courseId),
        deadline: entry.assignment.deadline!
      });
    }

    this.closing.set(rows);
    this.loading.set(false);

    // The numbers of the past due assignments are a nice to have: a request that fails
    // must not take the page down with it.
    void this.loadPastDueCounts();
  }

  /** Fills the numbers of the past due assignments that are on the page. */
  /** An assignment needs review while at least one student has pushed and nothing was reviewed yet. */
  private async loadToReview(closed: TeacherPastDue[]): Promise<void> {
    const week = this.clock.now().minus(7, ChronoUnit.DAYS);
    const recent = closed.filter(entry => entry.deadline.isAfter(week));

    const waiting = await Promise.all(recent.map(async entry => {
      const submissions = await this.assignmentService.getStudents(entry.id);
      if (submissions === null) {
        return false;
      }

      return submissions.some(submission =>
        submission.feedbackState === FeedbackState.None && submission.lastPushAt !== null);
    }));

    this.toReview.set(waiting.filter(Boolean).length);
  }

  private async loadPastDueCounts(): Promise<void> {
    const counts = new Map(this.pastDueCounts());
    for (const entry of this.visiblePastDue()) {
      if (counts.has(entry.id)) {
        continue;
      }

      try {
        counts.set(entry.id, {
          accepted: await this.acceptedCount(entry.id),
          total: await this.studentCount(entry.courseId)
        });
      } catch {
        // The row keeps its title and its deadline; the counts stay empty.
        continue;
      }

      this.pastDueCounts.set(new Map(counts));
    }
  }

  /** How many students have taken the assignment. */
  private async acceptedCount(assignmentId: number): Promise<number> {
    const students = await this.assignmentService.getStudents(assignmentId);

    return students?.length ?? 0;
  }

  /** How many students the course has, so the teacher sees "12 of 25 accepted". */
  private async studentCount(courseId: number): Promise<number> {
    const cached = this.courseTotals.get(courseId);
    if (cached !== undefined) {
      return cached;
    }

    const participants = await this.courseService.getParticipants(courseId);
    if (participants === null) {
      return 0;
    }

    const total = participants.participants.filter(participant => participant.role === Role.Student).length;
    this.courseTotals.set(courseId, total);

    return total;
  }
}
