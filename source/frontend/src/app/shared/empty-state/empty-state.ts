import { Component, input, InputSignal } from '@angular/core';
import { MatIcon } from '@angular/material/icon';

@Component({
  selector: 'app-empty-state',
  imports: [MatIcon],
  template: `
    <div class="empty-state">
      <mat-icon>{{ icon() }}</mat-icon>
      <p>{{ message() }}</p>
    </div>
  `,
  styles: `
    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.5rem;
      padding: 2rem;
      color: rgba(0, 0, 0, 0.6);
      text-align: center;
    }
    mat-icon {
      width: 2.5rem;
      height: 2.5rem;
      font-size: 2.5rem;
    }
  `
})
export class EmptyState {
  public readonly message: InputSignal<string> = input('Nothing here yet');
  public readonly icon: InputSignal<string> = input('inbox');
}
