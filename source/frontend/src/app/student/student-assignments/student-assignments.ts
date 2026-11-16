import { Component, computed, inject, OnInit, signal, Signal, WritableSignal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatCard, MatCardContent } from '@angular/material/card';
import { MatProgressBar } from '@angular/material/progress-bar';
import { Instant } from '@js-joda/core';
import { AssignedAssignment, AssignmentService } from '../../../core/services/assignment-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

type AssignmentView = 'open' | 'expired';

interface AssignmentRow {
  id: number;
  title: string;
  deadline: Date | null;
  accepted: boolean;
}

interface CourseSection {
  id: number;
  title: string;
  assignments: AssignmentRow[];
}

@Component({
  selector: 'app-student-assignments',
  imports: [DatePipe, RouterLink, MatButton, MatCard, MatCardContent, MatProgressBar],
  templateUrl: './student-assignments.html',
  styleUrl: './student-assignments.scss'
})
export class StudentAssignments implements OnInit {
  private readonly service = inject(AssignmentService);
  private readonly snackbar = inject(SnackbarService);

  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly view: WritableSignal<AssignmentView> = signal('open');
  protected readonly accepting: WritableSignal<number | null> = signal(null);

  protected readonly all: WritableSignal<AssignedAssignment[]> = signal([]);

  protected readonly openCount: Signal<number> = computed(() => this.countFor('open'));
  protected readonly expiredCount: Signal<number> = computed(() => this.countFor('expired'));

  protected readonly sections: Signal<CourseSection[]> = computed(() => {
    const now = Instant.now();
    const wantedExpired = this.view() === 'expired';
    const courses = new Map<number, CourseSection>();

    for (const assignment of this.all()) {
      if (this.isExpired(assignment, now) !== wantedExpired) {
        continue;
      }

      const course = courses.get(assignment.courseId) ?? {
        id: assignment.courseId,
        title: assignment.courseTitle,
        assignments: []
      };
      course.assignments.push({
        id: assignment.id,
        title: assignment.title,
        deadline: assignment.deadline === null ? null : new Date(assignment.deadline.toEpochMilli()),
        accepted: assignment.accepted
      });
      courses.set(assignment.courseId, course);
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
    await this.load();
  }

  protected setView(view: AssignmentView): void {
    this.view.set(view);
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

  private countFor(view: AssignmentView): number {
    const now = Instant.now();

    return this.all().filter(assignment => this.isExpired(assignment, now) === (view === 'expired')).length;
  }

  private isExpired(assignment: AssignedAssignment, now: Instant): boolean {
    return assignment.deadline !== null && assignment.deadline.isBefore(now);
  }
}
