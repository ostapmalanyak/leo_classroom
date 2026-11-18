import { Component, computed, input } from '@angular/core';
import { courseIconSeed, courseIconUrl } from '../../../core/util/course-icon';

export type CourseIconSize = 'sm' | 'md' | 'lg';

/**
 * The icon of a course, drawn by the backend from the seed the course was created with.
 * Every list uses this component, so a course always shows the same icon.
 */
@Component({
  selector: 'app-course-icon',
  templateUrl: './course-icon.html',
  styleUrl: './course-icon.scss'
})
export class CourseIcon {
  readonly courseId = input.required<number>();
  readonly iconSeed = input<string | null>(null);
  readonly name = input<string>('');
  readonly size = input<CourseIconSize>('md');

  protected readonly url = computed(() =>
    courseIconUrl(courseIconSeed(this.iconSeed(), this.courseId())));

  protected readonly alt = computed(() => this.name() === '' ? '' : `${this.name()} icon`);
}
