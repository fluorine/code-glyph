import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

/** seen is the day number (days since 1970) of the last use. */
export interface Choice { icon: string | null; confidence: number; seen?: number; }

export const MAX_AGE_DAYS = 90;
export const MAX_ENTRIES = 20000;

/** Key to answer. "None" answers are stored too, so they are never asked again. */
export class IconCache {
  private map = new Map<string, Choice>();
  private timer: NodeJS.Timeout | undefined;

  constructor(private readonly path?: string, private readonly now: () => number = Date.now) {
    if (path && existsSync(path)) {
      try {
        const raw = JSON.parse(readFileSync(path, 'utf8')) as Record<string, Choice>;
        for (const [k, v] of Object.entries(raw)) this.map.set(k, { ...v, seen: v.seen ?? this.today() });
      } catch { /* A bad file must never break the editor. */ }
      this.prune();
    }
  }

  private today(): number { return Math.floor(this.now() / 86_400_000); }

  get(key: string): Choice | undefined {
    const c = this.map.get(key);
    if (c && c.seen !== this.today()) {
      c.seen = this.today();
      this.saveSoon();
    }
    return c;
  }

  set(key: string, icon: string | null, confidence: number): void {
    this.map.set(key, { icon, confidence, seen: this.today() });
  }

  get size(): number { return this.map.size; }

  clear(): void { this.map.clear(); this.save(); }

  /** Removes entries not used for MAX_AGE_DAYS, then the oldest ones above MAX_ENTRIES. */
  prune(): void {
    const limit = this.today() - MAX_AGE_DAYS;
    for (const [k, v] of this.map) if ((v.seen ?? 0) < limit) this.map.delete(k);
    if (this.map.size <= MAX_ENTRIES) return;
    const oldest = [...this.map].sort((a, b) => (a[1].seen ?? 0) - (b[1].seen ?? 0));
    for (const [k] of oldest.slice(0, this.map.size - MAX_ENTRIES)) this.map.delete(k);
  }

  save(): void {
    if (!this.path) return;
    try {
      this.prune();
      mkdirSync(dirname(this.path), { recursive: true });
      writeFileSync(this.path, JSON.stringify(Object.fromEntries(this.map)));
    } catch { /* Disk errors only cost the cache. */ }
  }

  saveSoon(): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.save(), 2000);
    this.timer.unref?.();
  }
}
