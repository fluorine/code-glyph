import { describe, expect, it } from 'vitest';
import { join } from 'node:path';
import { IconCatalog, iconToSvg } from '../src/catalog';
import { IconCache } from '../src/cache';
import { descriptorKey, type Descriptor } from '../src/descriptor';
import { Glossary } from '../src/glossary';
import { SelectionPipeline } from '../src/pipeline';
import { buildSystem, buildUser, wordsOf } from '../src/prompt';
import type { Selector } from '../src/providers';
import { emptyUsage } from '../src/usage';
import { BADGES, badgeFor, conceptWords, localIcon, tokens, WORDS } from '../src/vocabulary';

const catalog = IconCatalog.load(join(__dirname, '..', 'resources', 'icons.json.gz'));
const desc = (o: Partial<Descriptor> = {}): Descriptor => ({
  kind: 'Method', name: 'Widget', container: '', language: 'typescript', lineCount: 10, signature: '', comments: '', ...o,
});
const options = { minConfidence: () => 0.7, minSymbolLines: () => 4, logUsage: () => false, log: () => {} };

describe('vocabulary', () => {
  it('splits names into words', () => {
    expect(tokens('SendWelcomeEmail')).toEqual(['send', 'welcome', 'email']);
    expect(tokens('parse_HTTPResponse2')).toEqual(['parse', 'http', 'response2']);
  });
  it('uses only icons that exist', () => {
    for (const icon of [...WORDS.values(), ...BADGES.values()]) expect(catalog.has(icon), icon).toBe(true);
  });
  it('resolves a name when its known words agree', () => {
    expect(localIcon({ name: 'SendWelcomeEmail' })).toBe('mail');
    expect(localIcon({ name: 'EmailTemplates' })).toBe('mail');
    expect(localIcon({ name: 'Orders' })).toBe('shopping-cart');
  });
  it('defers when words disagree or none is known', () => {
    expect(localIcon({ name: 'OrderEmailValidator' })).toBeNull();
    expect(localIcon({ name: 'Helper' })).toBeNull();
  });
  it('gives a badge for method verbs only', () => {
    expect(badgeFor(desc({ name: 'CancelOrder' }))).toBe('x');
    expect(badgeFor(desc({ name: 'getUserById' }))).toBe('search');
    expect(badgeFor(desc({ name: 'Widget' }))).toBeNull();
    expect(badgeFor(desc({ name: 'CancelOrder', kind: 'Type' }))).toBeNull();
  });
  it('keeps verbs and constructs out of concept words', () => {
    expect(conceptWords('CancelOrderHandler')).toEqual(['order']);
  });
});

describe('glossary', () => {
  it('hints only a word seen twice with one dominant icon', () => {
    const g = new Glossary();
    g.learn(desc({ name: 'SupplyBatch' }), 'package');
    expect(g.hints([desc({ name: 'Supply' })])).toEqual([]);
    g.learn(desc({ name: 'SupplyRun' }), 'package');
    expect(g.hints([desc({ name: 'SupplyList' })])).toEqual([{ word: 'supply', icon: 'package' }]);
    g.learn(desc({ name: 'SupplyX' }), 'truck');
    g.learn(desc({ name: 'SupplyY' }), 'truck');
    expect(g.hints([desc({ name: 'Supply' })])).toEqual([]);
  });
  it('learns from high confidence answers only and sends hints to the provider', async () => {
    const g = new Glossary();
    const seen: unknown[] = [];
    let answer = 0.95;
    const sel: Selector = {
      name: 's',
      async select(_s, reqs, _sig, hints) {
        seen.push(hints);
        return { answers: reqs.map(r => ({ id: r.id, icon: 'package', confidence: answer })), usage: emptyUsage() };
      },
    };
    const p = new SelectionPipeline(catalog, new IconCache(), () => sel, { ...options, glossary: g });
    await p.resolve([desc({ name: 'SupplyOne', kind: 'Type' }), desc({ name: 'SupplyTwo', kind: 'Type' })]);
    await p.resolve([desc({ name: 'SupplyThree', kind: 'Type' })]);
    expect(seen[1]).toEqual([{ word: 'supply', icon: 'package' }]);
    answer = 0.8;
    await p.resolve([desc({ name: 'GadgetOne', kind: 'Type' }), desc({ name: 'GadgetTwo', kind: 'Type' })]);
    expect(g.hints([desc({ name: 'Gadget' })])).toEqual([]);
  });
});

