import { Component, computed, inject, OnInit, signal, Signal, WritableSignal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatCard, MatCardContent } from '@angular/material/card';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { forgejoOrigin } from '../../../core/auth/auth-config';
import { SessionService } from '../../../core/auth/session-service';
import { AssignedAssignment, AssignmentService, SubmissionStatus } from '../../../core/services/assignment-service';
import { NowService } from '../../../core/services/now-service';
import { SnackbarService } from '../../../core/services/snackbar-service';
import {
  DueUrgency, dueLabel, dueUrgency, dueWithinHours, relativeDue, StatePresentation, statePresentation,
  urgencyMarks
} from '../../../core/util/assignment-state';
import { countViews, workEntries, WorkEntry } from '../../../core/util/student-work';
import { CourseService } from '../../../core/services/course-service';
import { CourseIcon } from '../../shared/course-icon/course-icon';
import { GitCredentialsNotice } from '../../shared/git-credentials-notice/git-credentials-notice';

interface NextUpItem {
  assignment: AssignedAssignment;
  dueLabel: string;
  relative: string;
  urgency: DueUrgency;
  state: StatePresentation;
}

/** One tile of the overview row. The tone says how urgent the number is, nothing else. */
interface StatTile {
  label: string;
  count: number;
  query: Record<string, string> | null;
  tone: 'neutral' | 'urgent' | 'danger';
}

@Component({
  selector: 'app-student-dashboard',
  imports: [RouterLink, MatCard, MatCardContent, MatIcon, MatProgressBar, CourseIcon, GitCredentialsNotice],
  templateUrl: './student-dashboard.html',
  styleUrl: './student-dashboard.scss'
})
export class StudentDashboard implements OnInit {
  private readonly service = inject(AssignmentService);
  private readonly courseService = inject(CourseService);
  private readonly session = inject(SessionService);
  private readonly clock = inject(NowService);
  private readonly snackbar = inject(SnackbarService);

  protected readonly loading: WritableSignal<boolean> = signal(true);
  protected readonly accepting: WritableSignal<number | null> = signal(null);
  protected readonly forgejoLoginUrl = `${forgejoOrigin()}/user/login`;

  private readonly all: WritableSignal<AssignedAssignment[]> = signal([]);

  /** The icon seed of every course, so a course always shows the icon it really has. */
  protected readonly iconSeeds: WritableSignal<Map<number, string | null>> = signal(new Map());

  protected readonly firstName: Signal<string> = computed(() =>
    this.session.identity()?.displayName?.split(' ')[0] || 'there');

  private readonly entries: Signal<WorkEntry[]> = computed(() => workEntries(this.all(), this.clock.now()));

  private readonly counts = computed(() => countViews(this.entries()));

  /** Work that is due within the next day: the number that decides what to do first. */
  protected readonly dueSoonCount: Signal<number> = computed(() => {
    const now = this.clock.now();

    return this.entries().filter(entry => entry.view === 'todo'
      && entry.assignment.deadline !== null
      && dueWithinHours(entry.assignment.deadline, 24, now)).length;
  });

  protected readonly stats: Signal<StatTile[]> = computed(() => [
    { label: 'To do', count: this.counts().todo, query: { view: 'todo' }, tone: 'neutral' },
    { label: 'Pushed', count: this.counts().pushed, query: { view: 'pushed' }, tone: 'neutral' },
    { label: 'Due in 24 h', count: this.dueSoonCount(), query: null, tone: 'urgent' },
    { label: 'Past due', count: this.counts().pastDue, query: { view: 'past-due' }, tone: 'danger' }
  ]);

  /** The three soonest deadlines: on a landing page, the next three are the whole point. */
  protected readonly nextUp: Signal<NextUpItem[]> = computed(() => {
    const now = this.clock.now();

    return this.entries()
      .filter(entry => entry.view === 'todo' && entry.assignment.deadline !== null)
      .sort((left, right) => left.assignment.deadline!.compareTo(right.assignment.deadline!))
      .slice(0, 3)
      .map(entry => ({
        assignment: entry.assignment,
        dueLabel: dueLabel(entry.assignment.deadline),
        relative: relativeDue(entry.assignment.deadline, now)
          + urgencyMarks(dueUrgency(entry.assignment.deadline, now)),
        urgency: dueUrgency(entry.assignment.deadline, now),
        state: statePresentation(entry.state)
      }));
  });

  public async ngOnInit(): Promise<void> {
    await this.load();
    this.iconSeeds.set(await this.courseService.getCourseIconSeeds());
  }

  /** The seed of a course's icon, so a course always shows the icon it really has. */
  protected iconSeed(courseId: number): string | null {
    return this.iconSeeds().get(courseId) ?? null;
  }

  private async load(): Promise<void> {
    const result = await this.service.listMine();
    this.loading.set(false);
    if (result !== null) {
      this.all.set([...result.accepted, ...result.unaccepted]);
    }
  }

  /** Home offers the one action that makes sense before the deadline: accept the next one. */
  protected async accept(id: number): Promise<void> {
    this.accepting.set(id);
    const result = await this.service.accept(id);
    this.accepting.set(null);

    if (result === 'conflict') {
      await this.load();

      return;
    }
    if (typeof result === 'string' || result === null) {
      this.snackbar.show('Could not accept the assignment');

      return;
    }

    this.all.update(assignments => assignments.map(assignment =>
      assignment.id === id
        ? { ...assignment, accepted: true, status: assignment.status ?? SubmissionStatus.Provisioning }
        : assignment));
    this.snackbar.show('Assignment accepted');
  }
}
