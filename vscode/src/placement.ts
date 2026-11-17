export interface Band { start: number; end: number; } // inclusive line numbers

/** Largest run of lines inside [start, end] that no child symbol covers. */
export function largestFree(start: number, end: number, children: Band[]): Band | undefined {
  const used = new Array<boolean>(end - start + 1).fill(false);
  for (const c of children) {
    for (let l = Math.max(c.start, start); l <= Math.min(c.end, end); l++) used[l - start] = true;
  }
  let best: Band | undefined;
  let runStart = -1;
  for (let i = 0; i <= used.length; i++) {
    if (i < used.length && !used[i]) { if (runStart < 0) runStart = i; continue; }
    if (runStart >= 0) {
      const b = { start: start + runStart, end: start + i - 1 };
      if (!best || b.end - b.start > best.end - best.start) best = b;
      runStart = -1;
    }
  }
  return best;
}

export type Vertical = 'top' | 'center' | 'bottom';

/** Icon height in lines and its first line, or undefined when the band is too short. */
export function layout(band: Band, minLines: number, maxLines: number, vertical: Vertical): { startLine: number; sizeLines: number } | undefined {
  const height = band.end - band.start + 1;
  const size = Math.min(height, maxLines);
  if (size < minLines) return undefined;
  const room = height - size;
  const offset = vertical === 'top' ? 0 : vertical === 'bottom' ? room : Math.floor(room / 2);
  return { startLine: band.start + offset, sizeLines: size };
}

/** Longest line, with tabs expanded, in characters. */
export function maxLineWidth(lines: string[], from: number, to: number, tabSize: number): number {
  let max = 0;
  for (let l = from; l <= Math.min(to, lines.length - 1); l++) {
    let w = 0;
    for (const ch of lines[l]) w += ch === '\t' ? tabSize - (w % tabSize) : 1;
    if (w > max) max = w;
  }
  return max;
}

export interface ColumnOptions { placement: 'fixedColumn' | 'afterCode'; column: number; overlapCode: boolean; }

/** Column where the icon starts, or undefined when it must not be shown. */
export function columnFor(maxWidth: number, sizeLines: number, o: ColumnOptions): number | undefined {
  const margin = 3;
  if (o.placement === 'afterCode') return maxWidth + margin;
  if (!o.overlapCode && maxWidth + margin > o.column) return undefined;
  void sizeLines;
  return o.column;
}
