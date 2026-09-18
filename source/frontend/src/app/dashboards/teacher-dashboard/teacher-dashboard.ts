import { Component, inject, OnInit, signal, WritableSignal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatListModule } from '@angular/material/list';
import { MatButton } from '@angular/material/button';
import { CourseOverview, CourseService } from '../../../core/services/course-service';
import { LoadingIndicator } from '../../shared/loading-indicator/loading-indicator';
import { EmptyState } from '../../shared/empty-state/empty-state';
import { InlineError } from '../../shared/inline-error/inline-error';

@Component({
  selector: 'app-teacher-dashboard',
  imports: [RouterLink, MatCardModule, MatListModule, MatButton, LoadingIndicator, EmptyState, InlineError],
  templateUrl: './teacher-dashboard.html',
  styleUrl: './teacher-dashboard.scss'
})
export class TeacherDashboard implements OnInit {
  private readonly courses = inject(CourseService);

  protected readonly list: WritableSignal<CourseOverview[] | null> = signal(null);
  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly failed: WritableSignal<boolean> = signal(false);

  public async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);
    const result = await this.courses.getCourses();
    this.loading.set(false);
    if (result === null) {
      this.failed.set(true);

      return;
    }
    this.list.set(result);
  }
}
