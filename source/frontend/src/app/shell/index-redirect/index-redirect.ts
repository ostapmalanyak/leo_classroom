import { Component, inject, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { SessionService } from '../../../core/auth/session-service';
import { MeService } from '../../../core/services/me-service';
import { defaultRouteForRoles } from '../../../core/util/navigation';

@Component({
  selector: 'app-index-redirect',
  template: ''
})
export class IndexRedirect implements OnInit {
  private readonly session = inject(SessionService);
  private readonly me = inject(MeService);
  private readonly router = inject(Router);

  public async ngOnInit(): Promise<void> {
    await this.me.load();
    await this.router.navigateByUrl(defaultRouteForRoles(this.session.roles()), { replaceUrl: true });
  }
}
