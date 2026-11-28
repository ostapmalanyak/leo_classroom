import { Component, computed, inject, OnInit, signal, Signal, WritableSignal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatButton } from '@angular/material/button';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatIcon } from '@angular/material/icon';
import { Instant } from '@js-joda/core';
import { IssuedGitCredential, Me, MeService } from '../../core/services/me-service';
import { SnackbarService } from '../../core/services/snackbar-service';
import { SessionService } from '../../core/auth/session-service';
import { forgejoOrigin } from '../../core/auth/auth-config';
import { GitLoginStep } from '../shared/git-login-step/git-login-step';

@Component({
  selector: 'app-account',
  imports: [MatButton, MatProgressBar, MatIcon, DatePipe, GitLoginStep],
  templateUrl: './account.html',
  styleUrl: './account.scss'
})
export class Account implements OnInit {
  private readonly meService = inject(MeService);
  private readonly session = inject(SessionService);
  private readonly snackbar = inject(SnackbarService);

  protected readonly me: Signal<Me | null> = this.meService.value;
  protected readonly identity = this.session.identity;
  protected readonly loading: WritableSignal<boolean> = signal(false);

  /** Only ever set from the response of a reset; never fetched, because the backend does not keep it. */
  protected readonly issued: WritableSignal<IssuedGitCredential | null> = signal(null);

  /** The rail mirrors the page: one entry per panel, in the order the panels appear. */
  protected readonly sections = [
    { id: 'profile', label: 'Profile', icon: 'person' },
    { id: 'git-password', label: 'Git password', icon: 'key' },
    { id: 'git-login', label: 'Git login', icon: 'terminal' }
  ] as const;

  /** The section the reader picked. The rail marks it until another is picked. */
  protected readonly active = signal<string>(this.sections[0].id);

  protected readonly neverIssued = computed(() => this.me()?.gitCredentialIssuedAt === null);
  protected readonly forgejoOrigin = signal(forgejoOrigin());

  protected readonly roles = computed(() => this.me()?.roles ?? []);

  /** When the password was last generated, for the status pill beside the heading. */
  protected readonly issuedAt = computed(() => this.asDate(this.me()?.gitCredentialIssuedAt ?? null));

  /** The avatar letters: two initials from the display name, or the student id when there is no name. */
  protected readonly initials = computed(() => {
    const name = (this.identity()?.displayName ?? this.me()?.studentId ?? '').trim();
    const parts = name.split(/\s+/).filter((part) => part.length > 0);

    if (parts.length === 0) {
      return '?';
    }

    return parts.length === 1
      ? parts[0].slice(0, 2).toUpperCase()
      : (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
  });

  public async ngOnInit(): Promise<void> {
    await this.meService.load();

    // first visit after provisioning: issue the credential straight away so the student leaves this page
    // with something that works, rather than wondering why cloning asks for a password they never had
    if (this.neverIssued() && (this.me()?.gitCredentialManaged ?? false)) {
      await this.reset();
    }
  }

  protected goTo(id: string): void {
    this.active.set(id);
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  protected async reset(): Promise<void> {
    this.loading.set(true);
    try {
      this.issued.set(await this.meService.resetGitCredential());
    } catch {
      this.snackbar.show('Could not generate a git password. Please try again.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async copy(value: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(value);
      this.snackbar.show('Copied to the clipboard');
    } catch {
      this.snackbar.show('Could not copy — select the text and copy it manually');
    }
  }

  protected asDate(instant: Instant | null): Date | null {
    return instant === null ? null : new Date(instant.toEpochMilli());
  }
}
