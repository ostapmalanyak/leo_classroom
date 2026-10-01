import { Component, inject, OnInit, signal, WritableSignal } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatIcon } from '@angular/material/icon';
import { Roster, RosterKind, RosterOverview, RosterService } from '../../../core/services/roster-service';
import { LdapSyncOutcome, LdapSyncService } from '../../../core/services/ldap-sync-service';
import { SnackbarService } from '../../../core/services/snackbar-service';
import { SessionService } from '../../../core/auth/session-service';

@Component({
  selector: 'app-roster-list',
  imports: [
    FormsModule,
    MatTableModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatInput,
    MatProgressBar,
    MatIcon
  ],
  templateUrl: './roster-list.html',
  styleUrl: './roster-list.scss'
})
export class RosterList implements OnInit {
  private readonly rosterService = inject(RosterService);
  private readonly ldapSyncService = inject(LdapSyncService);
  private readonly snackbar = inject(SnackbarService);
  private readonly router = inject(Router);
  protected readonly session = inject(SessionService);

  protected readonly RosterKind = RosterKind;
  protected readonly displayedColumns: string[] = ['name', 'kind', 'memberCount'];
  protected readonly rosters: WritableSignal<RosterOverview[]> = signal([]);
  protected readonly loading: WritableSignal<boolean> = signal(false);

  protected readonly newName: WritableSignal<string> = signal('');
  protected readonly creating: WritableSignal<boolean> = signal(false);
  protected readonly syncing: WritableSignal<boolean> = signal(false);

  public async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected async handleRowClicked(roster: RosterOverview): Promise<void> {
    await this.router.navigate(['rosters', roster.id]);
  }

  protected async handleCreateClicked(): Promise<void> {
    const name = this.newName().trim();
    if (name === '') {
      this.snackbar.show('A roster name is required');

      return;
    }

    this.creating.set(true);
    const result: Roster | 'conflict' | 'forbidden' | 'not-found' | null =
      await this.rosterService.createRoster(name);
    this.creating.set(false);

    if (result === null || result === 'conflict' || result === 'forbidden' || result === 'not-found') {
      this.snackbar.show('Could not create the roster');

      return;
    }

    this.newName.set('');
    await this.router.navigate(['rosters', result.id]);
  }

  protected async handleSyncClicked(): Promise<void> {
    this.syncing.set(true);
    let result: LdapSyncOutcome | null;
    try {
      result = await this.ldapSyncService.run();
    } finally {
      this.syncing.set(false);
    }
    if (result === null) {
      this.snackbar.show('Could not sync LDAP users and rosters');

      return;
    }
    await this.reload();
    if (result.ldapError) {
      const errorCodes = [
        result.ldapExitCode === null || result.ldapExitCode === undefined
          ? null
          : `ldapsearch exit ${result.ldapExitCode}`,
        result.ldapResultCode === null || result.ldapResultCode === undefined
          ? null
          : `LDAP result ${result.ldapResultCode}`
      ].filter(code => code !== null).join(', ');
      this.snackbar.show(`LDAP query failed${errorCodes === '' ? '' : ` (${errorCodes})`}: ${result.ldapError}`);

      return;
    }
    const safetyNote = result.destructivePassSkipped ? ' Some removals were skipped for safety.' : '';
    this.snackbar.show(
      `LDAP sync complete: ${result.created} created, ${result.updated} updated, `
      + `${result.reactivated} reactivated, ${result.softDeleted} deactivated.${safetyNote}`);
  }

  private async reload(): Promise<void> {
    this.loading.set(true);
    const result = await this.rosterService.getRosters();
    this.loading.set(false);
    if (result === null) {
      this.snackbar.show('Could not load rosters');

      return;
    }
    this.rosters.set(result);
  }
}
