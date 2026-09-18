import { Routes } from '@angular/router';
import { authGuard } from '../core/auth/auth-guard';
import { Role } from '../core/auth/roles';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    canActivate: [authGuard],
    loadComponent: () => import('./shell/index-redirect/index-redirect').then(m => m.IndexRedirect)
  },

  {
    path: 'home/student',
    canActivate: [authGuard],
    data: { role: Role.Student },
    loadComponent: () => import('./dashboards/student-dashboard/student-dashboard').then(m => m.StudentDashboard)
  },
  {
    path: 'home/teacher',
    canActivate: [authGuard],
    data: { role: Role.Teacher },
    loadComponent: () => import('./dashboards/teacher-dashboard/teacher-dashboard').then(m => m.TeacherDashboard)
  },
  {
    path: 'home/admin',
    canActivate: [authGuard],
    data: { role: Role.Admin },
    loadComponent: () => import('./dashboards/admin-dashboard/admin-dashboard').then(m => m.AdminDashboard)
  },

  {
    path: 'courses',
    canActivate: [authGuard],
    loadComponent: () => import('./course-list/course-list').then(m => m.CourseList)
  },
  {
    path: 'courses/:courseId/moodle',
    canActivate: [authGuard],
    data: { role: Role.Teacher },
    loadComponent: () => import('./courses/moodle-settings/moodle-settings').then(m => m.MoodleSettings)
  },
  {
    path: 'courses/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./course-detail/course-detail').then(m => m.CourseDetail)
  },
  {
    path: 'cli',
    canActivate: [authGuard],
    data: { role: Role.Teacher },
    loadComponent: () => import('./cli-download/cli-download').then(m => m.CliDownload)
  },
  {
    path: 'audit',
    canActivate: [authGuard],
    data: { role: Role.Admin },
    loadComponent: () => import('./admin/audit-log/audit-log').then(m => m.AuditLog)
  },
  {
    path: 'admin/forgejo',
    canActivate: [authGuard],
    data: { role: Role.Admin },
    loadComponent: () => import('./admin/forgejo-health/forgejo-health').then(m => m.ForgejoHealth)
  },
  {
    path: 'account',
    canActivate: [authGuard],
    loadComponent: () => import('./account/account').then(m => m.Account)
  },
  {
    path: 'notification-settings',
    canActivate: [authGuard],
    data: { role: Role.Student },
    loadComponent: () =>
      import('./notifications/notification-settings/notification-settings').then(m => m.NotificationSettings)
  },

  { path: '', loadChildren: () => import('./rosters/roster.routes').then(m => m.rosterRoutes) },
  { path: '', loadChildren: () => import('./assignments/assignment.routes').then(m => m.assignmentRoutes) },

  {
    path: 'forbidden',
    loadComponent: () => import('./errors/forbidden/forbidden').then(m => m.Forbidden)
  },
  {
    path: 'not-found',
    loadComponent: () => import('./errors/not-found/not-found').then(m => m.NotFound)
  },
  {
    path: '**',
    loadComponent: () => import('./errors/not-found/not-found').then(m => m.NotFound)
  }
];
