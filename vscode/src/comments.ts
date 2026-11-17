import type { Descriptor, SymbolKind } from './descriptor';

const MARKERS = /^(\/\/\/?|\/\*\*?|\*\/|\*(?!\w*\()|#(?!\s*(region|endregion|if|else|elif|endif|pragma|include|define|undef|nullable|import|!)\b)|--|<!--|%|;;|''')/;
const SKIP_UP = /^(@\w|\[[A-Za-z_][\w.,\s()"'=:]*\]\s*$|#\[)/;
const TRIPLE = /^[rRuUbBfF]{0,2}("""|''')/;

const strip = (line: string) =>
  line.trim()
    .replace(/^(\/\/\/?|\/\*\*?|\*\/|\*|#|--|<!--|%|;;)\s?/, '')
    .replace(/\*\/\s*$/, '')
    .replace(/-->\s*$/, '')
    .replace(/<\/?(summary|remarks|returns|param|c|para|see)[^>]*>/gi, ' ')
    .trim();

/**
 * Collects documentation text for a symbol in any language: the comment block above it,
 * and a triple-quote docstring right after the signature (Python style).
 * Returns text only. It never returns code.
 */
export function extractComments(lines: string[], startLine: number, signatureLine: number, maxChars = 600): string {
  const parts: string[] = [];

  let i = startLine - 1;
  // Decorators and attributes can sit between the comment and the symbol.
  while (i >= 0 && SKIP_UP.test(lines[i].trim())) i--;
  const above: string[] = [];
  while (i >= 0) {
    const t = lines[i].trim();
    if (!t || !MARKERS.test(t)) break;
    above.unshift(strip(t));
    i--;
    if (above.length >= 30) break;
  }
  parts.push(...above);

  for (let j = signatureLine; j <= Math.min(lines.length - 1, signatureLine + 6); j++) {
    const m = TRIPLE.exec(lines[j].trim());
    if (!m) continue;
    const quote = m[1];
    const doc: string[] = [];
    let first = lines[j].trim().replace(TRIPLE, '');
    for (let k = j; k <= Math.min(lines.length - 1, j + 20); k++) {
      const text = k === j ? first : lines[k].trim();
      const end = text.indexOf(quote);
      if (end >= 0) { doc.push(text.slice(0, end)); break; }
      doc.push(text);
    }
    parts.push(...doc);
    break;
  }

  const text = parts.map(p => p.trim()).filter(Boolean).join(' ').replace(/\s+/g, ' ').trim();
  return text.length > maxChars ? text.slice(0, maxChars) : text;
}

export function isEligibleName(name: string, kind: SymbolKind): boolean {
  if (!name || !name.trim()) return false;
  if (/^(ToString|Equals|GetHashCode|Dispose|DisposeAsync|Main|Clone|CompareTo|toString|toJSON|valueOf|constructor|__init__|__str__|__repr__|__eq__|__hash__|__del__|__enter__|__exit__|init|deinit)$/.test(name)) return false;
  if (kind === 'Method' && /^(get|set)$/.test(name)) return false;
  return true;
}

export type { Descriptor };
