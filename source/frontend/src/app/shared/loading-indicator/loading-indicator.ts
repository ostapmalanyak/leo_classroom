import { Component, input, InputSignal } from '@angular/core';
import { MatProgressBar } from '@angular/material/progress-bar';

@Component({
  selector: 'app-loading-indicator',
  imports: [MatProgressBar],
  template: `
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" [attr.aria-label]="label()"></mat-progress-bar>
    }
  `
})
export class LoadingIndicator {
  public readonly loading: InputSignal<boolean> = input.required<boolean>();
  public readonly label: InputSignal<string> = input('Loading');
}
