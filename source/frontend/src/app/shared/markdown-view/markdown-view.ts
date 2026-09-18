import { ChangeDetectionStrategy, Component, computed, input, InputSignal, Signal } from '@angular/core';
import { renderMarkdown } from '../../../core/util/markdown';

@Component({
  selector: 'app-markdown-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<div class="markdown-body" [innerHTML]="html()"></div>',
  styleUrl: './markdown-view.scss'
})
export class MarkdownView {
  public readonly source: InputSignal<string | null | undefined> = input<string | null | undefined>(null);

  protected readonly html: Signal<string> = computed(() => renderMarkdown(this.source()));
}
