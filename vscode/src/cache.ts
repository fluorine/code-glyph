import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

export interface Choice { icon: string | null; confidence: number; }

/** Key to answer. "None" answers are stored too, so they are never asked again. */
export class IconCache {
  private map = new Map<string, Choice>();
  private timer: NodeJS.Timeout | undefined;

  constructor(private readonly path?: string) {
    if (path && existsSync(path)) {
      try {
        const raw = JSON.parse(readFileSync(path, 'utf8')) as Record<string, Choice>;
        for (const [k, v] of Object.entries(raw)) this.map.set(k, v);
      } catch { /* A bad file must never break the editor. */ }
    }
  }

  get(key: string): Choice | undefined { return this.map.get(key); }
  set(key: string, icon: string | null, confidence: number): void { this.map.set(key, { icon, confidence }); }
  get size(): number { return this.map.size; }

  clear(): void { this.map.clear(); this.save(); }

  save(): void {
    if (!this.path) return;
    try {
      mkdirSync(dirname(this.path), { recursive: true });
      writeFileSync(this.path, JSON.stringify(Object.fromEntries(this.map)));
    } catch { /* Disk errors only cost the cache. */ }
  }

  saveSoon(): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.save(), 2000);
  }
}
