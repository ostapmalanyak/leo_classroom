import { Component, computed, inject, Signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { map } from 'rxjs';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { SessionService } from '../../../core/auth/session-service';
import { NavEntry, navEntries, visibleNavEntries } from '../../../core/util/navigation';

@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatIconModule,
    MatMenuModule, MatSidenavModule, MatListModule
  ],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss'
})
export class AppShell {
  protected readonly session = inject(SessionService);

  private readonly breakpoints = inject(BreakpointObserver);

  protected readonly isHandset: Signal<boolean> = toSignal(
    this.breakpoints.observe(Breakpoints.Handset).pipe(map(result => result.matches)),
    { initialValue: false });

  protected readonly entries: Signal<NavEntry[]> =
    computed(() => visibleNavEntries(navEntries, this.session.roles()));

  protected async logout(): Promise<void> {
    await this.session.logout();
  }
}
