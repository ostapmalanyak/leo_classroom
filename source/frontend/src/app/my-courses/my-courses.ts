import { Component, computed, inject, OnInit, signal, WritableSignal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { Instant } from '@js-joda/core';
import { HttpErrorResponse } from '@angular/common/http';
import { catchError, of } from 'rxjs';
import { ZodError } from 'zod';
import { MatDialog } from '@angular/material/dialog';
import { Assignment, AssignmentService } from '../../core/services/assignment-service';
import { SessionService } from '../../core/auth/session-service';
import { CourseOverview, CourseService } from '../../core/services/course-service';
import { CourseManagementService } from '../../core/services/course-management-service';
import { SnackbarService } from '../../core/services/snackbar-service';
import { CreateCourseDialog } from './create-course-dialog/create-course-dialog';
import { DeleteCourseDialog } from './delete-course-dialog/delete-course-dialog';
import { SearchEntry, SearchPalette } from '../shared/search-palette/search-palette';

interface CourseTile {
  id: number;
  title: string;
  avatarUrl: string;
  open: number;
}

@Component({
  selector: 'app-my-courses',
  imports: [RouterLink, MatProgressBar, MatButtonModule, MatIconModule, MatMenuModule, SearchPalette],
  templateUrl: './my-courses.html',
  styleUrl: './my-courses.scss'
})
export class MyCourses implements OnInit {
  private readonly assignmentService = inject(AssignmentService);
  private readonly courseService = inject(CourseService);
  private readonly courseManagementService = inject(CourseManagementService);
  private readonly snackbar = inject(SnackbarService);
  private readonly session = inject(SessionService);
  private readonly dialog = inject(MatDialog);

  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly courses: WritableSignal<CourseTile[]> = signal([]);

  /** The assignments of the courses, so the palette can jump to one of them as well. */
  private readonly assignments: WritableSignal<SearchEntry[]> = signal([]);
  private assignmentsFailureNotified = false;

  /** Everything the search palette can find: the courses and the assignments of them. */
  protected readonly searchEntries = computed<SearchEntry[]>(() => [
    ...this.courses().map(course => ({
      icon: 'school',
      title: course.title,
      subtitle: course.open > 0 ? `${course.open} open assignments` : 'All caught up',
      route: ['/my-assignments/course', String(course.id)]
    })),
    ...this.assignments()
  ]);

  public async ngOnInit(): Promise<void> {
    await this.loadCourses();
  }

  protected openCreateDialog(): void {
    const dialogRef = this.dialog.open(CreateCourseDialog, {
      width: 'min(56rem, 92vw)',
      maxWidth: '92vw',
      maxHeight: '92vh',
      ariaLabelledBy: 'create-course-title',
      ariaModal: true,
      data: { titleId: 'create-course-title' }
    });
    dialogRef.afterClosed().subscribe(created => {
      if (created) {
        this.snackbar.show('Course created');
        void this.loadCourses();
      }
    });
  }

  protected canManageCourses(): boolean {
    return this.session.isTeacher();
  }

  protected courseRoute(course: CourseTile): readonly string[] {
    return ['/my-assignments/course', String(course.id)];
  }

  protected stopCardNavigation(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
  }

  protected openDeleteDialog(event: Event, course: CourseTile): void {
    this.stopCardNavigation(event);
    const dialogRef = this.dialog.open(DeleteCourseDialog, {
      width: '28rem',
      maxWidth: '92%',
      ariaLabelledBy: 'delete-course-title',
      ariaModal: true,
      autoFocus: true,
      data: { id: course.id, title: course.title }
    });

    dialogRef.afterClosed().subscribe(deleted => {
      if (deleted === true) {
        this.snackbar.show('Course deleted');
        void this.loadCourses();
      }
    });
  }

  private async loadCourses(): Promise<void> {
    this.assignmentsFailureNotified = false;
    this.loading.set(true);
    const courses = await this.courseService.getCourses();
    if (courses === null) {
      this.loading.set(false);
      this.snackbar.show('Could not load your courses');

      return;
    }

    const now = Instant.now();
    const tiles = courses.map(course => this.toTile(course, [], now));
    this.courses.set(tiles);
    this.loading.set(false);

    if (!this.session.isTeacher()) {
      const mine = await this.assignmentService.listMine();
      if (mine === null) {
        this.snackbar.show('Could not load your assignments');

        return;
      }

      const assignments = [...mine.accepted, ...mine.unaccepted];
      this.courses.update(current => current.map(tile => ({
        ...tile,
        open: assignments.filter(assignment => assignment.courseId === tile.id
          && (assignment.deadline === null || !assignment.deadline.isBefore(now))).length
      })));
      this.assignments.set(assignments.map(assignment => ({
        icon: 'assignment',
        title: assignment.title,
        subtitle: assignment.courseTitle,
        route: ['/my-assignments', String(assignment.id)]
      })));

      return;
    }

    for (const course of courses) {
      this.assignmentService.listForCourse$(course.id).pipe(
        catchError(error => {
          if (error instanceof HttpErrorResponse) {
            console.error(`GET /api/assignments/course/${course.id} failed with status ${error.status}.`, error);
          } else if (error instanceof ZodError) {
            console.error(`GET /api/assignments/course/${course.id} returned an invalid response.`, error);
          } else {
            throw error;
          }

          if (!this.assignmentsFailureNotified) {
            this.assignmentsFailureNotified = true;
            this.snackbar.show('Could not load course assignments');
          }

          return of([]);
        })
      ).subscribe(assignments => {
        this.courses.update(current => current.map(tile =>
          tile.id === course.id ? this.toTile(course, assignments, now) : tile));
        this.assignments.update(current => [
          ...current.filter(entry => entry.subtitle !== course.title),
          ...assignments.map(assignment => ({
            icon: 'assignment',
            title: assignment.title,
            subtitle: course.title,
            route: ['/my-assignments/course', String(course.id)]
          }))
        ]);
      });
    }
  }

  private toTile(course: CourseOverview, assignments: Assignment[], now: Instant): CourseTile {
    return {
      id: course.id,
      title: course.title,
      avatarUrl: this.courseManagementService.avatarUrl(course.iconSeed ?? `legacy-${course.id}`),
      open: assignments.filter(assignment => assignment.deadline === null || !assignment.deadline.isBefore(now)).length
    };
  }
}
