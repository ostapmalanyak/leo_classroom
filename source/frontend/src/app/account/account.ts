import { Component, computed, DestroyRef, inject, OnInit, signal, Signal, WritableSignal } from '@angular/core';
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
  private readonly destroyRef = inject(DestroyRef);

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

  /** The panel whose heading last passed the top of the viewport; the rail marks it. */
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

  public constructor() {
    this.destroyRef.onDestroy(() => window.removeEventListener('scroll', this.onScroll));
  }

  public async ngOnInit(): Promise<void> {
    window.addEventListener('scroll', this.onScroll, { passive: true });
    this.onScroll();

    await this.meService.load();

    // first visit after provisioning: issue the credential straight away so the student leaves this page
    // with something that works, rather than wondering why cloning asks for a password they never had
    if (this.neverIssued() && (this.me()?.gitCredentialManaged ?? false)) {
      await this.reset();
    }
  }

  private readonly onScroll = (): void => {
    let current: string = this.sections[0].id;

    for (const section of this.sections) {
      const panel = document.getElementById(section.id);

      // 10rem of slack: a panel counts as current once its heading is close to the top, not only when it touches it.
      if (panel !== null && panel.getBoundingClientRect().top <= 160) {
        current = section.id;
      }
    }

    this.active.set(current);
  };

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
