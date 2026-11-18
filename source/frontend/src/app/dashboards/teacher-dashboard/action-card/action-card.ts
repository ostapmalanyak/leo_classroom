import { Component, input, output } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { RouterLink } from '@angular/router';

/** One way into an action - the course an assignment is created in, for example. */
export interface ActionChoice {
  label: string;
  route: readonly (string | number)[];
}

/**
 * A large button card: an icon, a label and one line of explanation. It is either a link
 * to a page or a button that opens a choice, so the whole card is one big target.
 */
@Component({
  selector: 'app-action-card',
  imports: [MatIcon, MatMenu, MatMenuItem, MatMenuTrigger, NgTemplateOutlet, RouterLink],
  templateUrl: './action-card.html',
  styleUrl: './action-card.scss'
})
export class ActionCard {
  readonly icon = input.required<string>();
  readonly label = input.required<string>();
  readonly description = input.required<string>();
  /** Where the card leads. Without it the card opens the choices instead. */
  readonly route = input<readonly (string | number)[] | null>(null);
  /** The ways into the action, when the card is a button with a menu. */
  readonly choices = input<ActionChoice[]>([]);
  /** The one card that matters most gets the accent. */
  readonly primary = input(false);

  readonly chosen = output<ActionChoice>();
  /** The card opens a dialog of its own instead of a menu. */
  readonly picked = output<void>();
}
