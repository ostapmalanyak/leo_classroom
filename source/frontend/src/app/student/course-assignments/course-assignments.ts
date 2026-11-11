import { Component, computed, inject, input, OnInit, signal, WritableSignal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatCard, MatCardContent } from '@angular/material/card';
import { MatIcon } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { Instant } from '@js-joda/core';
import { CourseDetail } from '../../course-detail/course-detail';
import { AssignmentService, SubmissionStatus } from '../../../core/services/assignment-service';
import { CourseParticipant, CourseService } from '../../../core/services/course-service';
import { MeService } from '../../../core/services/me-service';
import { SessionService } from '../../../core/auth/session-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

interface CourseAssignment {
  id: number;
  title: string;
  deadline: Instant | null;
  accepted: boolean | null;
  status: SubmissionStatus | null;
}

type AssignmentSort = 'date-ascending' | 'date-descending' | 'title';

@Component({
  selector: 'app-course-assignments',
  imports: [
    DatePipe, RouterLink, MatButton, MatCard, MatCardContent, MatIcon, MatListModule, MatMenuModule, MatProgressBar,
    MatTabsModule, CourseDetail
  ],
  templateUrl: './course-assignments.html',
  styleUrls: ['./course-assignments.scss', '../../my-classes/my-classes.scss']
})
export class CourseAssignments implements OnInit {
  private readonly assignmentService = inject(AssignmentService);
  private readonly courseService = inject(CourseService);
  private readonly me = inject(MeService);
  protected readonly session = inject(SessionService);
  private readonly snackbar = inject(SnackbarService);

  /** Bound from the route parameter (withComponentInputBinding). */
  public readonly courseId = input.required<string>();

  protected readonly title: WritableSignal<string> = signal('Class');
  protected readonly assignments: WritableSignal<CourseAssignment[]> = signal([]);
  protected readonly participants: WritableSignal<CourseParticipant[]> = signal([]);
  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly selectedTab = signal(0);
  protected readonly sortMode = signal<AssignmentSort>('date-ascending');
  protected readonly sortLabel = computed(() => ({
    'date-ascending': 'Date ascending',
    'date-descending': 'Date descending',
    title: 'Title'
  })[this.sortMode()]);
  protected readonly sortedAssignments = computed(() => {
    const mode = this.sortMode();

    return [...this.assignments()].sort((left, right) => {
      if (mode === 'title') {
        return left.title.localeCompare(right.title);
      }
      if (left.deadline === null && right.deadline === null) {
        return left.title.localeCompare(right.title);
      }
      if (left.deadline === null) {
        return 1;
      }
      if (right.deadline === null) {
        return -1;
      }

      const byDate = left.deadline.compareTo(right.deadline);

      return mode === 'date-descending' ? -byDate : byDate;
    });
  });

  protected get classesRoute(): string {
    return this.session.isTeacher() ? '/my-classes' : '/my-assignments';
  }

  protected formatDeadline(deadline: Instant): Date {
    return new Date(deadline.toEpochMilli());
  }

  public async ngOnInit(): Promise<void> {
    this.loading.set(true);
    try {
      await this.me.load();

      const course = await this.courseService.getParticipants(Number(this.courseId()));
      if (course === null) {
        this.snackbar.show('Could not load course participants');

        return;
      }

      this.title.set(course.title);
      this.participants.set(course.participants);

      if (this.session.isTeacher()) {
        const assignments = await this.assignmentService.listForCourse(Number(this.courseId()));
        if (assignments === null) {
          this.snackbar.show('Could not load assignments');

          return;
        }
        this.assignments.set(assignments.map(assignment => ({
          id: assignment.id,
          title: assignment.title,
          deadline: assignment.deadline,
          accepted: null,
          status: null
        })));

        return;
      }

      const mine = await this.assignmentService.listMine();
      if (mine === null) {
        this.snackbar.show('Could not load your assignments');

        return;
      }
      const courseId = Number(this.courseId());
      const assignments = [...mine.accepted, ...mine.unaccepted]
        .filter(assignment => assignment.courseId === courseId)
        .map(assignment => ({
          id: assignment.id,
          title: assignment.title,
          deadline: assignment.deadline,
          accepted: assignment.accepted,
          status: assignment.status
        }));
      this.assignments.set(assignments);
    } finally {
      this.loading.set(false);
    }
  }

  protected async removeParticipant(participant: CourseParticipant): Promise<void> {
    const removed = await this.courseService.removeParticipant(Number(this.courseId()), participant.id);
    if (!removed) {
      this.snackbar.show('Could not remove course participant');

      return;
    }

    this.participants.update(participants => participants.filter(member => member.id !== participant.id));
    this.snackbar.show('Participant removed from this course');
  }

}
