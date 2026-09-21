import { Component, inject, input, InputSignal, OnInit, signal, WritableSignal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatProgressBar } from '@angular/material/progress-bar';
import { Assignment, AssignmentService } from '../../../core/services/assignment-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

@Component({
  selector: 'app-assignment-list',
  imports: [RouterLink, MatButton, MatProgressBar],
  template: `
    <h1>Assignments</h1>
    <a mat-flat-button color="primary" [routerLink]="['/courses', courseId(), 'assignments', 'new']">
      Create new assignment
    </a>

    @if (loading()) {
      <mat-progress-bar mode="indeterminate"></mat-progress-bar>
    } @else if (assignments().length === 0) {
      <p>No assignments yet.</p>
    } @else {
      <div class="assignments">
        @for (assignment of assignments(); track assignment.id) {
          <div class="assignment">
            <strong>{{ assignment.title }}</strong>
            <a mat-button [routerLink]="['/assignments', assignment.id, 'edit']">Edit</a>
            <a mat-button [routerLink]="['/assignments', assignment.id, 'students']">See submissions</a>
          </div>
        }
      </div>
    }
  `
})
export class AssignmentList implements OnInit {
  private readonly service = inject(AssignmentService);
  private readonly snackbar = inject(SnackbarService);

  public readonly courseId: InputSignal<string> = input.required<string>();
  protected readonly assignments: WritableSignal<Assignment[]> = signal([]);
  protected readonly loading: WritableSignal<boolean> = signal(false);

  public async ngOnInit(): Promise<void> {
    this.loading.set(true);
    const result = await this.service.listForCourse(Number(this.courseId()));
    this.loading.set(false);
    if (result === null) {
      this.snackbar.show('Could not load assignments');

      return;
    }
    this.assignments.set(result);
  }
}
