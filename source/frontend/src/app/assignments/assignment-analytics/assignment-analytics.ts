import { Component, inject, input, InputSignal, OnInit, signal, WritableSignal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatCardModule } from '@angular/material/card';
import { AssignmentService, CommitAnalytics } from '../../../core/services/assignment-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

@Component({
  selector: 'app-assignment-analytics',
  imports: [MatProgressBar, MatCardModule, DecimalPipe],
  templateUrl: './assignment-analytics.html',
  styleUrl: './assignment-analytics.scss'
})
export class AssignmentAnalytics implements OnInit {
  private readonly service = inject(AssignmentService);
  private readonly snackbar = inject(SnackbarService);

  public readonly acceptanceId: InputSignal<string> = input.required<string>();

  protected readonly analytics: WritableSignal<CommitAnalytics | null> = signal(null);
  protected readonly loading: WritableSignal<boolean> = signal(false);

  public async ngOnInit(): Promise<void> {
    this.loading.set(true);
    const result = await this.service.getAnalytics(Number(this.acceptanceId()));
    this.loading.set(false);
    if (result === 'forbidden' || result === null) {
      this.snackbar.show('Could not load analytics');

      return;
    }
    this.analytics.set(result);
  }
}
