import { ChangeDetectionStrategy, Component, input, InputSignal, model, ModelSignal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MarkdownView } from '../markdown-view/markdown-view';

@Component({
  selector: 'app-markdown-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatFormField, MatLabel, MatInput, MarkdownView],
  templateUrl: './markdown-editor.html',
  styleUrl: './markdown-editor.scss'
})
export class MarkdownEditor {
  public readonly label: InputSignal<string> = input('Markdown');
  public readonly value: ModelSignal<string> = model('');
}
