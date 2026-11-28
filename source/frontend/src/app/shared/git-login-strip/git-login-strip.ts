import { Component, inject, input, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIcon } from '@angular/material/icon';
import { GitSetupService } from '../../../core/services/git-setup-service';

/**
 * One muted line on the home page while git still asks for a password.
 *
 * It links to the page that owns the step instead of repeating it, and it disappears for good once the
 * backend has seen a successful git operation.
 */
@Component({
  selector: 'app-git-login-strip',
  imports: [RouterLink, MatIcon],
  template: `
    @if (visible()) {
      <a class="strip" [routerLink]="route()">
        <mat-icon aria-hidden="true">key</mat-icon>
        <span>Git still asks for your password? Fix it once.</span>
      </a>
    }
  `,
  styles: [`
    :host {
      display: block;
    }

    .strip {
      display: inline-flex;
      align-items: center;
      gap: var(--space-2);
      padding-block: var(--space-2);
      color: var(--color-text-muted);
      font-size: var(--font-size-sm);
      text-decoration: none;
    }

    .strip:hover {
      color: var(--color-text-strong);
      text-decoration: underline;
      text-underline-offset: 0.2em;
    }

    .strip:focus-visible {
      outline: 0.125rem solid var(--color-accent);
      outline-offset: 0.125rem;
      border-radius: var(--radius);
    }

    .strip mat-icon {
      font-size: 1rem;
      inline-size: 1rem;
      block-size: 1rem;
      color: var(--color-text-subtle);
    }
  `]
})
export class GitLoginStrip implements OnInit {
  /** Where the step lives for this audience: an assignment page, or the teacher CLI page. */
  public readonly route = input<string | readonly string[]>('/my-assignments');

  private readonly service = inject(GitSetupService);

  protected readonly visible = signal(false);

  public ngOnInit(): void {
    void this.load();
  }

  private async load(): Promise<void> {
    const setup = await this.service.get();
    this.visible.set(setup !== null && !setup.authorized);
  }
}
