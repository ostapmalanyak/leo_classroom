import { ChangeDetectionStrategy, Component, input, InputSignal, model, ModelSignal, signal, WritableSignal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MarkdownView } from '../markdown-view/markdown-view';

/**
 * A markdown field with a Write / Preview switch. One pane at a time: the editor writes,
 * the preview reads, and the two never squeeze each other.
 */
@Component({
  selector: 'app-markdown-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MarkdownView],
  templateUrl: './markdown-editor.html',
  styleUrl: './markdown-editor.scss'
})
export class MarkdownEditor {
  public readonly label: InputSignal<string> = input('Markdown');
  public readonly value: ModelSignal<string> = model('');
  public readonly placeholder: InputSignal<string> = input('Markdown supported…');

  /** Which pane is on screen. */
  protected readonly mode: WritableSignal<'write' | 'preview'> = signal('write');

  protected show(mode: 'write' | 'preview'): void {
    this.mode.set(mode);
  }
}
