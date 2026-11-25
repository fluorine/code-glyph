import { afterEach, describe, expect, it } from 'vitest';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { IconCache } from '../src/cache';
import { IconCatalog } from '../src/catalog';
import { hslToHex, iconColor, iconHue } from '../src/color';
import type { Descriptor } from '../src/descriptor';
import { buildEntries } from '../src/mapentries';
import { matchOverride, OverrideStore, parseOverrides } from '../src/overrides';
import { SelectionPipeline } from '../src/pipeline';
import type { Selector } from '../src/providers';
import type { Sym } from '../src/symbols';
import { emptyUsage } from '../src/usage';

const catalog = IconCatalog.load(join(__dirname, '..', 'resources', 'icons.json.gz'));
const desc = (o: Partial<Descriptor> = {}): Descriptor => ({
  kind: 'Method', name: 'Widget', container: 'Shop', language: 'typescript', lineCount: 10, signature: '', comments: '', ...o,
});
const options = { minConfidence: () => 0.7, minSymbolLines: () => 4, logUsage: () => false, log: () => {} };

const dirs: string[] = [];
const tmp = () => { const d = mkdtempSync(join(tmpdir(), 'cg3-')); dirs.push(d); return d; };
afterEach(() => { while (dirs.length) rmSync(dirs.pop()!, { recursive: true, force: true }); });

describe('overrides file', () => {
  it('reads entries and skips bad ones', () => {
    const list = parseOverrides(JSON.stringify({ overrides: [
      { name: 'A', icon: 'mail' }, { name: 'B', icon: null }, { name: 'C', icon: 'none' },
      { icon: 'mail' }, { name: 'D', icon: 5 }, 'text', { name: 'E', kind: 'Bogus', container: 'X', icon: 'bell' },
    ] }));
    expect(list).toEqual([
      { name: 'A', icon: 'mail' }, { name: 'B', icon: null }, { name: 'C', icon: null }, { name: 'E', container: 'X', icon: 'bell' },
    ]);
    expect(parseOverrides('{ broken')).toEqual([]);
  });
  it('picks the most specific match, and the later entry on a tie', () => {
    const list = parseOverrides(JSON.stringify({ overrides: [
      { name: 'Run', icon: 'a' }, { name: 'Run', container: 'Shop', icon: 'b' }, { name: 'Run', icon: 'c' },
      { name: 'Run', container: 'Shop', kind: 'Method', icon: 'd' },
    ] }));
    expect(matchOverride(list, { name: 'Run', container: 'Shop', kind: 'Method' })?.icon).toBe('d');
    expect(matchOverride(list, { name: 'Run', container: 'Other', kind: 'Method' })?.icon).toBe('c');
    expect(matchOverride(list, { name: 'Nope', container: '', kind: 'Method' })).toBeUndefined();
  });
  it('finds the nearest file above the source file and follows edits', () => {
    const root = tmp();
    const sub = join(root, 'src', 'deep');
    mkdirSync(sub, { recursive: true });
    let now = 0;
    const store = new OverrideStore(() => now, 1000);
    const d = desc({ path: join(sub, 'a.ts') });
    expect(store.lookup(d)).toBeUndefined();

    writeFileSync(join(root, '.codeglyph.json'), JSON.stringify({ overrides: [{ name: 'Widget', icon: 'bell' }] }));
    now += 2000;
    expect(store.lookup(d)).toEqual({ icon: 'bell' });

    writeFileSync(join(sub, '.codeglyph.json'), JSON.stringify({ overrides: [{ name: 'Widget', icon: null }] }));
    now += 2000;
    expect(store.lookup(d)).toEqual({ icon: null });
    expect(store.lookup(desc({ name: 'Widget' }))).toBeUndefined();
  });
  it('writes, replaces and removes one symbol', () => {
    const root = tmp();
    const file = join(root, '.codeglyph.json');
    const store = new OverrideStore();
    const target = { name: 'Run', container: 'Shop', kind: 'Method' as const };
    store.set(file, target, 'bell');
    store.set(file, target, 'mail');
    store.set(file, { name: 'Other', container: '', kind: 'Type' }, null);
    const text = require('node:fs').readFileSync(file, 'utf8');
    expect(parseOverrides(text)).toEqual([
      { name: 'Run', container: 'Shop', kind: 'Method', icon: 'mail' },
      { name: 'Other', kind: 'Type', icon: null },
    ]);
    expect(store.remove(file, target)).toBe(true);
    expect(store.remove(file, target)).toBe(false);
    expect(parseOverrides(require('node:fs').readFileSync(file, 'utf8'))).toHaveLength(1);
  });
});

