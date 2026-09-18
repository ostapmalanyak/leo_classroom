import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';

@Component({
  selector: 'app-forbidden',
  imports: [RouterLink, MatButton, MatIcon],
  template: `
    <div class="error-page">
      <mat-icon>block</mat-icon>
      <h1>Access denied</h1>
      <p>You do not have permission to view this page.</p>
      <a mat-raised-button color="primary" routerLink="/">Go home</a>
    </div>
  `,
  styleUrl: './forbidden.scss'
})
export class Forbidden {}
