import { Role } from '../auth/roles';

export interface NavEntry {
  /**
   * Plain text. The application is English-only and has no translation system, so every label in the UI is
   * written out where it is used. These entries once held translation keys for a system that was never
   * built, and read `nav.home` on screen.
   */
  label: string;
  route: string;
  icon: string;
  requiredRole?: Role;
}

export const navEntries: readonly NavEntry[] = [
  { label: 'Home', route: '/', icon: 'home' },
  { label: 'My assignments', route: '/my-assignments', icon: 'assignment', requiredRole: Role.Student },
  { label: 'Notifications', route: '/notification-settings', icon: 'notifications', requiredRole: Role.Student },
  { label: 'Courses', route: '/courses', icon: 'school', requiredRole: Role.Teacher },
  { label: 'Rosters', route: '/rosters', icon: 'group', requiredRole: Role.Teacher },
  { label: 'Teacher CLI', route: '/cli', icon: 'terminal', requiredRole: Role.Teacher },
  { label: 'Audit log', route: '/audit', icon: 'fact_check', requiredRole: Role.Admin },
  { label: 'Forgejo connection', route: '/admin/forgejo', icon: 'cable', requiredRole: Role.Admin },
  { label: 'My account', route: '/account', icon: 'account_circle' }
];

export function visibleNavEntries(entries: readonly NavEntry[], roles: readonly Role[]): NavEntry[] {
  const isAdmin = roles.includes(Role.Admin);

  return entries.filter(entry =>
    entry.requiredRole === undefined || isAdmin || roles.includes(entry.requiredRole));
}

export function defaultRouteForRoles(roles: readonly Role[]): string {
  if (roles.includes(Role.Admin)) {
    return '/home/admin';
  }
  if (roles.includes(Role.Teacher)) {
    return '/home/teacher';
  }

  return '/home/student';
}
