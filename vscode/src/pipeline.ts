import type { IconCatalog } from './catalog';
import type { IconCache } from './cache';
import { descriptorKey, type Descriptor } from './descriptor';
import { isEligibleName } from './comments';
import type { Glossary } from './glossary';
import type { OverrideStore } from './overrides';
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
  /** Default true. When false, every name goes to the model. */
  useLocalWords?: () => boolean;
  /** Entries from .codeglyph.json. They win over every other source. */
  overrides?: OverrideStore;
}

export interface Explanation {
  icon: string | null;
  source: 'override' | 'word' | 'model' | 'none';
  confidence?: number;
}

/** Why each answer of one batch gave an icon or not. It goes to the log. */
class Tally {
  icons = 0; none = 0; lowConfidence = 0; generic = 0; unknownIcon = 0; noAnswer = 0;
  readonly low: string[] = [];

  summary(): string {
    const without = this.none + this.lowConfidence + this.generic + this.unknownIcon + this.noAnswer;
    return `Answers: ${this.icons} icons, ${without} without (${this.none} none, ${this.lowConfidence} below confidence, `
      + `${this.generic} generic, ${this.unknownIcon} unknown icon name, ${this.noAnswer} no answer)`;
  }
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
    const o = this.override(d);
    if (o) return o.icon;
    const local = this.local(d);
    if (local) return local;
    const c = this.cache.get(this.keyOf(d));
    // A low-confidence answer stays in the cache. The limit is applied here, so a change shows at once.
    return c?.icon && this.catalog.has(c.icon) && c.confidence >= this.o.minConfidence() ? c.icon : null;
  }

  /** undefined: no override. icon null: the user hid the icon. */
  private override(d: Descriptor): { icon: string | null } | undefined {
    const o = this.o.overrides?.lookup(d);
    return o && (o.icon === null || this.catalog.has(o.icon)) ? o : undefined;
  }

  /** Where the icon of a symbol comes from. */
  explain(d: Descriptor): Explanation {
    const o = this.override(d);
    if (o) return { icon: o.icon, source: 'override' };
    const local = this.local(d);
    if (local) return { icon: local, source: 'word' };
    const c = this.cache.get(this.keyOf(d));
    return c?.icon && this.catalog.has(c.icon) && c.confidence >= this.o.minConfidence()
      ? { icon: c.icon, source: 'model', confidence: c.confidence }
      : { icon: null, source: 'none' };
  }

  /** A name whose known words agree on one icon needs no model call. */
  private local(d: Descriptor): string | null {
    if (this.o.useLocalWords && !this.o.useLocalWords()) return null;
    const icon = localIcon(d);
    return icon && this.catalog.has(icon) ? icon : null;
  }

  shouldRequest(d: Descriptor): boolean {
    return d.lineCount >= this.o.minSymbolLines() && isEligibleName(d.name, d.kind) && !this.override(d) && !this.local(d);
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
          const tally = new Tally();
          const missed = this.apply(batch, keyById, await this.ask(selector, batch, signal), false, tally);
          changed = missed.length < batch.length || changed;
          if (missed.length) {
            this.apply(missed, keyById, await this.ask(selector, missed, signal), true, tally);
            changed = true;
          }
          if (this.o.logUsage()) {
            this.o.log(tally.summary());
            if (tally.low.length) this.o.log('Below the confidence limit, kept hidden: ' + tally.low.slice(0, 12).join('; '));
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
  private apply(batch: Request[], keyById: Map<string, string>, answers: { id: string; icon: string | null; confidence: number }[], final: boolean, tally: Tally): Request[] {
    const min = this.o.minConfidence();
    const byId = new Map(answers.map(a => [a.id, a] as const));
    const missed: Request[] = [];
    for (const r of batch) {
      const a = byId.get(r.id);
      if (!a && !final) { missed.push(r); continue; }
      let icon: string | null = null;
      let confidence = 0;
      if (!a) tally.noAnswer++;
      else if (!a.icon) tally.none++;
      else if (!this.catalog.has(a.icon)) tally.unknownIcon++;
      else if (a.confidence < 0.9 && GENERIC_ICONS.includes(a.icon)) tally.generic++;
      else {
        icon = a.icon;
        confidence = a.confidence;
        if (confidence < min) {
          tally.lowConfidence++;
          tally.low.push(`${r.descriptor.name} = ${icon} ${confidence.toFixed(2)}`);
        } else {
          tally.icons++;
          if (confidence >= 0.9) this.o.glossary?.learn(r.descriptor, icon);
        }
      }
      this.cache.set(keyById.get(r.id)!, icon, confidence);
    }
    return missed;
  }

  private release(keys: Map<string, string>): void {
    for (const k of keys.values()) this.inFlight.delete(k);
  }
}
