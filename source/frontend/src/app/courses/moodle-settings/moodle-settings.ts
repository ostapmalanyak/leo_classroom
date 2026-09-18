import { Component, inject, input, InputSignal, OnInit, signal, WritableSignal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatButton } from '@angular/material/button';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MoodleService } from '../../../core/services/moodle-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

@Component({
  selector: 'app-moodle-settings',
  imports: [FormsModule, MatSlideToggle, MatFormField, MatLabel, MatInput, MatButton, MatProgressBar],
  templateUrl: './moodle-settings.html',
  styleUrl: './moodle-settings.scss'
})
export class MoodleSettings implements OnInit {
  private readonly service = inject(MoodleService);
  private readonly snackbar = inject(SnackbarService);

  public readonly courseId: InputSignal<string> = input.required<string>();

  protected readonly enabled: WritableSignal<boolean> = signal(false);
  protected readonly baseUrl: WritableSignal<string> = signal('');
  protected readonly moodleCourseId: WritableSignal<number | null> = signal(null);
  protected readonly token: WritableSignal<string> = signal('');
  protected readonly hasToken: WritableSignal<boolean> = signal(false);
  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly saving: WritableSignal<boolean> = signal(false);

  public async ngOnInit(): Promise<void> {
    this.loading.set(true);
    const result = await this.service.get(Number(this.courseId()));
    this.loading.set(false);
    if (result === 'forbidden' || result === 'not-found' || result === null) {
      this.snackbar.show('Could not load the Moodle link');

      return;
    }
    this.enabled.set(result.enabled);
    this.baseUrl.set(result.moodleBaseUrl);
    this.moodleCourseId.set(result.moodleCourseId || null);
    this.hasToken.set(result.hasToken);
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    const result = await this.service.set(Number(this.courseId()), {
      enabled: this.enabled(),
      moodleBaseUrl: this.baseUrl(),
      moodleCourseId: this.moodleCourseId() ?? 0,
      token: this.token().trim() === '' ? null : this.token().trim()
    });
    this.saving.set(false);
    if (result === 'forbidden' || result === 'not-found' || result === null) {
      this.snackbar.show('Could not save the Moodle link');

      return;
    }
    this.token.set('');
    this.hasToken.set(result.hasToken);
    this.snackbar.show('Moodle link saved');
  }
}