describe('overrides in the pipeline', () => {
  const setup = (entries: unknown[]) => {
    const root = tmp();
    writeFileSync(join(root, '.codeglyph.json'), JSON.stringify({ overrides: entries }));
    let calls = 0;
    const sel: Selector = { name: 's', async select() { calls++; return { answers: [], usage: emptyUsage() }; } };
    const pipeline = new SelectionPipeline(catalog, new IconCache(), () => sel, { ...options, overrides: new OverrideStore() });
    return { pipeline, path: join(root, 'a.ts'), calls: () => calls };
  };

  it('wins over the local word list and needs no model call', async () => {
    const { pipeline, path, calls } = setup([{ name: 'SendEmail', icon: 'bell' }]);
    const d = desc({ name: 'SendEmail', path });
    expect(pipeline.lookup(d)).toBe('bell');
    expect(pipeline.explain(d)).toEqual({ icon: 'bell', source: 'override' });
    await pipeline.resolve([d]);
    expect(calls()).toBe(0);
  });
  it('hides the icon when the entry is null', () => {
    const { pipeline, path } = setup([{ name: 'SendEmail', icon: null }]);
    const d = desc({ name: 'SendEmail', path });
    expect(pipeline.lookup(d)).toBeNull();
    expect(pipeline.explain(d)).toEqual({ icon: null, source: 'override' });
    expect(pipeline.shouldRequest(d)).toBe(false);
  });
  it('ignores an entry with an unknown icon', () => {
    const { pipeline, path } = setup([{ name: 'SendEmail', icon: 'not-an-icon' }]);
    expect(pipeline.lookup(desc({ name: 'SendEmail', path }))).toBe('mail');
  });
  it('explains word and model sources', async () => {
    const sel: Selector = { name: 's', async select(_s, reqs) { return { answers: reqs.map(r => ({ id: r.id, icon: 'package', confidence: 0.92 })), usage: emptyUsage() }; } };
    const p = new SelectionPipeline(catalog, new IconCache(), () => sel, options);
    const w = desc({ name: 'SendEmail' }), m = desc({ name: 'Reconcile' });
    await p.resolve([w, m]);
    expect(p.explain(w)).toEqual({ icon: 'mail', source: 'word' });
    expect(p.explain(m)).toEqual({ icon: 'package', source: 'model', confidence: 0.92 });
    expect(p.explain(desc({ name: 'Unknown' }))).toEqual({ icon: null, source: 'none' });
  });
});

describe('local words setting', () => {
  it('sends every name to the model when off', async () => {
    let asked = 0;
    const sel: Selector = { name: 's', async select(_s, reqs) { asked += reqs.length; return { answers: reqs.map(r => ({ id: r.id, icon: 'package', confidence: 0.95 })), usage: emptyUsage() }; } };
    let on = true;
    const p = new SelectionPipeline(catalog, new IconCache(), () => sel, { ...options, useLocalWords: () => on });
    const d = desc({ name: 'SendEmail' });
    expect(p.lookup(d)).toBe('mail');
    on = false;
    expect(p.lookup(d)).toBeNull();
    await p.resolve([d]);
    expect(asked).toBe(1);
    expect(p.lookup(d)).toBe('package');
  });
});

