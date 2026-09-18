import { Component, inject, OnInit, signal, WritableSignal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { ForgejoHealthReport, ForgejoHealthService } from '../../../core/services/forgejo-health-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

@Component({
  selector: 'app-forgejo-health',
  imports: [MatButton, MatIcon, MatProgressBar],
  templateUrl: './forgejo-health.html',
  styleUrl: './forgejo-health.scss'
})
export class ForgejoHealth implements OnInit {
  private readonly service = inject(ForgejoHealthService);
  private readonly snackbar = inject(SnackbarService);

  protected readonly report: WritableSignal<ForgejoHealthReport | null> = signal(null);
  protected readonly running: WritableSignal<boolean> = signal(false);

  public async ngOnInit(): Promise<void> {
    await this.run();
  }

  protected async run(): Promise<void> {
    this.running.set(true);
    const result = await this.service.getHealth();
    this.running.set(false);

    if (result === null) {
      this.snackbar.show('Could not reach the backend to run the check');

      return;
    }
    this.report.set(result);
  }
}
