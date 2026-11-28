import { Injectable, OnDestroy, signal, Signal } from '@angular/core';
import { Instant } from '@js-joda/core';

/**
 * The one clock the assignment UI reads. Relative times ("in 5 h") are wrong the moment they are
 * rendered, so every screen listens to a tick instead of sampling `Instant.now()` once.
 */
@Injectable({ providedIn: 'root' })
export class NowService implements OnDestroy {
  private readonly current = signal(Instant.now());

  public readonly now: Signal<Instant> = this.current.asReadonly();

  private readonly timer = setInterval(() => this.current.set(Instant.now()), 1_000);

  public ngOnDestroy(): void {
    clearInterval(this.timer);
  }
}
