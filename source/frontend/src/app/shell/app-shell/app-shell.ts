import { Component, computed, inject, signal, Signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { MatTooltip } from '@angular/material/tooltip';
import { filter } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SessionService } from '../../../core/auth/session-service';
import { Role } from '../../../core/auth/roles';
import { NavEntry, navEntries, visibleNavEntries } from '../../../core/util/navigation';
import { SearchPaletteService } from '../../../core/services/search-palette-service';
import { forgejoOrigin } from '../../../core/auth/auth-config';
import { NotificationBell } from '../../shared/notification-bell/notification-bell';
import { SearchPalette } from '../../shared/search-palette/search-palette';

@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive, MatIcon, MatMenu, MatMenuItem, MatMenuTrigger, MatTooltip,
    NotificationBell, SearchPalette
  ],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss'
})
export class AppShell {
  protected readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly palette = inject(SearchPaletteService);

  protected readonly entries: Signal<NavEntry[]> =
    computed(() => visibleNavEntries(navEntries, this.session.roles()));

  protected readonly isAdmin = computed(() => this.session.roles().includes(Role.Admin));

  /** The path of the page that is open, so the navigation can mark the current entry. */
  protected readonly url = signal(this.router.url);

  /** The menu of the links under the bar on a phone. */
  protected readonly menuOpen = signal(false);

  protected readonly forgejoUrl = forgejoOrigin();

  /** The initials of the signed in user on the avatar. */
  protected readonly initials = computed(() => {
    const name = this.session.identity()?.displayName ?? '';
    const parts = name.split(' ').filter(part => part.length > 0);

    if (parts.length === 0) {
      return '?';
    }

    return (parts[0][0] + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase();
  });

  /** The role of the signed in user, for the badge in the menu of the avatar. */
  protected readonly roleLabel = computed(() => {
    const roles = this.session.roles();

    return roles.includes(Role.Admin) || roles.includes(Role.Teacher) ? 'Teacher' : 'Student';
  });

  constructor() {
    // The subscription lives in the constructor: takeUntilDestroyed needs an injection
    // context, and there is none in a lifecycle hook.
    this.router.events.pipe(
      filter(event => event instanceof NavigationEnd),
      takeUntilDestroyed()
    ).subscribe(event => {
      this.url.set(event.urlAfterRedirects);
      this.menuOpen.set(false);
    });
  }

  /** The avatar menu ends the session. */
  protected async logout(): Promise<void> {
    await this.session.logout();
  }

  /** The search button in the bar opens the palette of every page. */
  protected openSearch(): void {
    this.palette.open();
  }

  /** The label of the search button says which shortcut opens it. */
  protected readonly searchShortcut = computed(() =>
    typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.userAgent) ? 'Cmd K' : 'Ctrl K');

  /** The entry of the navigation that belongs to the page that is open. */
  protected isCurrent(entry: NavEntry): boolean {
    if (entry.route === '/') {
      return this.url().startsWith('/home');
    }

    return this.url().startsWith(entry.route);
  }
}
