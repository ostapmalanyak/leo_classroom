import { Component, inject, input, InputSignal, OnInit, signal, WritableSignal } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCard, MatCardContent, MatCardTitle } from '@angular/material/card';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatSelect } from '@angular/material/select';
import { MatOption } from '@angular/material/core';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { MatButton } from '@angular/material/button';
import { MatProgressBar } from '@angular/material/progress-bar';
import {
  Assignment, AssignmentService, AssignmentWrite, DeadlineKind, DownloadSnapshotMode, StarterSourceKind
} from '../../../core/services/assignment-service';
import { SnackbarService } from '../../../core/services/snackbar-service';
import { MarkdownEditor } from '../../shared/markdown-editor/markdown-editor';

@Component({
  selector: 'app-assignment-edit',
  imports: [
    FormsModule, MatCard, MatCardContent, MatCardTitle, MatFormField, MatLabel, MatInput, MatSelect, MatOption,
    MatSlideToggle, MatButton, MatProgressBar, MarkdownEditor
  ],
  templateUrl: './assignment-edit.html',
  styleUrl: './assignment-edit.scss'
})
export class AssignmentEdit implements OnInit {
  private readonly service = inject(AssignmentService);
  private readonly snackbar = inject(SnackbarService);
  private readonly router = inject(Router);

  public readonly id: InputSignal<string | undefined> = input<string>();
  public readonly courseId: InputSignal<string | undefined> = input<string>();

  protected readonly DeadlineKind = DeadlineKind;
  protected readonly StarterSourceKind = StarterSourceKind;
  protected readonly DownloadSnapshotMode = DownloadSnapshotMode;
  protected readonly deadlineKinds = Object.values(DeadlineKind);
  protected readonly starterSources = Object.values(StarterSourceKind);
  protected readonly snapshotModes = Object.values(DownloadSnapshotMode);

  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly saving: WritableSignal<boolean> = signal(false);

  protected readonly title: WritableSignal<string> = signal('');
  protected readonly description: WritableSignal<string> = signal('');
  protected readonly hints: WritableSignal<string> = signal('');
  protected readonly deadline: WritableSignal<string> = signal('');
  protected readonly deadlineKind: WritableSignal<DeadlineKind> = signal(DeadlineKind.None);
  protected readonly hardRevokesRead: WritableSignal<boolean> = signal(false);
  protected readonly starterSource: WritableSignal<StarterSourceKind> = signal(StarterSourceKind.DescriptionOnly);
  protected readonly starterRepoUrl: WritableSignal<string> = signal('');
  protected readonly readme: WritableSignal<string> = signal('');
  protected readonly autoDeleteEnabled: WritableSignal<boolean> = signal(false);
  protected readonly autoDeleteOn: WritableSignal<string> = signal('');
  protected readonly snapshotMode: WritableSignal<DownloadSnapshotMode> = signal(DownloadSnapshotMode.Deadline);

  protected isEditMode(): boolean {
    return this.id() !== undefined;
  }

  public async ngOnInit(): Promise<void> {
    if (!this.isEditMode()) {
      return;
    }

    this.loading.set(true);
    const result = await this.service.getForEdit(Number(this.id()));
    this.loading.set(false);
    if (result === 'forbidden') {
      this.snackbar.show('You are not allowed to edit this assignment');
      await this.router.navigate(['courses']);

      return;
    }
    if (result === null) {
      this.snackbar.show('Assignment not found');
      await this.router.navigate(['courses']);

      return;
    }
    this.apply(result);
  }

  protected async handleSaveClicked(): Promise<void> {
    if (this.title().trim() === '') {
      this.snackbar.show('A title is required');

      return;
    }

    this.saving.set(true);
    const body = this.buildWrite();
    const result = this.isEditMode()
      ? await this.service.update(Number(this.id()), body)
      : await this.service.create({ ...body, courseId: Number(this.courseId()) });
    this.saving.set(false);

    if (result === 'forbidden') {
      this.snackbar.show('You are not allowed to save this assignment');

      return;
    }
    if (result === null || result === 'not-found' || result === 'conflict') {
      this.snackbar.show('Could not save the assignment');

      return;
    }
    this.snackbar.show('Assignment saved');
    await this.router.navigate(['assignments', result.id, 'students']);
  }

  private buildWrite(): AssignmentWrite {
    return {
      title: this.title().trim(),
      description: this.emptyToNull(this.description()),
      hintsInstructions: this.emptyToNull(this.hints()),
      deadline: this.emptyToNull(this.deadline()),
      deadlineKind: this.deadlineKind(),
      hardDeadlineRevokesRead: this.hardRevokesRead(),
      starterSourceKind: this.starterSource(),
      starterRepoUrl: this.emptyToNull(this.starterRepoUrl()),
      readmeMarkdown: this.emptyToNull(this.readme()),
      autoDeleteEnabled: this.autoDeleteEnabled(),
      autoDeleteOn: this.emptyToNull(this.autoDeleteOn()),
      downloadSnapshotMode: this.snapshotMode()
    };
  }

  private apply(a: Assignment): void {
    this.title.set(a.title);
    this.description.set(a.description ?? '');
    this.hints.set(a.hintsInstructions ?? '');
    this.deadline.set(a.deadline?.toString() ?? '');
    this.deadlineKind.set(a.deadlineKind);
    this.hardRevokesRead.set(a.hardDeadlineRevokesRead);
    this.starterSource.set(a.starterSourceKind);
    this.starterRepoUrl.set(a.starterRepoUrl ?? '');
    this.readme.set(a.readmeMarkdown ?? '');
    this.autoDeleteEnabled.set(a.autoDeleteEnabled);
    this.autoDeleteOn.set(a.autoDeleteOn?.toString() ?? '');
    this.snapshotMode.set(a.downloadSnapshotMode);
  }

  private emptyToNull(value: string): string | null {
    const trimmed = value.trim();

    return trimmed === '' ? null : trimmed;
  }
}
