import { Component, computed, inject, input } from '@angular/core';
import { ChronoUnit, Duration, Instant } from '@js-joda/core';
import { DeadlineKind } from '../../../core/services/assignment-service';
import { NowService } from '../../../core/services/now-service';
import { calendarDaysBetween, dueLabel } from '../../../core/util/assignment-state';

/** How far before the deadline the tracked stretch starts when no assign time is known. */
const RUN_UP_DAYS = 14;

/**
 * One picture of a deadline: a thin track from "assigned" to the deadline with a flat fill.
 *
 * A hard deadline stops at the end of the track. A soft one keeps working after it, so the fill
 * turns the warning tone and fades out instead of pretending there is a wall. Everything on
 * screen is derived from the deadline, its kind and the clock, so the picture cannot disagree
 * with the label next to it.
 */
@Component({
  selector: 'app-deadline-timeline',
  template: `
    @if (hasDeadline()) {
      <div class="deadline"
           [class.deadline--hard]="isHard()"
           [class.deadline--passed]="passed()"
           [class.deadline--late]="isLate()"
           role="progressbar"
           aria-valuemin="0"
           aria-valuemax="100"
           [attr.aria-valuenow]="elapsed()"
           [attr.aria-valuetext]="label()">
        <div class="deadline__top">
          <span class="deadline__date">{{ stamp() }}</span>
          <span class="deadline__left">{{ left() }}</span>
        </div>

        <div class="deadline__track">
          <span class="deadline__fill" [style.inline-size.%]="fill()"></span>
        </div>

        <div class="deadline__bottom">
          <span class="deadline__assigned">Assigned</span>
          <span class="deadline__kind">{{ kindLabel() }}</span>
        </div>

        @if (isLate()) {
          <p class="deadline__note">You can still submit</p>
        }
      </div>
    }
  `,
  styleUrl: './deadline-timeline.scss'
})
export class DeadlineTimeline {
  private readonly clock = inject(NowService);

  public readonly deadline = input<Instant | null>(null);
  public readonly kind = input<DeadlineKind>(DeadlineKind.None);
  /** Optional start of the tracked stretch; defaults to two weeks before the deadline. */
  public readonly start = input<Instant | null>(null);

  protected readonly hasDeadline = computed(
    () => this.deadline() !== null && this.kind() !== DeadlineKind.None);
  protected readonly isHard = computed(() => this.kind() === DeadlineKind.Hard);

  protected readonly stamp = computed(() => {
    const deadline = this.deadline();

    return deadline === null ? '' : dueLabel(deadline);
  });

  protected readonly passed = computed(() => {
    const deadline = this.deadline();

    return deadline !== null && this.clock.now().isAfter(deadline);
  });

  /** Only a soft deadline can be late: a passed hard deadline is closed, not late. */
  protected readonly isLate = computed(() => this.passed() && !this.isHard());

  protected readonly kindLabel = computed(() => (this.isHard() ? 'Hard deadline' : 'Soft deadline'));

  /** How much of the assigned stretch is behind us, in percent. */
  protected readonly elapsed = computed(() => {
    const deadline = this.deadline();
    if (deadline === null) {
      return 0;
    }
    const start = (this.start() ?? deadline.minus(Duration.ofDays(RUN_UP_DAYS))).toEpochMilli();
    const span = Math.max(1, deadline.toEpochMilli() - start);
    const done = ((this.clock.now().toEpochMilli() - start) / span) * 100;

    return Math.min(100, Math.max(0, Math.round(done)));
  });

  /** A passed deadline fills the whole track; nothing is drawn after it. */
  protected readonly fill = computed(() => (this.passed() ? 100 : this.elapsed()));

  protected readonly left = computed(() => {
    if (this.deadline() === null) {
      return '';
    }
    if (this.passed()) {
      return this.isHard() ? 'Closed' : `${this.amount()} late`;
    }

    return `${this.amount()} left`;
  });

  protected readonly label = computed(() => {
    const kind = this.isHard() ? 'hard' : 'soft';
    if (this.passed()) {
      return this.isHard()
        ? 'Hard deadline closed'
        : `${this.amount()} late, soft deadline, you can still submit`;
    }

    return `${this.amount()} left until the ${kind} deadline`;
  });

  /** "8 days" / "5 hours" / "40 minutes", counted in whichever unit a person would use. */
  private amount(): string {
    const deadline = this.deadline();
    if (deadline === null) {
      return '';
    }
    const now = this.clock.now();
    const seconds = Math.abs(now.until(deadline, ChronoUnit.SECONDS));
    const minutes = Math.abs(now.until(deadline, ChronoUnit.MINUTES));

    if (seconds < 60) {
      return this.unit(Math.max(1, seconds), 'second');
    }
    if (minutes < 60) {
      return this.unit(Math.max(1, minutes), 'minute');
    }
    if (minutes < 24 * 60) {
      return this.unit(Math.floor(minutes / 60), 'hour');
    }

    const days = Math.abs(calendarDaysBetween(now, deadline));

    return this.unit(days, 'day');
  }

  private unit(value: number, unit: string): string {
    return `${value} ${value === 1 ? unit : `${unit}s`}`;
  }
}
