import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';

interface DeleteAssignmentDialogData {
  readonly title: string;
}

@Component({
  selector: 'app-delete-assignment-dialog',
  imports: [MatButtonModule, MatDialogModule, MatIcon],
  template: `
    <div class="confirm-dialog">
      <h2 mat-dialog-title id="delete-assignment-title">Delete assignment?</h2>

      <mat-dialog-content>
        <p>
          “{{ data.title }}” and its student submissions, associated repositories, and starter archive
          will be permanently deleted. This cannot be undone.
        </p>
      </mat-dialog-content>

      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button class="btn confirm-dialog__confirm" type="button" mat-dialog-close="delete">
          <mat-icon aria-hidden="true">delete</mat-icon>
          <span>Delete assignment</span>
        </button>
      </mat-dialog-actions>
    </div>
  `,
  styles: `
    .confirm-dialog {
      background-color: var(--color-bg-surface);
      border-radius: var(--course-card-radius);
      padding: var(--space-5);

      h2 {
        color: var(--color-text-strong);
        font-size: var(--font-size-h2);
        margin: 0 0 var(--space-3);
      }

      p {
        color: var(--color-text);
        margin: 0;
      }

      mat-dialog-actions {
        gap: var(--space-2);
        margin: var(--space-4) 0 0;
        min-height: 0;
        padding: 0;
      }
    }

    .confirm-dialog__confirm {
      border-color: var(--color-danger-border);
      color: var(--color-danger-text);

      &:hover {
        background-color: var(--color-danger-bg);
        color: var(--color-danger-text);
      }

      mat-icon {
        font-size: 1.125rem;
        height: 1.125rem;
        width: 1.125rem;
      }
    }
  `
})
export class DeleteAssignmentDialog {
  protected readonly data = inject<DeleteAssignmentDialogData>(MAT_DIALOG_DATA);
}
