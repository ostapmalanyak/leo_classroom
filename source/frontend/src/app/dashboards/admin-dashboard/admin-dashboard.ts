import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIcon } from '@angular/material/icon';

interface AdminLink {
  title: string;
  description: string;
  route: string;
  icon: string;
}

@Component({
  selector: 'app-admin-dashboard',
  imports: [RouterLink, MatCardModule, MatIcon],
  templateUrl: './admin-dashboard.html',
  styleUrl: './admin-dashboard.scss'
})
export class AdminDashboard {
  protected readonly links: readonly AdminLink[] = [
    { title: 'Audit log', description: 'Review administrative and system actions', route: '/audit', icon: 'fact_check' },
    { title: 'Courses', description: 'Browse and manage courses', route: '/courses', icon: 'school' },
    { title: 'Rosters', description: 'Manage class and custom rosters', route: '/rosters', icon: 'group' }
  ];
}
