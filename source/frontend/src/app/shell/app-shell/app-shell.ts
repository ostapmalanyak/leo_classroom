import { Component, computed, inject, signal, Signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { SessionService } from '../../../core/auth/session-service';
import { Role } from '../../../core/auth/roles';
import { NavEntry, navEntries, visibleNavEntries } from '../../../core/util/navigation';

@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatIconModule,
    MatMenuModule
  ],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss'
})
export class AppShell {
  protected readonly session = inject(SessionService);
  private readonly router = inject(Router);

  /** The path of the page that is open, so the navigation can mark the current entry. */
  protected readonly url = signal(this.router.url);

  protected readonly entries: Signal<NavEntry[]> =
    computed(() => visibleNavEntries(navEntries, this.session.roles()));

  // same rule as the route's data.role: admins pass every role check
  protected readonly isStudent = computed(() =>
    this.session.roles().some(r => r === Role.Student || r === Role.Admin));

  constructor() {
    this.router.events.pipe(
      filter(event => event instanceof NavigationEnd),
      takeUntilDestroyed()
    ).subscribe(event => this.url.set(event.urlAfterRedirects));
  }

  protected async logout(): Promise<void> {
    await this.session.logout();
  }
}
