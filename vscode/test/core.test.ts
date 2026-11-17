import { describe, expect, it } from 'vitest';
import { join } from 'node:path';
import { IconCatalog, iconToSvg, svgToDataUri } from '../src/catalog';
import { IconCache } from '../src/cache';
import { descriptorKey, type Descriptor } from '../src/descriptor';
import { extractComments, isEligibleName } from '../src/comments';
import { columnFor, largestFree, layout, maxLineWidth } from '../src/placement';
import { buildSystem, buildUser, parseResponse } from '../src/prompt';
import { SelectionPipeline } from '../src/pipeline';
import { createSelector, type Selector } from '../src/providers';
import { emptyUsage } from '../src/usage';

const catalog = IconCatalog.load(join(__dirname, '..', 'resources', 'icons.json.gz'));

const desc = (o: Partial<Descriptor> = {}): Descriptor => ({
  kind: 'Type', name: 'EmailSender', container: '', language: 'typescript', lineCount: 10,
  signature: '', comments: 'Sends email to patients.', ...o,
});

describe('catalog', () => {
  it('loads the packed Lucide set and builds SVG', () => {
    expect(catalog.size).toBeGreaterThan(1000);
    const mail = catalog.get('mail')!;
    const svg = iconToSvg(mail, { color: '#fff', opacity: 0.1, strokeWidth: 2, filled: false });
    expect(svg).toContain('<svg');
    expect(svg).toContain('stroke-opacity="0.100"');
    expect(svgToDataUri(svg)).toMatch(/^data:image\/svg\+xml;base64,/);
  });
  it('prompt text is sorted and stable', () => {
    expect(catalog.promptText()).toBe(catalog.promptText());
    expect(buildSystem(catalog)).toContain('ICON LIST');
  });
});

describe('placement', () => {
  it('finds the largest free run around children', () => {
    expect(largestFree(0, 20, [{ start: 5, end: 8 }])).toEqual({ start: 9, end: 20 });
    expect(largestFree(0, 9, [{ start: 0, end: 9 }])).toBeUndefined();
  });
  it('centers, tops and bottoms the icon, and caps it', () => {
    const band = { start: 10, end: 29 };
    expect(layout(band, 4, 10, 'top')).toEqual({ startLine: 10, sizeLines: 10 });
    expect(layout(band, 4, 10, 'bottom')).toEqual({ startLine: 20, sizeLines: 10 });
    expect(layout(band, 4, 10, 'center')).toEqual({ startLine: 15, sizeLines: 10 });
  });
  it('shows no icon when the band is shorter than the minimum', () => {
    expect(layout({ start: 0, end: 2 }, 4, 10, 'center')).toBeUndefined();
  });
  it('expands tabs when measuring width', () => {
    expect(maxLineWidth(['\tabc', 'ab'], 0, 1, 4)).toBe(7);
  });
  it('column rules', () => {
    const base = { placement: 'fixedColumn' as const, column: 100, overlapCode: true };
    expect(columnFor(150, 6, base)).toBe(100);
    expect(columnFor(150, 6, { ...base, overlapCode: false })).toBeUndefined();
    expect(columnFor(40, 6, { ...base, overlapCode: false })).toBe(100);
    expect(columnFor(40, 6, { ...base, placement: 'afterCode' })).toBe(43);
  });
});

describe('comments', () => {
  it('reads a // block, skipping attributes and decorators', () => {
    const lines = ['// Sends email.', '// To patients.', '@Injectable()', 'class EmailSender {'];
    expect(extractComments(lines, 3, 3)).toBe('Sends email. To patients.');
  });
  it('reads XML doc and JSDoc blocks', () => {
    expect(extractComments(['/// <summary>Creates orders.</summary>', 'public class A {'], 1, 1)).toContain('Creates orders.');
    expect(extractComments(['/**', ' * Validates the token.', ' */', 'function f() {'], 3, 3)).toBe('Validates the token.');
  });
  it('reads a Python docstring after the signature', () => {
    const lines = ['def pay(order):', '    """Charges the card for an order."""', '    return 1'];
    expect(extractComments(lines, 0, 0)).toBe('Charges the card for an order.');
  });
  it('does not take code or region markers as comments', () => {
    expect(extractComments(['int x = 1;', 'void F() {'], 1, 1)).toBe('');
    expect(extractComments(['#region Helpers', 'void F() {'], 1, 1)).toBe('');
  });
  it('skips boilerplate names', () => {
    expect(isEligibleName('ToString', 'Method')).toBe(false);
    expect(isEligibleName('__init__', 'Method')).toBe(false);
    expect(isEligibleName('sendEmail', 'Method')).toBe(true);
  });
});

