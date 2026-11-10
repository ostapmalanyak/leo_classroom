import { Component, inject, OnInit, signal, WritableSignal } from '@angular/core';
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
import { CourseTeacherService } from '../../core/services/course-teacher-service';
import { CourseOverview, CourseService } from '../../core/services/course-service';
import { ClassService } from '../../core/services/class-service';
import { SnackbarService } from '../../core/services/snackbar-service';
import { CreateClassDialog } from './create-class-dialog/create-class-dialog';
import { DeleteCourseDialog } from './delete-course-dialog/delete-course-dialog';

interface ClassTile {
  id: number;
  title: string;
  avatarUrl: string;
  open: number;
}

@Component({
  selector: 'app-my-classes',
  imports: [RouterLink, MatProgressBar, MatButtonModule, MatIconModule, MatMenuModule],
  templateUrl: './my-classes.html',
  styleUrl: './my-classes.scss'
})
export class MyClasses implements OnInit {
  private readonly assignmentService = inject(AssignmentService);
  private readonly courseTeacherService = inject(CourseTeacherService);
  private readonly courseService = inject(CourseService);
  private readonly classService = inject(ClassService);
  private readonly snackbar = inject(SnackbarService);
  private readonly session = inject(SessionService);
  private readonly dialog = inject(MatDialog);

  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly classes: WritableSignal<ClassTile[]> = signal([]);
  private assignmentsFailureNotified = false;

  public async ngOnInit(): Promise<void> {
    await this.loadClasses();
  }

  protected openCreateDialog(): void {
    const dialogRef = this.dialog.open(CreateClassDialog, {
      width: 'min(56rem, 92vw)',
      maxWidth: '92vw',
      maxHeight: '92vh',
      ariaLabelledBy: 'create-class-title',
      ariaModal: true,
      data: { titleId: 'create-class-title' }
    });
    dialogRef.afterClosed().subscribe(created => {
      if (created) {
        this.snackbar.show('Class created');
        void this.loadClasses();
      }
    });
  }

  protected canManageCourses(): boolean {
    return this.session.isTeacher();
  }

  protected stopCardNavigation(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
  }

  protected openDeleteDialog(event: Event, course: ClassTile): void {
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
        void this.loadClasses();
      }
    });
  }

  private async loadClasses(): Promise<void> {
    this.assignmentsFailureNotified = false;
    this.loading.set(true);
    const courses = await this.courseService.getCourses();
    if (courses === null) {
      this.loading.set(false);
      this.snackbar.show('Could not load your classes');

      return;
    }

    const teacherId = this.session.identity()?.studentId;
    if (teacherId === undefined) {
      this.loading.set(false);
      this.snackbar.show('Could not identify your teacher account');

      return;
    }

    const teacherLists = await Promise.all(courses.map(course => this.courseTeacherService.getTeachers(course.id)));
    if (teacherLists.some(teachers => teachers === null)) {
      this.loading.set(false);
      this.snackbar.show('Could not load class teachers');

      return;
    }

    const myCourses = this.session.isAdmin()
      ? courses
      : courses.filter((course, index) => {
        const teachers = teacherLists[index];

        return teachers !== null
          && teachers !== undefined
          && (teachers.owner.studentId === teacherId
            || teachers.coTeachers.some(teacher => teacher.studentId === teacherId));
      });
    const now = Instant.now();
    const tiles = myCourses.map(course => this.toTile(course, [], now));
    this.classes.set(tiles);
    this.loading.set(false);

    for (const course of myCourses) {
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
            this.snackbar.show('Could not load class assignments');
          }

          return of([]);
        })
      ).subscribe(assignments => {
        this.classes.update(current => current.map(tile =>
          tile.id === course.id ? this.toTile(course, assignments, now) : tile));
      });
    }
  }

  private toTile(course: CourseOverview, assignments: Assignment[], now: Instant): ClassTile {
    return {
      id: course.id,
      title: course.title,
      avatarUrl: this.classService.avatarUrl(course.iconSeed ?? `legacy-${course.id}`),
      open: assignments.filter(assignment => assignment.deadline === null || !assignment.deadline.isBefore(now)).length
    };
  }
}
