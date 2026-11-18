import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';

export interface DeleteRosterDialogData {
  readonly name: string;
}

/**
 * Asks before a roster is deleted. A roster cannot be undone, so the question is asked.
 */
@Component({
  selector: 'app-delete-roster-dialog',
  imports: [MatButtonModule, MatDialogModule, MatIcon],
  template: `
    <div class="confirm-dialog">
      <h2 mat-dialog-title id="delete-roster-title">Delete roster?</h2>

      <mat-dialog-content>
        <p>“{{ data.name }}” is deleted for good. This cannot be undone.</p>
      </mat-dialog-content>

      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button class="btn confirm-dialog__confirm" type="button" mat-dialog-close="delete">
          <mat-icon aria-hidden="true">delete</mat-icon>
          <span>Delete roster</span>
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
export class DeleteRosterDialog {
  protected readonly data = inject<DeleteRosterDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<DeleteRosterDialog, string>);
}
