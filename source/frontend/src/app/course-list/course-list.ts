import { Component, inject, OnInit, signal, WritableSignal } from '@angular/core';
import { Router } from '@angular/router';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatIcon } from '@angular/material/icon';
import { Course, CourseOverview, CourseService } from '../../core/services/course-service';
import { CourseTeacherService } from '../../core/services/course-teacher-service';
import { SnackbarService } from '../../core/services/snackbar-service';
import { SessionService } from '../../core/auth/session-service';
import { RosterOverview, RosterService } from '../../core/services/roster-service';

@Component({
  selector: 'app-course-list',
  imports: [
    FormsModule,
    RouterLink,
    MatTableModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatInput,
    MatSelect,
    MatOption,
    MatProgressBar,
    MatIcon
  ],
  templateUrl: './course-list.html',
  styleUrl: './course-list.scss'
})
export class CourseList implements OnInit {
  private readonly courseService = inject(CourseService);
  private readonly courseTeacherService = inject(CourseTeacherService);
  private readonly rosterService = inject(RosterService);
  private readonly snackbar = inject(SnackbarService);
  private readonly router = inject(Router);
  protected readonly session = inject(SessionService);

  protected readonly displayedColumns: string[] = ['title', 'rosterName', 'memberCount', 'flags', 'actions'];
  protected readonly courses: WritableSignal<CourseOverview[]> = signal([]);
  protected readonly manageableCourseIds: WritableSignal<ReadonlySet<number>> = signal(new Set());
  protected readonly loading: WritableSignal<boolean> = signal(false);

  protected readonly newTitle: WritableSignal<string> = signal('');
  protected readonly newRosterId: WritableSignal<number | null> = signal(null);
  protected readonly rosters: WritableSignal<RosterOverview[]> = signal([]);
  protected readonly creating: WritableSignal<boolean> = signal(false);

  public async ngOnInit(): Promise<void> {
    await Promise.all([this.reload(), this.reloadRosters()]);
  }

  protected async handleRowClicked(course: CourseOverview): Promise<void> {
    if (!this.canManageCourse(course)) {
      return;
    }

    await this.router.navigate(['courses', course.id]);
  }

  protected async handleCreateClicked(): Promise<void> {
    const title = this.newTitle().trim();
    const rosterId = this.newRosterId();
    if (title === '' || rosterId === null) {
      this.snackbar.show('Pick a roster and enter a title');

      return;
    }

    this.creating.set(true);
    const result: Course | 'conflict' | 'not-found' | null =
      await this.courseService.createCourse(title, rosterId);
    this.creating.set(false);

    if (result === 'conflict') {
      this.snackbar.show('A course with this title already exists for the roster');

      return;
    }
    if (result === 'not-found') {
      this.snackbar.show('Referenced roster does not exist');

      return;
    }
    if (result === null) {
      this.snackbar.show('Could not create the course');

      return;
    }

    this.newTitle.set('');
    this.newRosterId.set(null);
    await this.reload();
  }

  private async reloadRosters(): Promise<void> {
    const result = await this.rosterService.getRosters();
    if (result === null) {
      this.snackbar.show('Could not load rosters');

      return;
    }
    this.rosters.set(result);
  }

  private async reload(): Promise<void> {
    this.loading.set(true);
    const result = await this.courseService.getCourses();
    this.loading.set(false);
    if (result === null) {
      this.snackbar.show('Could not load courses');

      return;
    }
    this.courses.set(result);
    const teacherResults = await Promise.all(
      result.map(course => this.courseTeacherService.getTeachers(course.id))
    );
    const studentId = this.session.identity()?.studentId;
    const manageableIds = new Set<number>();
    if (studentId !== undefined) {
      result.forEach((course, index) => {
        const teachers = teacherResults[index];
        if (teachers !== null
          && (teachers.owner.studentId === studentId
            || teachers.coTeachers.some(teacher => teacher.studentId === studentId))) {
          manageableIds.add(course.id);
        }
      });
    }
    this.manageableCourseIds.set(manageableIds);
  }

  protected canManageCourse(course: CourseOverview): boolean {
    return this.manageableCourseIds().has(course.id);
  }
}
