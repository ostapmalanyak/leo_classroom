import { Component, computed, inject, OnInit, signal, Signal, WritableSignal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatCard, MatCardContent } from '@angular/material/card';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuTrigger } from '@angular/material/menu';
import { MatProgressBar } from '@angular/material/progress-bar';
import { Instant } from '@js-joda/core';
import { AssignedAssignment, AssignmentService } from '../../../core/services/assignment-service';
import { CourseService } from '../../../core/services/course-service';
import { SnackbarService } from '../../../core/services/snackbar-service';
import {
  AssignmentView,
  assignmentViewOf,
  relativeDue,
  studentAssignmentState,
  timestampLabel
} from '../../../core/util/assignment-state';
import { CourseIcon } from '../../shared/course-icon/course-icon';

interface AssignmentRow {
  id: number;
  title: string;
  courseId: number;
  courseTitle: string;
  deadline: Date | null;
  accepted: boolean;
  view: AssignmentView;
  /** Relative due text, shown inside the card: "Due in 3 days", "Overdue · 5 days ago". */
  due: string;
  /** Push timestamp for work that is already in the student's repository. */
  pushed: string;
}

interface CourseSection {
  id: number;
  title: string;
  assignments: AssignmentRow[];
}

@Component({
  selector: 'app-student-assignments',
  imports: [RouterLink, MatButton, MatCard, MatCardContent, MatIcon, MatMenu, MatMenuTrigger, MatProgressBar, CourseIcon],
  templateUrl: './student-assignments.html',
  styleUrl: './student-assignments.scss'
})
export class StudentAssignments implements OnInit {
  private readonly service = inject(AssignmentService);
  private readonly courseService = inject(CourseService);
  private readonly snackbar = inject(SnackbarService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly view: WritableSignal<AssignmentView> = signal('todo');
  protected readonly accepting: WritableSignal<number | null> = signal(null);

  protected readonly all: WritableSignal<AssignedAssignment[]> = signal([]);

  /** The icon seed of every course, so a course always shows the icon it really has. */
  protected readonly iconSeeds: WritableSignal<Map<number, string | null>> = signal(new Map());

  /** Every assignment with the state it is in, derived in one place so the tabs and the list agree. */
  protected readonly rows: Signal<AssignmentRow[]> = computed(() => {
    const now = Instant.now();

    return this.all().map(assignment => {
      const view = assignmentViewOf(studentAssignmentState({
        accepted: assignment.accepted,
        status: assignment.status,
        lastPushAt: assignment.lastPushAt,
        late: assignment.late,
        deadline: assignment.deadline
      }, now));

      return {
        id: assignment.id,
        title: assignment.title,
        courseId: assignment.courseId,
        courseTitle: assignment.courseTitle,
        deadline: assignment.deadline === null ? null : new Date(assignment.deadline.toEpochMilli()),
        accepted: assignment.accepted,
        view,
        due: this.dueText(assignment, now),
        pushed: view === 'pushed' && assignment.lastPushAt !== null
          ? `Pushed ${timestampLabel(assignment.lastPushAt)}`
          : ''
      };
    });
  });

  protected readonly todoCount: Signal<number> = computed(() => this.countFor('todo'));
  protected readonly pushedCount: Signal<number> = computed(() => this.countFor('pushed'));
  protected readonly pastDueCount: Signal<number> = computed(() => this.countFor('past-due'));

  /** Which course the list is narrowed to; null means every course. */
  protected readonly courseFilter: WritableSignal<number | null> = signal(null);

  /** The course the chip shows, so the chip always names the filter that is active. */
  protected readonly selectedCourse: Signal<{ id: number; title: string } | null> = computed(() => {
    const filter = this.courseFilter();

    return filter === null ? null : this.courses().find(course => course.id === filter) ?? null;
  });

  /** The courses the student actually has work in, so the filter never offers an empty list. */
  protected readonly courses: Signal<{ id: number; title: string }[]> = computed(() => {
    const courses = new Map<number, string>();

    for (const row of this.rows()) {
      courses.set(row.courseId, row.courseTitle);
    }

    return [...courses.entries()]
      .map(([id, title]) => ({ id, title }))
      .sort((left, right) => left.title.localeCompare(right.title));
  });

  protected readonly sections: Signal<CourseSection[]> = computed(() => {
    const courses = new Map<number, CourseSection>();
    const filter = this.courseFilter();

    for (const row of this.rows()) {
      if (row.view !== this.view()) {
        continue;
      }
      if (filter !== null && row.courseId !== filter) {
        continue;
      }

      const course = courses.get(row.courseId) ?? {
        id: row.courseId,
        title: row.courseTitle,
        assignments: []
      };
      course.assignments.push(row);
      courses.set(row.courseId, course);
    }

    return [...courses.values()]
      .sort((left, right) => left.title.localeCompare(right.title))
      .map(course => ({
        ...course,
        assignments: course.assignments.sort((left, right) => {
          if (left.deadline === null && right.deadline === null) {
            return left.title.localeCompare(right.title);
          }
          if (left.deadline === null) {
            return 1;
          }
          if (right.deadline === null) {
            return -1;
          }

          return left.deadline.getTime() - right.deadline.getTime();
        })
      }));
  });

  public async ngOnInit(): Promise<void> {
    // The course page links here with ?course=<id>, so the list can open narrowed to one course.
    const requested = this.route.snapshot.queryParamMap.get('course');
    if (requested !== null && /^\d+$/.test(requested)) {
      this.courseFilter.set(Number(requested));
    }

    await this.load();
    this.iconSeeds.set(await this.courseService.getCourseIconSeeds());
  }

  protected setView(view: AssignmentView): void {
    this.view.set(view);
  }

  /** Clicking the active course again clears the filter, so the row always has a way out. */
  protected setCourse(courseId: number | null): void {
    this.courseFilter.set(this.courseFilter() === courseId ? null : courseId);
    void this.syncCourseParam();
  }

  /** The seed of a course's icon, so a course always shows the icon it really has. */
  protected iconSeed(courseId: number): string | null {
    return this.iconSeeds().get(courseId) ?? null;
  }

  /** The filter lives in the URL, so the view can be linked to and survives a reload. */
  private async syncCourseParam(): Promise<void> {
    await this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { course: this.courseFilter() },
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
  }

  protected async accept(row: AssignmentRow): Promise<void> {
    this.accepting.set(row.id);
    const result = await this.service.accept(row.id);
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
      assignment.id === row.id ? { ...assignment, accepted: true } : assignment));
    this.snackbar.show('Assignment accepted');
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    const result = await this.service.listMine();
    this.loading.set(false);
    if (result === null) {
      this.snackbar.show('Could not load your assignments');

      return;
    }
    this.all.set([...result.accepted, ...result.unaccepted]);
  }

  /** The counts follow the course filter, so the tabs always describe the list on screen. */
  private countFor(view: AssignmentView): number {
    const filter = this.courseFilter();

    return this.rows().filter(row => row.view === view
      && (filter === null || row.courseId === filter)).length;
  }

  /** The deadline is relative, so a card says "Due in 3 days" instead of a bare date. */
  private dueText(assignment: AssignedAssignment, now: Instant): string {
    if (assignment.deadline === null) {
      return 'No due date';
    }

    const relative = relativeDue(assignment.deadline, now);

    return assignment.deadline.isBefore(now) ? `Overdue · ${relative}` : `Due ${relative}`;
  }
}
