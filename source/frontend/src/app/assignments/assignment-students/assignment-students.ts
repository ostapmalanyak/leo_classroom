import { Component, computed, inject, input, InputSignal, OnInit, signal, WritableSignal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { AssignmentService, FeedbackState, StudentSubmission } from '../../../core/services/assignment-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

@Component({
  selector: 'app-assignment-students',
  imports: [RouterLink, MatTableModule, MatButton, MatIcon, MatProgressBar],
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
  /** Students open (and accept) the assignment here, so this is the page to send them. */
  protected readonly studentUrl = computed(() => `${window.location.origin}/my-assignments/${this.id()}`);
  protected readonly students: WritableSignal<StudentSubmission[]> = signal([]);
  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly courseId: WritableSignal<number | null> = signal(null);

  // The student list does not carry the assignment, so the parent course is read for
  // the way back. If it is unavailable the link still leads somewhere safe.
  protected get backRoute(): (string | number)[] {
    const courseId = this.courseId();

    return courseId === null ? ['/my-classes'] : ['/my-assignments/course', courseId];
  }

  public async ngOnInit(): Promise<void> {
    await Promise.all([this.loadCourse(), this.reload()]);
  }

  protected async copyLink(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.studentUrl());
      this.snackbar.show('Link copied to the clipboard');
    } catch {
      this.snackbar.show('Could not copy — select the link and copy it manually');
    }
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

  private async loadCourse(): Promise<void> {
    const assignment = await this.service.getForEdit(Number(this.id()));
    if (assignment !== 'forbidden' && assignment !== null) {
      this.courseId.set(assignment.courseId);
    }
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
