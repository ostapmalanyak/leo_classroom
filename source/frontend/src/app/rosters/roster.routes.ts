import { Routes } from '@angular/router';
import { RosterList } from './roster-list/roster-list';
import { RosterDetail } from './roster-detail/roster-detail';
import { CourseTeacherManagement } from '../course-teachers/course-teachers';
import { authGuard } from '../../core/auth/auth-guard';
import { Role } from '../../core/auth/roles';

export const rosterRoutes: Routes = [
  { path: 'rosters', component: RosterList, canActivate: [authGuard], data: { role: Role.Teacher } },
  { path: 'rosters/:id', component: RosterDetail, canActivate: [authGuard], data: { role: Role.Teacher } },
  {
    path: 'courses/:id/teachers',
    component: CourseTeacherManagement,
    canActivate: [authGuard],
    data: { role: Role.Teacher }
  }
];
