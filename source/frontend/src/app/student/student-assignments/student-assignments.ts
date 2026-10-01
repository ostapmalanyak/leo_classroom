import { Component, inject, OnInit, signal, WritableSignal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatCard, MatCardContent, MatCardTitle } from '@angular/material/card';
import { MatButton } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatProgressBar } from '@angular/material/progress-bar';
import { AssignedAssignment, AssignmentService } from '../../../core/services/assignment-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

@Component({
  selector: 'app-student-assignments',
  imports: [RouterLink, MatCard, MatCardContent, MatCardTitle, MatListModule, MatProgressBar, MatButton],
  templateUrl: './student-assignments.html',
  styleUrl: './student-assignments.scss'
})
export class StudentAssignments implements OnInit {
  private readonly service = inject(AssignmentService);
  private readonly snackbar = inject(SnackbarService);

  protected readonly accepted: WritableSignal<AssignedAssignment[]> = signal([]);
  protected readonly unaccepted: WritableSignal<AssignedAssignment[]> = signal([]);
  protected readonly loading: WritableSignal<boolean> = signal(false);

  public async ngOnInit(): Promise<void> {
    this.loading.set(true);
    const result = await this.service.listMine();
    this.loading.set(false);
    if (result === null) {
      this.snackbar.show('Could not load your assignments');

      return;
    }
    this.accepted.set(this.sortAssignments(result.accepted));
    this.unaccepted.set(this.sortAssignments(result.unaccepted));
  }

  private sortAssignments(assignments: AssignedAssignment[]): AssignedAssignment[] {
    return [...assignments].sort((left, right) => {
      if (left.deadline === null && right.deadline === null) {
        return left.title.localeCompare(right.title);
      }
      if (left.deadline === null) {
        return 1;
      }
      if (right.deadline === null) {
        return -1;
      }

      return left.deadline.compareTo(right.deadline);
    });
  }
}
