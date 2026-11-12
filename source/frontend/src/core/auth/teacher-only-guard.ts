import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { SessionService } from './session-service';

export const teacherOnlyGuard: CanActivateFn = () => {
  const session = inject(SessionService);
  const router = inject(Router);
  if (session.isTeacher()) {
    return true;
  }

  return router.createUrlTree(['/forbidden']);
};
