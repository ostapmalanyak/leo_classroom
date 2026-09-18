import { Component, inject, OnInit, signal, WritableSignal } from '@angular/core';
import { Router } from '@angular/router';
import { MatCard, MatCardContent, MatCardTitle } from '@angular/material/card';
import { MatListModule } from '@angular/material/list';
import { MatProgressBar } from '@angular/material/progress-bar';
import { AssignedAssignment, AssignmentService } from '../../../core/services/assignment-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

@Component({
  selector: 'app-student-assignments',
  imports: [MatCard, MatCardContent, MatCardTitle, MatListModule, MatProgressBar],
  templateUrl: './student-assignments.html',
  styleUrl: './student-assignments.scss'
})
export class StudentAssignments implements OnInit {
  private readonly service = inject(AssignmentService);
  private readonly snackbar = inject(SnackbarService);
  private readonly router = inject(Router);

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
    this.accepted.set(result.accepted);
    this.unaccepted.set(result.unaccepted);
  }

  protected async handleOpen(assignment: AssignedAssignment): Promise<void> {
    await this.router.navigate(['my-assignments', assignment.id]);
  }
}
