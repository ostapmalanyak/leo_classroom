import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { CourseIcon } from '../../../shared/course-icon/course-icon';

/** One course a new assignment could belong to. */
export interface ChooseCourseEntry {
  id: number;
  title: string;
  iconSeed: string | null;
}

export interface ChooseCourseDialogData {
  readonly courses: ChooseCourseEntry[];
}

/**
 * Asks which course an assignment belongs to. An assignment always lives in a course, and
 * the teacher knows the course by heart - the app does not.
 */
@Component({
  selector: 'app-choose-course-dialog',
  imports: [MatButtonModule, MatDialogModule, MatIcon, CourseIcon],
  templateUrl: './choose-course-dialog.html',
  styleUrl: './choose-course-dialog.scss'
})
export class ChooseCourseDialog {
  protected readonly data = inject<ChooseCourseDialogData>(MAT_DIALOG_DATA);

  /** The course the teacher picked. */
  protected readonly selected = signal<number | null>(this.data.courses[0]?.id ?? null);

  protected pick(id: number): void {
    this.selected.set(id);
  }
}
