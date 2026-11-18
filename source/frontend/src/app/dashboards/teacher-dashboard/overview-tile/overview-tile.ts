import { Component, input } from '@angular/core';

/**
 * One number of the overview: how many courses, how many assignments are still open, how
 * many close today. The tile itself is the shared one of the student home.
 */
@Component({
  selector: 'app-overview-tile',
  template: `
    <div class="stat"
         [class.stat--warning]="warning() && value() > 0"
         [class.stat--danger]="danger() && value() > 0"
         [attr.title]="hint() ?? null">
      <span class="stat__value">{{ value() }}</span>
      <span class="stat__label">{{ label() }}</span>
    </div>
  `
})
export class OverviewTile {
  readonly value = input.required<number>();
  readonly label = input.required<string>();
  /** A number that deserves the warning colour. */
  readonly warning = input(false);
  /** A number that was missed and deserves the danger colour. */
  readonly danger = input(false);
  /** What the number means, for the tooltip. */
  readonly hint = input<string | null>(null);
}
