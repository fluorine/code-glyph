import type { IconCatalog } from './catalog';
import type { IconCache } from './cache';
import { descriptorKey, type Descriptor } from './descriptor';
import { isEligibleName } from './comments';
import type { Glossary } from './glossary';
import { buildSystem, GENERIC_ICONS, PROMPT_VERSION, wordsOf, type Request } from './prompt';
import type { Selector } from './providers';
import { emptyUsage, UsageTracker } from './usage';
import { localIcon } from './vocabulary';

const BATCH = 12;

export interface PipelineOptions {
  minConfidence: () => number;
  minSymbolLines: () => number;
  logUsage: () => boolean;
  log: (m: string) => void;
  now?: () => number;
  glossary?: Glossary;
}

export class SelectionPipeline {
  readonly usage = new UsageTracker();
  private readonly inFlight = new Set<string>();
  private blockedUntil = 0;
  private backoffSec = 0;
  private system: string | undefined;
  private listeners: Array<() => void> = [];

  constructor(
    private readonly catalog: IconCatalog,
    private readonly cache: IconCache,
    private readonly selector: () => Selector | undefined,
    private readonly o: PipelineOptions,
  ) {}

  onResolved(fn: () => void): void { this.listeners.push(fn); }
  keyOf(d: Descriptor): string { return descriptorKey(d, this.catalog.version, PROMPT_VERSION); }

  /** The icon name to draw now. Null for none or not yet known. */
  lookup(d: Descriptor): string | null {
    const local = this.local(d);
    if (local) return local;
    const c = this.cache.get(this.keyOf(d));
    return c?.icon && this.catalog.has(c.icon) ? c.icon : null;
  }

  /** A name whose known words agree on one icon needs no model call. */
  private local(d: Descriptor): string | null {
    const icon = localIcon(d);
    return icon && this.catalog.has(icon) ? icon : null;
  }

  shouldRequest(d: Descriptor): boolean {
    return d.lineCount >= this.o.minSymbolLines() && isEligibleName(d.name, d.kind) && !this.local(d);
  }

  /** Asks the provider for every uncached, eligible descriptor. Never throws on provider errors. */
  async resolve(descriptors: Descriptor[], signal?: AbortSignal): Promise<void> {
    const now = this.o.now ?? Date.now;
    if (now() < this.blockedUntil) return;

    const pending: Request[] = [];
    const keyById = new Map<string, string>();
    for (const d of descriptors) {
      if (!this.shouldRequest(d)) continue;
      const key = this.keyOf(d);
      if (this.cache.get(key) || this.inFlight.has(key)) continue;
      this.inFlight.add(key);
      const id = String(pending.length);
      pending.push({ id, descriptor: d });
      keyById.set(id, key);
    }
    if (!pending.length) return;

    const selector = this.selector();
    if (!selector) {
      this.release(keyById);
      this.o.log('No provider is configured. Run "CodeGlyph: Set Claude API key", or install the claude CLI.');
      return;
    }

    this.system ??= buildSystem(this.catalog);
    let changed = false;
    try {
      for (let i = 0; i < pending.length; i += BATCH) {
        if (signal?.aborted) break;
        const batch = pending.slice(i, i + BATCH);
        try {
          const missed = this.apply(batch, keyById, await this.ask(selector, batch, signal), false);
          changed = missed.length < batch.length || changed;
          if (missed.length) {
            this.apply(missed, keyById, await this.ask(selector, missed, signal), true);
            changed = true;
          }
          this.backoffSec = 0;
        } catch (e) {
          this.backoffSec = this.backoffSec === 0 ? 30 : Math.min(600, this.backoffSec * 2);
          this.blockedUntil = now() + this.backoffSec * 1000;
          this.o.log(`${selector.name} failed: ${(e as Error).message} Retry in ${this.backoffSec} s.`);
          break;
        }
      }
    } finally {
      this.release(keyById);
      if (changed) {
        this.cache.saveSoon();
        for (const fn of this.listeners) fn();
      }
    }
  }

  private async ask(selector: Selector, batch: Request[], signal?: AbortSignal) {
    const hints = this.o.glossary?.hints(batch.map(r => r.descriptor)) ?? [];
    const only = selector.shortlist ? this.catalog.shortlist(wordsOf(batch), hints.map(h => h.icon)) : [];
    const system = only.length ? buildSystem(this.catalog, only) : this.system!;
    const { answers, usage } = await selector.select(system, batch, signal, hints);
    const line = this.usage.add(selector.name, batch.length, usage ?? emptyUsage());
    if (this.o.logUsage()) this.o.log(line + '. ' + this.usage.summary());
    // An empty reply is a damaged reply. It must not become a cached "none".
    if (!answers.length) throw new Error('The reply had no usable answers.');
    return answers;
  }

  /** A request with no answer is cached as "none" only when final. Otherwise it is returned. */
  private apply(batch: Request[], keyById: Map<string, string>, answers: { id: string; icon: string | null; confidence: number }[], final: boolean): Request[] {
    const min = this.o.minConfidence();
    const byId = new Map(answers.map(a => [a.id, a] as const));
    const missed: Request[] = [];
    for (const r of batch) {
      const a = byId.get(r.id);
      if (!a && !final) { missed.push(r); continue; }
      let icon: string | null = null;
      let confidence = 0;
      if (a?.icon && this.catalog.has(a.icon) && a.confidence >= min && !(a.confidence < 0.9 && GENERIC_ICONS.includes(a.icon))) {
        icon = a.icon;
        confidence = a.confidence;
        if (confidence >= 0.9) this.o.glossary?.learn(r.descriptor, icon);
      }
      this.cache.set(keyById.get(r.id)!, icon, confidence);
    }
    return missed;
  }

  private release(keys: Map<string, string>): void {
    for (const k of keys.values()) this.inFlight.delete(k);
  }
}
