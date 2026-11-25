import { existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, parse } from 'node:path';
import type { Descriptor, SymbolKind } from './descriptor';

export const OVERRIDE_FILE = '.codeglyph.json';

/** icon null hides the icon. container and kind are optional. */
export interface Override { name: string; container?: string; kind?: SymbolKind; icon: string | null; }

type Target = Pick<Descriptor, 'name' | 'container' | 'kind'>;

const KINDS = new Set(['Type', 'Method', 'Property']);

/** Reads the file text. Bad entries are skipped, so a typo never breaks the editor. */
export function parseOverrides(text: string): Override[] {
  let root: unknown;
  try { root = JSON.parse(text); } catch { return []; }
  const list = (root as { overrides?: unknown })?.overrides;
  if (!Array.isArray(list)) return [];
  const out: Override[] = [];
  for (const e of list) {
    if (!e || typeof e !== 'object') continue;
    const o = e as Record<string, unknown>;
    if (typeof o.name !== 'string' || !o.name) continue;
    const icon = typeof o.icon === 'string' && !/^(none|null)$/i.test(o.icon.trim()) ? o.icon.trim() : null;
    if (o.icon !== null && typeof o.icon !== 'string') continue;
    out.push({
      name: o.name,
      ...(typeof o.container === 'string' ? { container: o.container } : {}),
      ...(typeof o.kind === 'string' && KINDS.has(o.kind) ? { kind: o.kind as SymbolKind } : {}),
      icon,
    });
  }
  return out;
}

const matches = (o: Override, d: Target) =>
  o.name === d.name && (o.container === undefined || o.container === d.container) && (o.kind === undefined || o.kind === d.kind);

const specificity = (o: Override) => (o.container === undefined ? 0 : 1) + (o.kind === undefined ? 0 : 1);

/** The most specific match wins. Later entries win a tie. */
export function matchOverride(list: Override[], d: Target): Override | undefined {
  let best: Override | undefined;
  for (const o of list) if (matches(o, d) && (!best || specificity(o) >= specificity(best))) best = o;
  return best;
}

interface Loaded { file: string | null; mtime: number; list: Override[]; checked: number; }

/** Finds the nearest .codeglyph.json above a source file and keeps its entries. */
export class OverrideStore {
  private readonly byDir = new Map<string, Loaded>();

  constructor(private readonly now: () => number = Date.now, private readonly recheckMs = 3000) {}

  find(startPath: string): string | null {
    let dir = dirname(startPath);
    const root = parse(dir).root;
    for (;;) {
      const candidate = join(dir, OVERRIDE_FILE);
      if (existsSync(candidate)) return candidate;
      if (dir === root || dirname(dir) === dir) return null;
      dir = dirname(dir);
    }
  }

  private load(startPath: string): Loaded {
    const dir = dirname(startPath);
    const cached = this.byDir.get(dir);
    const now = this.now();
    if (cached && now - cached.checked < this.recheckMs) return cached;

    const file = this.find(startPath);
    let mtime = 0;
    try { if (file) mtime = statSync(file).mtimeMs; } catch { /* The file may be gone. */ }
    if (cached && cached.file === file && cached.mtime === mtime) {
      cached.checked = now;
      return cached;
    }
    let list: Override[] = [];
    try { if (file) list = parseOverrides(readFileSync(file, 'utf8')); } catch { /* Read errors give no overrides. */ }
    const loaded = { file, mtime, list, checked: now };
    this.byDir.set(dir, loaded);
    return loaded;
  }

  /** undefined: no override. icon null: hidden. */
  lookup(d: Descriptor): { icon: string | null } | undefined {
    if (!d.path) return undefined;
    const o = matchOverride(this.load(d.path).list, d);
    return o ? { icon: o.icon } : undefined;
  }

  clear(): void { this.byDir.clear(); }

  /** Sets the override for one symbol. Writes the file and creates it when needed. */
  set(file: string, d: Target, icon: string | null): void {
    const list = this.read(file).filter(o => !(o.name === d.name && o.container === (d.container || undefined) && o.kind === d.kind));
    list.push({ name: d.name, ...(d.container ? { container: d.container } : {}), kind: d.kind, icon });
    this.write(file, list);
  }

  /** Removes the override for one symbol. Returns false when there was none. */
  remove(file: string, d: Target): boolean {
    const list = this.read(file);
    const kept = list.filter(o => !(o.name === d.name && o.container === (d.container || undefined) && o.kind === d.kind));
    if (kept.length === list.length) return false;
    this.write(file, kept);
    return true;
  }

  private read(file: string): Override[] {
    try { return existsSync(file) ? parseOverrides(readFileSync(file, 'utf8')) : []; } catch { return []; }
  }

  private write(file: string, list: Override[]): void {
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, JSON.stringify({ overrides: list }, null, 2) + '\n');
    this.clear();
  }
}
