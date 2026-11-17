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
  DueUrgency, dueLabel, dueUrgency, relativeDue, StatePresentation, statePresentation
} from '../../../core/util/assignment-state';
import { countViews, workEntries, WorkEntry } from '../../../core/util/student-work';
import { CourseService } from '../../../core/services/course-service';
import { CourseIcon } from '../../shared/course-icon/course-icon';

interface NextUpItem {
  assignment: AssignedAssignment;
  dueLabel: string;
  relative: string;
  urgency: DueUrgency;
  state: StatePresentation;
}

interface StatRow {
  label: string;
  count: number;
  view: 'todo' | 'pushed' | 'past-due';
  danger: boolean;
}

@Component({
  selector: 'app-student-dashboard',
  imports: [RouterLink, MatCard, MatCardContent, MatIcon, MatProgressBar, CourseIcon],
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

  protected readonly pastDueCount = computed(() => this.counts().pastDue);

  protected readonly stats: Signal<StatRow[]> = computed(() => [
    { label: 'To do', count: this.counts().todo, view: 'todo', danger: false },
    { label: 'Pushed', count: this.counts().pushed, view: 'pushed', danger: false },
    { label: 'Past due', count: this.counts().pastDue, view: 'past-due', danger: this.counts().pastDue > 0 }
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
        relative: relativeDue(entry.assignment.deadline, now),
        urgency: dueUrgency(entry.assignment.deadline, now),
        state: statePresentation(entry.state)
      }));
  });

  /** Missed work is an alert, not a grey number in a table. */
  protected readonly pastDueNames: Signal<string> = computed(() =>
    this.entries()
      .filter(entry => entry.view === 'past-due')
      .map(entry => entry.assignment.title)
      .join(', '));

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
