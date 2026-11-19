import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { SessionService } from '../../../core/auth/session-service';

@Component({
  selector: 'app-forbidden',
  imports: [RouterLink, MatButton, MatIcon],
  template: `
    <div class="error-page">
      <mat-icon>block</mat-icon>
      <h1>Access denied</h1>
      @if (session.roles().length > 0) {
        <p>You do not have permission to view this page.</p>
        <a mat-raised-button color="primary" routerLink="/">Go home</a>
      } @else {
        <!-- Without a role every home page is forbidden too, so "Go home" would just land here again. -->
        <p>Your session has no access. Sign in again to continue.</p>
        <button mat-raised-button color="primary" type="button" (click)="session.logout()">Sign in again</button>
      }
    </div>
  `,
  styleUrl: './forbidden.scss'
})
export class Forbidden {
  protected readonly session = inject(SessionService);
}
