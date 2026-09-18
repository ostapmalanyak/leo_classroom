import { Component, inject, OnInit, signal, WritableSignal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatSelect } from '@angular/material/select';
import { MatOption } from '@angular/material/core';
import { MatButton } from '@angular/material/button';
import { MatProgressBar } from '@angular/material/progress-bar';
import { AuditAction, AuditEvent, AuditService } from '../../../core/services/audit-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

@Component({
  selector: 'app-audit-log',
  imports: [
    FormsModule, MatTableModule, MatFormField, MatLabel, MatInput, MatSelect, MatOption, MatButton, MatProgressBar
  ],
  templateUrl: './audit-log.html',
  styleUrl: './audit-log.scss'
})
export class AuditLog implements OnInit {
  private readonly service = inject(AuditService);
  private readonly snackbar = inject(SnackbarService);

  protected readonly actions = Object.values(AuditAction);
  protected readonly displayedColumns: string[] = ['at', 'actor', 'action', 'target', 'metadata'];
  protected readonly events: WritableSignal<AuditEvent[]> = signal([]);
  protected readonly loading: WritableSignal<boolean> = signal(false);

  protected readonly actor: WritableSignal<string> = signal('');
  protected readonly action: WritableSignal<AuditAction | null> = signal(null);
  protected readonly from: WritableSignal<string> = signal('');
  protected readonly to: WritableSignal<string> = signal('');

  public async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    const result = await this.service.query({
      from: this.emptyToNull(this.from()),
      to: this.emptyToNull(this.to()),
      actor: this.emptyToNull(this.actor()),
      action: this.action()
    });
    this.loading.set(false);
    if (result === null) {
      this.snackbar.show('Could not load the audit log');

      return;
    }
    this.events.set(result);
  }

  private emptyToNull(value: string): string | null {
    return value.trim() === '' ? null : value.trim();
  }
}
