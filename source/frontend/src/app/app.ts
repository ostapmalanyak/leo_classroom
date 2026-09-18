import { Component, inject } from '@angular/core';
import { AppShell } from './shell/app-shell/app-shell';
import { PublicLanding } from './public/public-landing/public-landing';
import { SessionService } from '../core/auth/session-service';

@Component({
  selector: 'app-root',
  imports: [AppShell, PublicLanding],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly session = inject(SessionService);
}
