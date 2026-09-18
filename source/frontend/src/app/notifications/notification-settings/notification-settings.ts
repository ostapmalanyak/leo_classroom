import { Component, inject, OnInit, signal, WritableSignal } from '@angular/core';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { MatButton } from '@angular/material/button';
import { MatProgressBar } from '@angular/material/progress-bar';
import { NotificationService } from '../../../core/services/notification-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

@Component({
  selector: 'app-notification-settings',
  imports: [MatSlideToggle, MatButton, MatProgressBar],
  templateUrl: './notification-settings.html',
  styleUrl: './notification-settings.scss'
})
export class NotificationSettings implements OnInit {
  private readonly service = inject(NotificationService);
  private readonly snackbar = inject(SnackbarService);

  protected readonly newAssignment: WritableSignal<boolean> = signal(false);
  protected readonly deadlineChanged: WritableSignal<boolean> = signal(false);
  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly saving: WritableSignal<boolean> = signal(false);

  public async ngOnInit(): Promise<void> {
    this.loading.set(true);
    const result = await this.service.getPreferences();
    this.loading.set(false);
    if (result === null) {
      this.snackbar.show('Could not load notification settings');

      return;
    }
    this.newAssignment.set(result.newAssignment);
    this.deadlineChanged.set(result.deadlineChanged);
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    const result = await this.service.updatePreferences({
      newAssignment: this.newAssignment(),
      deadlineChanged: this.deadlineChanged()
    });
    this.saving.set(false);
    if (result === null) {
      this.snackbar.show('Could not save notification settings');

      return;
    }
    this.snackbar.show('Notification settings saved');
  }
}
