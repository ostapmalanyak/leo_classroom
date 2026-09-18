import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';

@Component({
  selector: 'app-not-found',
  imports: [RouterLink, MatButton, MatIcon],
  template: `
    <div class="error-page">
      <mat-icon>search_off</mat-icon>
      <h1>Page not found</h1>
      <p>The page you are looking for does not exist.</p>
      <a mat-raised-button color="primary" routerLink="/">Go home</a>
    </div>
  `,
  styleUrl: './not-found.scss'
})
export class NotFound {}