describe('key and prompt', () => {
  it('is stable and ignores whitespace in comments', () => {
    const a = descriptorKey(desc({ comments: 'Sends  email\n to patients.' }), 'v', '1');
    const b = descriptorKey(desc({ comments: 'Sends email to patients.' }), 'v', '1');
    expect(a).toBe(b);
    expect(descriptorKey(desc({ name: 'Other' }), 'v', '1')).not.toBe(a);
    expect(descriptorKey(desc(), 'v2', '1')).not.toBe(a);
  });
  it('user prompt never carries a body field', () => {
    const json = JSON.parse(buildUser([{ id: '0', descriptor: desc() }]));
    expect(Object.keys(json.symbols[0])).toEqual(['id', 'kind', 'name', 'language', 'comments']);
  });
  it('parses objects, bare arrays and fenced text; maps none to null', () => {
    expect(parseResponse('{"results":[{"id":"0","icon":"mail","confidence":0.9}]}')[0].icon).toBe('mail');
    expect(parseResponse('```json\n[{"id":1,"icon":"none","confidence":0.2}]\n```')[0]).toEqual({ id: '1', icon: null, confidence: 0.2 });
    expect(parseResponse('nonsense')).toEqual([]);
    expect(parseResponse('')).toEqual([]);
  });
});

class Fake implements Selector {
  name = 'fake';
  calls = 0;
  constructor(private reply: (ids: string[]) => { id: string; icon: string | null; confidence: number }[]) {}
  async select(_s: string, reqs: { id: string }[]) {
    this.calls++;
    return { answers: this.reply(reqs.map(r => r.id)), usage: { ...emptyUsage(), inputTokens: 10, outputTokens: 2 } };
  }
}

const makePipeline = (sel: () => Selector | undefined, logs: string[] = []) =>
  new SelectionPipeline(catalog, new IconCache(), sel, {
    minConfidence: () => 0.7, minSymbolLines: () => 4, logUsage: () => true, log: m => logs.push(m),
  });

describe('pipeline', () => {
  it('caches answers, including none, and asks only once', async () => {
    const f = new Fake(ids => ids.map(id => ({ id, icon: id === '0' ? 'mail' : null, confidence: 0.9 })));
    const p = makePipeline(() => f);
    const a = desc(), b = desc({ name: 'Helper', comments: '' });
    await p.resolve([a, b]);
    await p.resolve([a, b]);
    expect(f.calls).toBe(1);
    expect(p.lookup(a)).toBe('mail');
    expect(p.lookup(b)).toBeNull();
  });
  it('rejects unknown icons, low confidence and generic icons', async () => {
    const f = new Fake(ids => [
      { id: ids[0], icon: 'not-an-icon', confidence: 0.99 },
      { id: ids[1], icon: 'mail', confidence: 0.5 },
      { id: ids[2], icon: 'box', confidence: 0.8 },
    ]);
    const p = makePipeline(() => f);
    const ds = [desc({ name: 'A' }), desc({ name: 'B' }), desc({ name: 'C' })];
    await p.resolve(ds);
    expect(ds.map(d => p.lookup(d))).toEqual([null, null, null]);
  });
  it('skips short symbols and boilerplate names without calling the provider', async () => {
    const f = new Fake(() => []);
    const p = makePipeline(() => f);
    await p.resolve([desc({ lineCount: 2 }), desc({ name: 'ToString', kind: 'Method' })]);
    expect(f.calls).toBe(0);
  });
  it('backs off after a provider error and does not ask again at once', async () => {
    let n = 0;
    const bad: Selector = { name: 'bad', async select() { n++; throw new Error('boom'); } };
    const logs: string[] = [];
    const p = makePipeline(() => bad, logs);
    await p.resolve([desc()]);
    await p.resolve([desc({ name: 'Other' })]);
    expect(n).toBe(1);
    expect(logs.join('\n')).toContain('Retry in 30 s');
  });
  it('logs usage and raises Resolved', async () => {
    const f = new Fake(ids => ids.map(id => ({ id, icon: 'mail', confidence: 0.9 })));
    const logs: string[] = [];
    const p = makePipeline(() => f, logs);
    let raised = 0;
    p.onResolved(() => raised++);
    await p.resolve([desc()]);
    expect(raised).toBe(1);
    expect(logs[0]).toContain('in 10, out 2 tokens');
    expect(p.usage.summary()).toContain('1 calls');
  });
  it('removes in-flight keys when the provider is missing', async () => {
    let sel: Selector | undefined;
    const p = makePipeline(() => sel);
    await p.resolve([desc()]);
    sel = new Fake(ids => ids.map(id => ({ id, icon: 'mail', confidence: 0.9 })));
    await p.resolve([desc()]);
    expect(p.lookup(desc())).toBe('mail');
  });
});

describe('provider choice', () => {
  const base = { provider: 'auto' as const, anthropicModel: 'm', openAiModel: 'o', openAiBaseUrl: 'u', claudeCliPath: 'claude', claudeCliModel: 'haiku' };
  it('auto prefers keys, then the CLI', () => {
    expect(createSelector({ ...base, anthropicKey: 'k' }, {})!.name).toContain('Anthropic');
    expect(createSelector({ ...base }, { OPENAI_API_KEY: 'k' })!.name).toContain('OpenAI');
    expect(createSelector({ ...base }, {})!.name).toContain('Claude CLI');
    expect(createSelector({ ...base, provider: 'anthropic' }, {})).toBeUndefined();
  });
});
