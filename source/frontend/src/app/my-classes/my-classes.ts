import { Component, inject, OnInit, signal, WritableSignal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatProgressBar } from '@angular/material/progress-bar';
import { Instant } from '@js-joda/core';
import { HttpErrorResponse } from '@angular/common/http';
import { catchError, of } from 'rxjs';
import { ZodError } from 'zod';
import { MatDialog } from '@angular/material/dialog';
import { Assignment, AssignmentService } from '../../core/services/assignment-service';
import { SessionService } from '../../core/auth/session-service';
import { CourseTeacherService } from '../../core/services/course-teacher-service';
import { CourseOverview, CourseService } from '../../core/services/course-service';
import { SnackbarService } from '../../core/services/snackbar-service';
import { classAvatarColor, classInitials } from '../../core/util/class-avatar';
import { CreateClassDialog } from './create-class-dialog/create-class-dialog';

interface ClassTile {
  id: number;
  title: string;
  initials: string;
  color: string;
  open: number;
}

@Component({
  selector: 'app-my-classes',
  imports: [RouterLink, MatProgressBar],
  templateUrl: './my-classes.html',
  styleUrl: './my-classes.scss'
})
export class MyClasses implements OnInit {
  private readonly assignmentService = inject(AssignmentService);
  private readonly courseTeacherService = inject(CourseTeacherService);
  private readonly courseService = inject(CourseService);
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
      width: '60rem',
      maxWidth: '90%',
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
      initials: classInitials(course.title),
      color: classAvatarColor(course.title),
      open: assignments.filter(assignment => assignment.deadline === null || !assignment.deadline.isBefore(now)).length
    };
  }
}
