import { Component, input, InputSignal, output, OutputEmitterRef } from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { MatButton } from '@angular/material/button';

@Component({
  selector: 'app-inline-error',
  imports: [MatIcon, MatButton],
  template: `
    <div class="inline-error" role="alert">
      <mat-icon>error_outline</mat-icon>
      <span>{{ message() }}</span>
      @if (retryable()) {
        <button mat-button (click)="retry.emit()">Retry</button>
      }
    </div>
  `,
  styles: `
    .inline-error {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      padding: 1rem;
      color: var(--mat-sys-error, #b3261e);
    }
  `
})
export class InlineError {
  public readonly message: InputSignal<string> = input('Something went wrong');
  public readonly retryable: InputSignal<boolean> = input(false);
  public readonly retry: OutputEmitterRef<void> = output<void>();
}
