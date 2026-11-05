import { Component, computed, inject, Signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { SessionService } from '../../../core/auth/session-service';
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

  protected readonly entries: Signal<NavEntry[]> =
    computed(() => visibleNavEntries(navEntries, this.session.roles()));

  protected async logout(): Promise<void> {
    await this.session.logout();
  }
}
