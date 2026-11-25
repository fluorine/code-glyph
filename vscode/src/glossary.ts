import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import type { Descriptor } from './descriptor';
import { conceptWords } from './vocabulary';

export interface Hint { word: string; icon: string; }

const MIN_COUNT = 2;
const MIN_SHARE = 0.6;
const MAX_WORDS = 5000;

/**
 * Remembers which icon the model chose for each concept word.
 * A word seen twice or more with one dominant icon becomes a hint, so equal concepts get equal icons.
 */
export class Glossary {
  private words = new Map<string, Record<string, number>>();
  private timer: NodeJS.Timeout | undefined;

  constructor(private readonly path?: string) {
    if (path && existsSync(path)) {
      try {
        const raw = JSON.parse(readFileSync(path, 'utf8')) as Record<string, Record<string, number>>;
        for (const [w, counts] of Object.entries(raw)) this.words.set(w, counts);
      } catch { /* A bad file only costs the hints. */ }
    }
  }

  get size(): number { return this.words.size; }

  learn(d: Descriptor, icon: string): void {
    for (const w of conceptWords(d.name)) {
      const counts = this.words.get(w) ?? {};
      counts[icon] = (counts[icon] ?? 0) + 1;
      this.words.set(w, counts);
    }
    if (this.words.size > MAX_WORDS) this.trim();
    this.saveSoon();
  }

  private trim(): void {
    const total = (c: Record<string, number>) => Object.values(c).reduce((a, b) => a + b, 0);
    const weakest = [...this.words].sort((a, b) => total(a[1]) - total(b[1])).slice(0, this.words.size - MAX_WORDS);
    for (const [w] of weakest) this.words.delete(w);
  }

  /** Hints for the words of these symbols, strongest first, at most max. */
  hints(descriptors: Descriptor[], max = 30): Hint[] {
    const out: Array<Hint & { total: number }> = [];
    const seen = new Set<string>();
    for (const d of descriptors) {
      for (const w of conceptWords(d.name)) {
        if (seen.has(w)) continue;
        seen.add(w);
        const counts = this.words.get(w);
        if (!counts) continue;
        const total = Object.values(counts).reduce((a, b) => a + b, 0);
        const [icon, n] = Object.entries(counts).sort((a, b) => b[1] - a[1])[0];
        if (total >= MIN_COUNT && n / total >= MIN_SHARE) out.push({ word: w, icon, total });
      }
    }
    return out.sort((a, b) => b.total - a.total).slice(0, max).map(({ word, icon }) => ({ word, icon }));
  }

  clear(): void { this.words.clear(); this.save(); }

  save(): void {
    if (!this.path) return;
    try {
      mkdirSync(dirname(this.path), { recursive: true });
      writeFileSync(this.path, JSON.stringify(Object.fromEntries(this.words)));
    } catch { /* Disk errors only cost the hints. */ }
  }

  saveSoon(): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.save(), 2000);
    this.timer.unref?.();
  }
}
