import { Component, inject, OnInit, signal, WritableSignal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatListModule } from '@angular/material/list';
import { MatButton } from '@angular/material/button';
import { AssignmentService, StudentAssignmentList } from '../../../core/services/assignment-service';
import { LoadingIndicator } from '../../shared/loading-indicator/loading-indicator';
import { EmptyState } from '../../shared/empty-state/empty-state';
import { InlineError } from '../../shared/inline-error/inline-error';

@Component({
  selector: 'app-student-dashboard',
  imports: [RouterLink, MatCardModule, MatListModule, MatButton, LoadingIndicator, EmptyState, InlineError],
  templateUrl: './student-dashboard.html',
  styleUrl: './student-dashboard.scss'
})
export class StudentDashboard implements OnInit {
  private readonly assignments = inject(AssignmentService);

  protected readonly list: WritableSignal<StudentAssignmentList | null> = signal(null);
  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly failed: WritableSignal<boolean> = signal(false);

  public async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);
    const result = await this.assignments.listMine();
    this.loading.set(false);
    if (result === null) {
      this.failed.set(true);

      return;
    }
    this.list.set(result);
  }
}
