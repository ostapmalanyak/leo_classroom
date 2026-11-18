import { Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIcon } from '@angular/material/icon';

/**
 * The key carries a version on purpose: bumping it makes the notice come back for every
 * student, which is what we want once the git login problem is fixed or the text changes.
 */
const HIDDEN_KEY = 'git-credentials-notice-v1-hidden';

/**
 * A temporary notice about the git credentials, shown on the home page until the problem
 * is gone. The whole feature lives in this component: deleting the component and the one
 * line in the home page removes it.
 */
@Component({
  selector: 'app-git-credentials-notice',
  imports: [RouterLink, MatIcon],
  templateUrl: './git-credentials-notice.html',
  styleUrl: './git-credentials-notice.scss'
})
export class GitCredentialsNotice {
  /** True once the student asked for the banner to go away. */
  protected readonly hidden = signal(this.readHidden());

  /** Hides the banner, but keeps a small chip so it can come back. */
  protected hide(): void {
    this.hidden.set(true);
    this.writeHidden(true);
  }

  /** Brings the banner back, also for the next visit. */
  protected show(): void {
    this.hidden.set(false);
    this.writeHidden(false);
  }

  private readHidden(): boolean {
    try {
      return localStorage.getItem(HIDDEN_KEY) === 'true';
    } catch {
      // Without storage the notice simply shows every time, which is the safe direction.
      return false;
    }
  }

  private writeHidden(hidden: boolean): void {
    try {
      localStorage.setItem(HIDDEN_KEY, hidden ? 'true' : 'false');
    } catch {
      // Private mode or a blocked storage: the banner stays as it is for this visit.
    }
  }
}
