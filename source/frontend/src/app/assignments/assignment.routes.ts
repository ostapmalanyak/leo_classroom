import { Routes } from '@angular/router';
import { AssignmentEdit } from './assignment-edit/assignment-edit';
import { AssignmentStudents } from './assignment-students/assignment-students';
import { AssignmentAnalytics } from './assignment-analytics/assignment-analytics';
import { AssignmentDownload } from './assignment-download/assignment-download';
import { StudentAssignments } from '../student/student-assignments/student-assignments';
import { StudentAssignmentDetail } from '../student/assignment-detail/assignment-detail';
import { authGuard } from '../../core/auth/auth-guard';
import { Role } from '../../core/auth/roles';

export const assignmentRoutes: Routes = [
  {
    path: 'courses/:courseId/assignments/new',
    component: AssignmentEdit,
    canActivate: [authGuard],
    data: { role: Role.Teacher }
  },
  {
    path: 'assignments/:id/edit',
    component: AssignmentEdit,
    canActivate: [authGuard],
    data: { role: Role.Teacher }
  },
  {
    path: 'assignments/:id/students',
    component: AssignmentStudents,
    canActivate: [authGuard],
    data: { role: Role.Teacher }
  },
  {
    path: 'assignments/:id/acceptances/:acceptanceId/analytics',
    component: AssignmentAnalytics,
    canActivate: [authGuard],
    data: { role: Role.Teacher }
  },
  {
    path: 'assignments/:id/download',
    component: AssignmentDownload,
    canActivate: [authGuard],
    data: { role: Role.Teacher }
  },
  { path: 'my-assignments', component: StudentAssignments, canActivate: [authGuard] },
  { path: 'my-assignments/:id', component: StudentAssignmentDetail, canActivate: [authGuard] }
];
