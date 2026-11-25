import type { Sym } from './symbols';

export interface MapEntry {
  uri: string;
  sym: Sym;
  icon: string;
  children: MapEntry[];
}

/** Nests the symbols that have an icon by line range. A symbol with no icon is left out. */
export function buildEntries(uri: string, syms: Sym[], iconFor: (s: Sym) => string | null): MapEntry[] {
  const roots: MapEntry[] = [];
  const stack: MapEntry[] = [];
  const sorted = [...syms].sort((a, b) => a.start - b.start || b.end - a.end);
  for (const sym of sorted) {
    const icon = iconFor(sym);
    if (!icon) continue;
    const entry: MapEntry = { uri, sym, icon, children: [] };
    while (stack.length && stack[stack.length - 1].sym.end < sym.start) stack.pop();
    (stack.length ? stack[stack.length - 1].children : roots).push(entry);
    stack.push(entry);
  }
  return roots;
}
