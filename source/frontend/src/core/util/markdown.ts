import { marked } from 'marked';
import DOMPurify from 'dompurify';

export function renderMarkdown(source: string | null | undefined): string {
  if (source === null || source === undefined || source === '') {
    return '';
  }
  const html = marked.parse(source, { async: false }) as string;

  return DOMPurify.sanitize(html);
}
