import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { Instant } from '@js-joda/core';
import { firstValueFrom } from 'rxjs';
import { AssignmentService } from '../../../core/services/assignment-service';
import { CourseService } from '../../../core/services/course-service';
import { SessionService } from '../../../core/auth/session-service';
import { relativeDue, dueUrgency, DueUrgency, isHiddenNow } from '../../../core/util/assignment-state';

/** An assignment that closes soon. */
export interface ClosingAssignment {
  id: number;
  title: string;
  courseTitle: string;
  deadline: Instant;
  route: readonly string[];
}

/**
 * The bell in the bar: it counts the assignments that close within the next day and lists
 * the next ones, so a deadline never passes unnoticed.
 */
@Component({
  selector: 'app-notification-bell',
  imports: [MatIcon, MatMenu, MatMenuItem, MatMenuTrigger, RouterLink],
  templateUrl: './notification-bell.html',
  styleUrl: './notification-bell.scss'
})
export class NotificationBell implements OnInit {
  private readonly assignmentService = inject(AssignmentService);
  private readonly courseService = inject(CourseService);
  private readonly session = inject(SessionService);

  protected readonly closing = signal<ClosingAssignment[]>([]);
  protected readonly now = signal(Instant.now());

  /** The assignments that close within the next day. */
  protected readonly dueSoon = computed(() =>
    this.closing().filter(item => this.minutesLeft(item.deadline) <= 24 * 60));

  /** The next five of them, the nearest first. */
  protected readonly listed = computed(() => this.closing().slice(0, 5));

  public async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected readonly count = computed(() => this.dueSoon().length);

  /** The label of the button says how many are waiting. */
  protected ariaLabel(): string {
    const count = this.count();

    return count === 0 ? 'Notifications' : `Notifications, ${count} new`;
  }

  /** How the deadline is coloured: quiet, yellow or red. */
  protected urgency(item: ClosingAssignment): DueUrgency {
    return dueUrgency(item.deadline, this.now());
  }

  /** The short time that is left, with the marks of the student pages. */
  protected remaining(item: ClosingAssignment): string {
    return relativeDue(item.deadline, this.now());
  }

  /** The same time, spelled out for a screen reader. */
  protected closingLabel(item: ClosingAssignment): string {
    const minutes = this.minutesLeft(item.deadline);

    return minutes < 60
      ? `Closes in ${Math.max(1, minutes)} minute${minutes === 1 ? '' : 's'}`
      : `Closes in ${Math.floor(minutes / 60)} hour${Math.floor(minutes / 60) === 1 ? '' : 's'}`;
  }

  private minutesLeft(deadline: Instant): number {
    return Math.floor((deadline.toEpochMilli() - this.now().toEpochMilli()) / 60000);
  }

  private async load(): Promise<void> {
    const items: ClosingAssignment[] = [];

    if (this.session.isTeacher()) {
      const courses = await this.courseService.getCourses();
      for (const course of courses ?? []) {
        const assignments = await firstValueFrom(this.assignmentService.listForCourse$(course.id));
        for (const assignment of assignments.filter(a => !isHiddenNow(a, Instant.now()))) {
          if (assignment.deadline === null) {
            continue;
          }

          items.push({
            id: assignment.id,
            title: assignment.title,
            courseTitle: course.title,
            deadline: assignment.deadline,
            route: ['/assignments', String(assignment.id), 'students']
          });
        }
      }
    } else {
      const mine = await this.assignmentService.listMine();
      for (const assignment of mine?.unaccepted ?? []) {
        if (assignment.deadline === null) {
          continue;
        }

        items.push({
          id: assignment.id,
          title: assignment.title,
          courseTitle: assignment.courseTitle,
          deadline: assignment.deadline,
          route: ['/my-assignments', String(assignment.id)]
        });
      }
    }

    // A deadline that has passed is history: the bell is about what is still coming.
    const now = Instant.now();
    const upcoming = items.filter(item => item.deadline.isAfter(now));

    // The nearest deadline first: that is the order a teacher and a student work in.
    upcoming.sort((left, right) => left.deadline.compareTo(right.deadline));
    this.closing.set(upcoming);
  }
}
