import { Component, computed, inject, OnInit, signal, Signal, WritableSignal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatButton } from '@angular/material/button';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatIcon } from '@angular/material/icon';
import { Instant } from '@js-joda/core';
import { IssuedGitCredential, Me, MeService } from '../../core/services/me-service';
import { SnackbarService } from '../../core/services/snackbar-service';
import { SessionService } from '../../core/auth/session-service';
import { backendOrigin } from '../../core/auth/auth-config';

@Component({
  selector: 'app-account',
  imports: [MatButton, MatProgressBar, MatIcon, DatePipe],
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

  protected readonly neverIssued = computed(() => this.me()?.gitCredentialIssuedAt === null);
  protected readonly forgejoOrigin = signal(backendOrigin());

  public async ngOnInit(): Promise<void> {
    await this.meService.load();

    // first visit after provisioning: issue the credential straight away so the student leaves this page
    // with something that works, rather than wondering why cloning asks for a password they never had
    if (this.neverIssued() && (this.me()?.gitCredentialManaged ?? false)) {
      await this.reset();
    }
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
