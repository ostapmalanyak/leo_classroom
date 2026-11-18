import { Component, inject, OnInit, signal, WritableSignal } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { RosterOverview, RosterService } from '../../../../core/services/roster-service';
import { SnackbarService } from '../../../../core/services/snackbar-service';

/**
 * The rosters of a teacher in a popup: the place where a class list is created and opened,
 * without leaving the page the teacher is on.
 */
@Component({
  selector: 'app-my-rosters-dialog',
  imports: [MatButtonModule, MatDialogModule, MatIcon, MatProgressBar],
  templateUrl: './my-rosters-dialog.html',
  styleUrl: './my-rosters-dialog.scss'
})
export class MyRostersDialog implements OnInit {
  private readonly rosterService = inject(RosterService);
  private readonly snackbar = inject(SnackbarService);
  private readonly router = inject(Router);
  private readonly dialogRef = inject(MatDialogRef<MyRostersDialog>);

  protected readonly rosters: WritableSignal<RosterOverview[]> = signal([]);
  protected readonly loading: WritableSignal<boolean> = signal(true);
  protected readonly name: WritableSignal<string> = signal('');
  protected readonly creating: WritableSignal<boolean> = signal(false);

  public async ngOnInit(): Promise<void> {
    const all = await this.rosterService.getRosters();
    this.loading.set(false);
    if (all === null) {
      this.snackbar.show('Could not load rosters');

      return;
    }

    this.rosters.set(all.filter(roster => roster.isOwner));
  }

  /** Opens the roster of the teacher in the page behind the popup. */
  protected open(roster: RosterOverview): void {
    this.close();
    void this.router.navigate(['rosters', roster.id]);
  }

  /** Creates a roster and opens it. */
  protected async create(): Promise<void> {
    const name = this.name().trim();
    if (name === '') {
      this.snackbar.show('A roster name is required');

      return;
    }

    this.creating.set(true);
    const created = await this.rosterService.createRoster(name);
    this.creating.set(false);

    if (created === null || created === 'conflict' || created === 'forbidden' || created === 'not-found') {
      this.snackbar.show('Could not create the roster');

      return;
    }

    this.close();
    void this.router.navigate(['rosters', created.id]);
  }

  protected close(): void {
    this.dialogRef.close();
  }
}
