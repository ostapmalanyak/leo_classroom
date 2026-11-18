import { Component, computed, inject, input } from '@angular/core';
import { Duration, Instant } from '@js-joda/core';
import { MatIcon } from '@angular/material/icon';
import { DeadlineKind } from '../../../core/services/assignment-service';
import { NowService } from '../../../core/services/now-service';
import { dueLabel, relativeDue } from '../../../core/util/assignment-state';

/** How far before the deadline the run-up starts. */
const RUN_UP_DAYS = 14;
/** How long a soft deadline keeps accepting pushes after the marker. */
const LATE_DAYS = 7;

/**
 * The one picture of a deadline: a clean track with a round marker. A soft deadline keeps the
 * track running after the marker (fading out, and filling with the warning tone once passed);
 * a hard deadline ends in the marker and turns grey afterwards. The icon says which one it is,
 * so the meaning never depends on colour alone.
 */
@Component({
  selector: 'app-deadline-timeline',
  imports: [MatIcon],
  template: `
    @if (hasDeadline()) {
      <div class="timeline" [class.timeline--hard]="isHard()" [class.timeline--passed]="passed()"
           role="img" [attr.aria-label]="label()">
        <div class="timeline__track">
          <span class="timeline__elapsed" [style.flex-basis.%]="elapsed()"></span>
          <span class="timeline__remaining" [style.flex-basis.%]="remaining()"></span>
          @if (!isHard()) {
            <span class="timeline__tail" [style.flex-basis.%]="tail()"></span>
          }
          <span class="timeline__marker" [style.inset-inline-start.%]="elapsed()">
            <mat-icon aria-hidden="true">{{ icon() }}</mat-icon>
          </span>
        </div>

        <p class="timeline__meta">
          <mat-icon class="timeline__meta-icon" aria-hidden="true">{{ icon() }}</mat-icon>
          <span class="timeline__stamp">{{ stamp() }}</span>
          <span class="timeline__left" [class]="'timeline__left timeline__left--' + tone()">{{ left() }}</span>
        </p>
      </div>
    }
  `,
  styleUrl: './deadline-timeline.scss'
})
export class DeadlineTimeline {
  private readonly clock = inject(NowService);

  public readonly deadline = input<Instant | null>(null);
  public readonly kind = input<DeadlineKind>(DeadlineKind.None);
  /** Optional start of the run-up; defaults to two weeks before the deadline. */
  public readonly start = input<Instant | null>(null);

  protected readonly hasDeadline = computed(
    () => this.deadline() !== null && this.kind() !== DeadlineKind.None);
  protected readonly isHard = computed(() => this.kind() === DeadlineKind.Hard);
  protected readonly icon = computed(() => (this.isHard() ? 'lock' : 'hourglass_bottom'));

  protected readonly stamp = computed(() => {
    const deadline = this.deadline();

    return deadline === null ? '' : dueLabel(deadline);
  });

  protected readonly passed = computed(() => {
    const deadline = this.deadline();

    return deadline !== null && this.clock.now().isAfter(deadline);
  });

  /** Share of the whole track that the run-up takes; the rest is the soft tail. */
  private readonly runUpShare = computed(() => {
    if (this.isHard()) {
      return 100;
    }

    return (RUN_UP_DAYS / (RUN_UP_DAYS + LATE_DAYS)) * 100;
  });

  /** How much of the run-up is behind us, in percent of the full track. */
  protected readonly elapsed = computed(() => {
    const deadline = this.deadline();
    if (deadline === null) {
      return 0;
    }
    const start = (this.start() ?? deadline.minus(Duration.ofDays(RUN_UP_DAYS))).toEpochMilli();
    const end = deadline.toEpochMilli();
    const span = Math.max(1, end - start);
    const done = ((this.clock.now().toEpochMilli() - start) / span) * 100;

    return Math.min(this.runUpShare(), Math.max(0, (done / 100) * this.runUpShare()));
  });

  /** The neutral part between "now" and the marker. */
  protected readonly remaining = computed(() => this.runUpShare() - this.elapsed());

  /** The soft tail behind the marker; it is fully there until the deadline passes. */
  protected readonly tail = computed(() => 100 - this.runUpShare());

  protected readonly left = computed(() => {
    const deadline = this.deadline();
    if (deadline === null) {
      return '';
    }
    const relative = relativeDue(deadline, this.clock.now());
    if (!this.passed()) {
      return relative;
    }

    return this.isHard() ? 'Closed' : `${relative} late`;
  });

  protected readonly tone = computed(() => {
    if (this.passed()) {
      return this.isHard() ? 'closed' : 'late';
    }

    return 'open';
  });

  protected readonly label = computed(() => {
    if (this.isHard()) {
      return this.passed() ? 'Hard deadline, closed' : `Hard deadline, ${this.left()}`;
    }

    return this.passed()
      ? `Soft deadline, late pushes accepted, ${this.left()}`
      : `Soft deadline, late pushes accepted, ${this.left()}`;
  });
}
