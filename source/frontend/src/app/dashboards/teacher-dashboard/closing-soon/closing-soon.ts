import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Instant } from '@js-joda/core';
import { ChronoUnit } from '@js-joda/core';
import { NowService } from '../../../../core/services/now-service';
import { DueUrgency, dueUrgency } from '../../../../core/util/assignment-state';

/** An assignment that is still open, with how many of its students have taken it. */
export interface ClosingAssignment {
  id: number;
  title: string;
  courseTitle: string;
  accepted: number;
  total: number;
  deadline: Instant;
}

/**
 * The assignments that close soonest. The time left is short ("22 hours", "1 hour 40 minutes") and
 * the pill wears the urgency colour of the deadline.
 */
@Component({
  selector: 'app-closing-soon',
  imports: [RouterLink],
  templateUrl: './closing-soon.html',
  styleUrl: './closing-soon.scss'
})
export class ClosingSoonList {
  private readonly clock = inject(NowService);

  readonly items = input.required<ClosingAssignment[]>();

  /** The rows with their remaining time, recomputed whenever the clock ticks. */
  protected readonly rows = computed(() => {
    const now = this.clock.now();

    return this.items().map(item => {
      const minutes = now.until(item.deadline, ChronoUnit.MINUTES);
      const urgency: DueUrgency = dueUrgency(item.deadline, now);

      return {
        item,
        minutes,
        urgency,
        // The colour is the shared one, the exclamation marks are not: they are the
        // student's way of saying "this is urgent", a teacher reads the time itself.
        remaining: this.shortRemaining(minutes),
        label: this.closingLabel(minutes)
      };
    });
  });

  /** Where "Open" leads: the students of the assignment, which is what a teacher wants. */
  protected openRoute(id: number): readonly string[] {
    return ['/assignments', String(id), 'students'];
  }

  /** "22 hours", "1 hour 40 minutes", "1 day" - short enough for a pill. */
  private shortRemaining(minutes: number): string {
    if (minutes < 60) {
      return this.unit(Math.max(1, minutes), 'minute');
    }

    if (minutes < 120) {
      const rest = minutes % 60;

      return rest === 0 ? '1 hour' : `1 hour ${this.unit(rest, 'minute')}`;
    }

    if (minutes < 24 * 60) {
      return this.unit(Math.floor(minutes / 60), 'hour');
    }

    const days = Math.round(minutes / (24 * 60));

    return this.unit(days, 'day');
  }

  private unit(value: number, unit: string): string {
    return `${value} ${value === 1 ? unit : `${unit}s`}`;
  }

  /** The same time, spelled out for a screen reader. */
  private closingLabel(minutes: number): string {
    if (minutes < 60) {
      return `Closes in ${Math.max(1, minutes)} minute${minutes === 1 ? '' : 's'}`;
    }

    if (minutes < 120) {
      const rest = minutes % 60;

      return rest === 0
        ? 'Closes in 1 hour'
        : `Closes in 1 hour ${rest} minute${rest === 1 ? '' : 's'}`;
    }

    if (minutes < 24 * 60) {
      const hours = Math.floor(minutes / 60);

      return `Closes in ${hours} hour${hours === 1 ? '' : 's'}`;
    }

    const days = Math.round(minutes / (24 * 60));

    return `Closes in ${days} day${days === 1 ? '' : 's'}`;
  }
}
