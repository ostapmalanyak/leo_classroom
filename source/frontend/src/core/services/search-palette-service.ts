import { Injectable, signal } from '@angular/core';

/**
 * The search palette lives in the shell, so the keyboard shortcut works on every page.
 * Whoever wants to open it - the search button in the bar, the field on the courses page -
 * asks this service.
 */
@Injectable({ providedIn: 'root' })
export class SearchPaletteService {
  private readonly state = signal(false);

  /** True while the palette is open. */
  readonly opened = this.state.asReadonly();

  open(): void {
    this.state.set(true);
  }

  close(): void {
    this.state.set(false);
  }

  toggle(): void {
    this.state.update(open => !open);
  }
}
