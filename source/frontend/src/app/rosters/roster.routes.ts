import { Routes } from '@angular/router';
import { RosterList } from './roster-list/roster-list';
import { RosterDetail } from './roster-detail/roster-detail';
import { CourseTeacherManagement } from '../course-teachers/course-teachers';
import { authGuard } from '../../core/auth/auth-guard';

export const rosterRoutes: Routes = [
  { path: 'rosters', component: RosterList, canActivate: [authGuard] },
  { path: 'rosters/:id', component: RosterDetail, canActivate: [authGuard] },
  { path: 'courses/:id/teachers', component: CourseTeacherManagement, canActivate: [authGuard] }
];
