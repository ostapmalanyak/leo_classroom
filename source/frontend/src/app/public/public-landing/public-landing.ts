import { Component, inject } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { SessionService } from '../../../core/auth/session-service';

@Component({
  selector: 'app-public-landing',
  imports: [MatButton, MatCardModule],
  template: `
    <div class="landing">
      <mat-card>
        <mat-card-content>
          <h1>LEO Classroom</h1>
          <p>Sign in to manage and submit assignments.</p>
          <button mat-raised-button color="primary" (click)="login()">Login</button>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: `
    .landing {
      display: flex;
      justify-content: center;
      align-items: center;
      min-height: 100vh;
      padding: 1rem;
    }
    mat-card-content {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 1rem;
      padding: 2rem;
    }
  `
})
export class PublicLanding {
  private readonly session = inject(SessionService);

  protected async login(): Promise<void> {
    await this.session.login();
  }
}