describe('icon colors', () => {
  it('gives a stable color for a name', () => {
    expect(iconHue('mail')).toBe(iconHue('mail'));
    expect(iconColor('mail', true)).toMatch(/^#[0-9a-f]{6}$/);
    expect(iconColor('mail', true)).toBe(iconColor('mail', true));
  });
  it('differs by name and by theme', () => {
    const hues = new Set(['mail', 'bell', 'shield', 'truck', 'package', 'user', 'clock', 'star'].map(iconHue));
    expect(hues.size).toBeGreaterThan(5);
    expect(iconColor('mail', true)).not.toBe(iconColor('mail', false));
  });
  it('converts hsl to hex', () => {
    expect(hslToHex(0, 1, 0.5)).toBe('#ff0000');
    expect(hslToHex(120, 1, 0.5)).toBe('#00ff00');
    expect(hslToHex(240, 1, 0.5)).toBe('#0000ff');
  });
});

describe('glyph map entries', () => {
  const sym = (name: string, start: number, end: number): Sym =>
    ({ descriptor: desc({ name }), band: { start, end }, start, end, sigLine: start });
  it('nests by line range and leaves out symbols with no icon', () => {
    const syms = [sym('Method2', 20, 30), sym('Class', 0, 40), sym('NoIcon', 5, 10), sym('Method1', 6, 15)];
    const roots = buildEntries('file:///a.ts', syms, s => (s.descriptor.name === 'NoIcon' ? null : 'mail'));
    expect(roots.map(r => r.sym.descriptor.name)).toEqual(['Class']);
    expect(roots[0].children.map(c => c.sym.descriptor.name)).toEqual(['Method1', 'Method2']);
  });
});

describe('answer log', () => {
  it('says why answers gave no icon', async () => {
    const logs: string[] = [];
    const sel: Selector = {
      name: 's',
      async select(_s, reqs) {
        if (reqs.length < 5) return { answers: [{ id: '99', icon: null, confidence: 0 }], usage: emptyUsage() };
        const answers = [
          { id: reqs[0].id, icon: 'package', confidence: 0.95 },
          { id: reqs[1].id, icon: null, confidence: 0 },
          { id: reqs[2].id, icon: 'package', confidence: 0.4 },
          { id: reqs[3].id, icon: 'box', confidence: 0.8 },
          { id: reqs[4].id, icon: 'not-an-icon', confidence: 0.99 },
        ];
        return { answers, usage: emptyUsage() };
      },
    };
    const p = new SelectionPipeline(catalog, new IconCache(), () => sel, { ...options, logUsage: () => true, log: m => logs.push(m) });
    await p.resolve(['A1', 'B1', 'C1', 'D1', 'E1', 'F1'].map(name => desc({ name, kind: 'Type' })));
    expect(logs.join('\n')).toContain('Answers: 1 icons, 5 without (1 none, 1 below confidence, 1 generic, 1 unknown icon name, 1 no answer)');
  });
});

describe('confidence limit', () => {
  it('keeps a low-confidence answer cached and shows it when the limit drops', async () => {
    let asked = 0;
    const sel: Selector = {
      name: 's',
      async select(_s, reqs) { asked += reqs.length; return { answers: reqs.map(r => ({ id: r.id, icon: 'package', confidence: 0.6 })), usage: emptyUsage() }; },
    };
    let limit = 0.7;
    const logs: string[] = [];
    const p = new SelectionPipeline(catalog, new IconCache(), () => sel, { ...options, minConfidence: () => limit, logUsage: () => true, log: m => logs.push(m) });
    const d = desc({ name: 'Reconcile', kind: 'Type' });
    await p.resolve([d]);
    expect(p.lookup(d)).toBeNull();
    expect(logs.join('\n')).toContain('Below the confidence limit, kept hidden: Reconcile = package 0.60');
    limit = 0.5;
    expect(p.lookup(d)).toBe('package');
    expect(p.explain(d)).toEqual({ icon: 'package', source: 'model', confidence: 0.6 });
    await p.resolve([d]);
    expect(asked).toBe(1);
  });
  it('explains the confidence scale in the prompt', async () => {
    const { buildSystem } = await import('../src/prompt');
    const text = buildSystem(catalog);
    expect(text).toContain('Use 0.5 to 0.69 for a reasonable fit');
    expect(text).toContain('module, controller');
  });
});

describe('icon sensitivity', () => {
  it('maps the level to a confidence limit', async () => {
    const { confidenceLimit } = await import('../src/sensitivity');
    expect(confidenceLimit('strict', null)).toBe(0.7);
    expect(confidenceLimit('balanced', null)).toBe(0.5);
    expect(confidenceLimit('generous', undefined)).toBe(0.4);
    expect(confidenceLimit('unknown', null)).toBe(0.5);
  });
  it('lets an exact limit win', async () => {
    const { confidenceLimit } = await import('../src/sensitivity');
    expect(confidenceLimit('strict', 0.6)).toBe(0.6);
    expect(confidenceLimit('strict', 0)).toBe(0.7);
  });
});
