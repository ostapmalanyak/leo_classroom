import { Component, inject, input, InputSignal, OnInit, signal, WritableSignal } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCard, MatCardContent, MatCardTitle } from '@angular/material/card';
import { MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatListModule } from '@angular/material/list';
import { RosterKind, RosterOverview, RosterService } from '../../../core/services/roster-service';
import { UserRole, UserService, UserSummary } from '../../../core/services/user-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

const SEARCH_DEBOUNCE_MS = 300;
const MIN_SEARCH_LENGTH = 2;

@Component({
  selector: 'app-roster-detail',
  imports: [
    FormsModule,
    MatCard,
    MatCardContent,
    MatCardTitle,
    MatFormField,
    MatHint,
    MatLabel,
    MatInput,
    MatButton,
    MatIconButton,
    MatIcon,
    MatProgressBar,
    MatListModule
  ],
  templateUrl: './roster-detail.html',
  styleUrl: './roster-detail.scss'
})
export class RosterDetail implements OnInit {
  private readonly rosterService = inject(RosterService);
  private readonly userService = inject(UserService);
  private readonly snackbar = inject(SnackbarService);
  private readonly router = inject(Router);

  public readonly id: InputSignal<string> = input.required<string>();

  protected readonly roster: WritableSignal<RosterOverview | null> = signal(null);
  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly name: WritableSignal<string> = signal('');
  protected readonly saving: WritableSignal<boolean> = signal(false);

  protected readonly members: WritableSignal<UserSummary[]> = signal([]);
  protected readonly searchTerm: WritableSignal<string> = signal('');
  protected readonly searchResults: WritableSignal<UserSummary[]> = signal([]);
  protected readonly searching: WritableSignal<boolean> = signal(false);

  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  public async ngOnInit(): Promise<void> {
    this.loading.set(true);
    const rosters = await this.rosterService.getRosters();
    this.loading.set(false);
    const found = rosters?.find(r => r.id === Number(this.id())) ?? null;
    if (found === null) {
      this.snackbar.show('Roster not found');
      await this.router.navigate(['rosters']);

      return;
    }
    this.roster.set(found);
    this.name.set(found.name);
    if (found.kind === RosterKind.Custom) {
      await this.loadMembers(found.id);
    }
  }

  private async loadMembers(rosterId: number): Promise<void> {
    const members = await this.rosterService.getMembers(rosterId);
    if (members === null) {
      this.snackbar.show('Could not load roster members');

      return;
    }
    this.members.set(members);
  }

  protected isEditable(): boolean {
    return this.roster()?.kind === RosterKind.Custom;
  }

  protected async handleRenameClicked(): Promise<void> {
    const current = this.roster();
    const name = this.name().trim();
    if (current === null || name === '') {
      return;
    }

    this.saving.set(true);
    const result = await this.rosterService.renameRoster(current.id, name);
    this.saving.set(false);
    if (result === 'forbidden') {
      this.snackbar.show('You are not allowed to rename this roster');

      return;
    }
    if (result === null || result === 'conflict' || result === 'not-found') {
      this.snackbar.show('Could not rename the roster');

      return;
    }
    this.roster.set({ ...current, name: result.name });
    this.snackbar.show('Roster renamed');
  }

  protected async handleDeleteClicked(): Promise<void> {
    const current = this.roster();
    if (current === null) {
      return;
    }

    const result = await this.rosterService.deleteRoster(current.id);
    if (result === 'ok') {
      await this.router.navigate(['rosters']);

      return;
    }
    if (typeof result === 'object' && result !== null) {
      this.snackbar.show(`In use by ${result.courseCount} course(s); cannot delete`);

      return;
    }
    if (result === 'forbidden') {
      this.snackbar.show('You are not allowed to delete this roster');

      return;
    }
    this.snackbar.show('Could not delete the roster');
  }

  protected handleSearchChange(term: string): void {
    this.searchTerm.set(term);
    if (this.searchTimer !== null) {
      clearTimeout(this.searchTimer);
    }
    if (term.trim().length < MIN_SEARCH_LENGTH) {
      this.searchResults.set([]);

      return;
    }
    this.searchTimer = setTimeout(() => void this.runSearch(term.trim()), SEARCH_DEBOUNCE_MS);
  }

  protected async handleAddMember(user: UserSummary): Promise<void> {
    const current = this.roster();
    if (current === null) {
      return;
    }

    const result = await this.rosterService.addMember(current.id, user.id);
    if (result === 'forbidden') {
      this.snackbar.show('You are not allowed to edit this roster');

      return;
    }
    if (result !== 'ok') {
      this.snackbar.show('Could not add the member');

      return;
    }
    if (!this.members().some(m => m.id === user.id)) {
      this.members.update(list => [...list, user]);
    }
    this.snackbar.show(`Added ${user.firstName} ${user.lastName}`);
  }

  protected async handleRemoveMember(user: UserSummary): Promise<void> {
    const current = this.roster();
    if (current === null) {
      return;
    }

    const result = await this.rosterService.removeMember(current.id, user.id);
    if (result === 'forbidden') {
      this.snackbar.show('You are not allowed to edit this roster');

      return;
    }
    if (result !== 'ok') {
      this.snackbar.show('Could not remove the member');

      return;
    }
    this.members.update(list => list.filter(m => m.id !== user.id));
  }

  private async runSearch(term: string): Promise<void> {
    this.searching.set(true);
    const results = await this.userService.search(term, UserRole.Student);
    this.searching.set(false);
    if (results === null) {
      this.snackbar.show('Could not search students');

      return;
    }
    this.searchResults.set(results);
  }
}
