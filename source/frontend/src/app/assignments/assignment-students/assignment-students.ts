import { Component, inject, input, InputSignal, OnInit, signal, WritableSignal } from '@angular/core';
import { Router } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatButton } from '@angular/material/button';
import { MatProgressBar } from '@angular/material/progress-bar';
import { AssignmentService, FeedbackState, StudentSubmission } from '../../../core/services/assignment-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

@Component({
  selector: 'app-assignment-students',
  imports: [MatTableModule, MatButton, MatProgressBar],
  templateUrl: './assignment-students.html',
  styleUrl: './assignment-students.scss'
})
export class AssignmentStudents implements OnInit {
  private readonly service = inject(AssignmentService);
  private readonly snackbar = inject(SnackbarService);
  private readonly router = inject(Router);

  public readonly id: InputSignal<string> = input.required<string>();

  protected readonly FeedbackState = FeedbackState;
  protected readonly displayedColumns: string[] =
    ['student', 'status', 'late', 'lastCommit', 'feedback', 'repo', 'actions'];
  protected readonly students: WritableSignal<StudentSubmission[]> = signal([]);
  protected readonly loading: WritableSignal<boolean> = signal(false);

  public async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected async handleEditClicked(): Promise<void> {
    await this.router.navigate(['assignments', this.id(), 'edit']);
  }

  protected async handleDownloadAllClicked(): Promise<void> {
    await this.router.navigate(['assignments', this.id(), 'download']);
  }

  protected async viewAnalytics(submission: StudentSubmission): Promise<void> {
    await this.router.navigate(['assignments', this.id(), 'acceptances', submission.acceptanceId, 'analytics']);
  }

  protected async openFeedbackPr(submission: StudentSubmission): Promise<void> {
    const result = await this.service.openFeedbackPr(submission.acceptanceId);
    if (result === 'forbidden' || result === 'not-found' || result === null) {
      this.snackbar.show('Could not open the feedback pull request');

      return;
    }
    window.open(result.htmlUrl, '_blank', 'noopener');
    this.snackbar.show('Feedback pull request ready');
  }

  private async reload(): Promise<void> {
    this.loading.set(true);
    const result = await this.service.getStudents(Number(this.id()));
    this.loading.set(false);
    if (result === null) {
      this.snackbar.show('Could not load the student list');

      return;
    }
    this.students.set(result);
  }
}
