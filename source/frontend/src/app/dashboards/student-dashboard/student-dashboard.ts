import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatCard, MatCardContent } from '@angular/material/card';
import { forgejoOrigin } from '../../../core/auth/auth-config';

@Component({
  selector: 'app-student-dashboard',
  imports: [RouterLink, MatButton, MatCard, MatCardContent],
  templateUrl: './student-dashboard.html',
  styleUrl: './student-dashboard.scss'
})
export class StudentDashboard {
  protected readonly forgejoLoginUrl = `${forgejoOrigin()}/user/login`;
}
