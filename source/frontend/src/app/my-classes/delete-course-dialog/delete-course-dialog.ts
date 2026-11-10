import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ClassService } from '../../../core/services/class-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

export interface DeleteCourseDialogData {
  readonly id: number;
  readonly title: string;
}

@Component({
  selector: 'app-delete-course-dialog',
  imports: [MatButtonModule, MatDialogModule, MatProgressSpinnerModule],
  templateUrl: './delete-course-dialog.html',
  styleUrl: './delete-course-dialog.scss'
})
export class DeleteCourseDialog {
  protected readonly data = inject<DeleteCourseDialogData>(MAT_DIALOG_DATA);
  private readonly classService = inject(ClassService);
  private readonly dialogRef = inject(MatDialogRef<DeleteCourseDialog, boolean>);
  private readonly snackbar = inject(SnackbarService);
  protected readonly deleting = signal(false);
  protected readonly error = signal('');

  protected close(): void {
    if (!this.deleting()) {
      this.dialogRef.close(false);
    }
  }

  protected async delete(): Promise<void> {
    if (this.deleting()) {
      return;
    }

    this.deleting.set(true);
    this.dialogRef.disableClose = true;
    this.error.set('');
    try {
      const result = await this.classService.deleteCourse(this.data.id);
      if (result.kind === 'deleted') {
        this.dialogRef.close(true);

        return;
      }

      this.error.set(result.message);
      if (result.status === 403 || result.status === 404) {
        this.snackbar.show(result.message);
      }
    } finally {
      this.dialogRef.disableClose = false;
      this.deleting.set(false);
    }
  }
}
