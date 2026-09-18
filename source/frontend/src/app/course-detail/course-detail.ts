import { Component, inject, input, InputSignal, OnInit, signal, WritableSignal } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCard, MatCardContent, MatCardTitle } from '@angular/material/card';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { MatButton } from '@angular/material/button';
import { MatProgressBar } from '@angular/material/progress-bar';
import { Course, CourseService } from '../../core/services/course-service';
import { SnackbarService } from '../../core/services/snackbar-service';

@Component({
  selector: 'app-course-detail',
  imports: [
    FormsModule,
    MatCard,
    MatCardContent,
    MatCardTitle,
    MatFormField,
    MatLabel,
    MatInput,
    MatSlideToggle,
    MatButton,
    MatProgressBar
  ],
  templateUrl: './course-detail.html',
  styleUrl: './course-detail.scss'
})
export class CourseDetail implements OnInit {
  private readonly courseService = inject(CourseService);
  private readonly snackbar = inject(SnackbarService);
  private readonly router = inject(Router);

  public readonly id: InputSignal<string> = input.required<string>();

  protected readonly course: WritableSignal<Course | null> = signal(null);
  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly saving: WritableSignal<boolean> = signal(false);

  protected readonly title: WritableSignal<string> = signal('');
  protected readonly isReadOnly: WritableSignal<boolean> = signal(false);
  protected readonly studentsRetainAccess: WritableSignal<boolean> = signal(true);

  public async ngOnInit(): Promise<void> {
    this.loading.set(true);
    const course = await this.courseService.getCourseById(Number(this.id()));
    this.loading.set(false);
    if (course === null) {
      this.snackbar.show('Course not found');
      await this.router.navigate(['courses']);

      return;
    }
    this.applyCourse(course);
  }

  protected async handleSaveClicked(): Promise<void> {
    const current = this.course();
    if (current === null) {
      return;
    }

    this.saving.set(true);
    const result = await this.courseService.updateCourse(current.id, this.title().trim(), this.isReadOnly(),
                                                         this.studentsRetainAccess());
    this.saving.set(false);

    if (result === 'conflict') {
      this.snackbar.show('Another course with this title exists for the roster');

      return;
    }
    if (result === 'not-found' || result === null) {
      this.snackbar.show('Could not update the course');

      return;
    }
    this.applyCourse(result);
    this.snackbar.show('Course saved');
  }

  protected async handleDeleteClicked(): Promise<void> {
    const current = this.course();
    if (current === null) {
      return;
    }

    const deleted = await this.courseService.deleteCourse(current.id);
    if (!deleted) {
      this.snackbar.show('Could not delete the course');

      return;
    }
    await this.router.navigate(['courses']);
  }

  private applyCourse(course: Course): void {
    this.course.set(course);
    this.title.set(course.title);
    this.isReadOnly.set(course.isReadOnly);
    this.studentsRetainAccess.set(course.studentsRetainAccess);
  }
}