describe('local icons in the pipeline', () => {
  it('draws them with no model call', async () => {
    let calls = 0;
    const sel: Selector = { name: 's', async select() { calls++; return { answers: [], usage: emptyUsage() }; } };
    const p = new SelectionPipeline(catalog, new IconCache(), () => sel, options);
    const d = desc({ name: 'SendWelcomeEmail' });
    expect(p.lookup(d)).toBe('mail');
    await p.resolve([d]);
    expect(calls).toBe(0);
  });
});

describe('folder and prompt', () => {
  it('puts the folder in the key and the user prompt', () => {
    const a = desc({ folder: 'billing/invoices' }), b = desc({ folder: 'admin/users' });
    expect(descriptorKey(a, '1', '2')).not.toBe(descriptorKey(b, '1', '2'));
    expect(buildUser([{ id: '0', descriptor: a }])).toContain('"folder":"billing/invoices"');
  });
  it('adds hints only when there are some', () => {
    const r = [{ id: '0', descriptor: desc() }];
    expect(buildUser(r)).not.toContain('hints');
    expect(buildUser(r, [{ word: 'supply', icon: 'package' }])).toContain('"hints":[{"word":"supply","icon":"package"}]');
  });
});

describe('shortlist', () => {
  it('finds icons by name and tag words and keeps the list small', () => {
    const only = catalog.shortlist(['invoice', 'receipt', 'email'], ['truck']);
    expect(only).toContain('receipt');
    expect(only).toContain('mail');
    expect(only).toContain('truck');
    expect(only.length).toBeLessThanOrEqual(151);
    const small = buildSystem(catalog, only);
    expect(small.length).toBeLessThan(buildSystem(catalog).length / 4);
    expect(small).toContain('short list');
  });
  it('is empty when nothing matches, so the full list is used', () => {
    expect(catalog.shortlist(['zzzqqq'])).toEqual([]);
  });
  it('collects words from names, comments and folder', () => {
    const words = wordsOf([{ id: '0', descriptor: desc({ name: 'SendInvoice', comments: 'Mails the receipt.', folder: 'billing' }) }]);
    expect(words).toEqual(expect.arrayContaining(['send', 'invoice', 'receipt', 'billing']));
  });
  it('sends the short list only to providers with no prompt cache', async () => {
    const systems: string[] = [];
    const make = (shortlist: boolean): Selector => ({
      name: 's',
      shortlist,
      async select(system, reqs) {
        systems.push(system);
        return { answers: reqs.map(r => ({ id: r.id, icon: null, confidence: 0 })), usage: emptyUsage() };
      },
    });
    for (const flag of [false, true]) {
      const sel = make(flag);
      const p = new SelectionPipeline(catalog, new IconCache(), () => sel, options);
      await p.resolve([desc({ name: 'Reconcile', comments: 'Matches ledger entries with bank lines.' })]);
    }
    expect(systems[1].length).toBeLessThan(systems[0].length / 4);
  });
});

describe('badge svg', () => {
  const base = { color: '#fff', opacity: 0.1, strokeWidth: 2, filled: false };
  it('draws a smaller main icon and a badge with thinner lines', () => {
    const svg = iconToSvg(catalog.get('shopping-cart')!, { ...base, badge: catalog.get('x') });
    expect(svg).toContain('scale(0.78)');
    expect(svg).toContain('translate(14 14) scale(0.4)');
    expect(svg).toContain('stroke-width="1.60"');
  });
  it('has no badge group by default', () => {
    expect(iconToSvg(catalog.get('mail')!, base)).not.toContain('scale(');
  });
});
