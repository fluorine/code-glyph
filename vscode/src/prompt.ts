import type { IconCatalog } from './catalog';
import type { Descriptor } from './descriptor';

export const PROMPT_VERSION = '1';

export const GENERIC_ICONS = ['file', 'code', 'braces', 'box', 'circle', 'square', 'shapes', 'settings', 'wrench', 'puzzle'];

export interface Request { id: string; descriptor: Descriptor; }
export interface Answer { id: string; icon: string | null; confidence: number; }

export function buildSystem(catalog: IconCatalog): string {
  return [
    'You choose a watermark icon for symbols in a code editor. The code can be in any programming language.',
    'For each symbol, pick ONE icon name from the ICON LIST that shows the specific domain function of the symbol.',
    'Base the choice on the symbol name, container, signature and comments. You never see the code body.',
    '',
    'Rules:',
    '- Answer null when no icon clearly fits. No icon is better than a vague icon.',
    '- Do not use these generic icons unless the name or comments name that exact concept: ' + GENERIC_ICONS.join(', ') + '.',
    '- Do not pick an icon only because of the programming construct (class, method, function, handler, helper, manager, service, utility, base).',
    '- confidence is a number from 0 to 1. Use 0.9 or more only when the match is obvious.',
    '- Use only names that appear in the ICON LIST. Copy each name exactly.',
    '',
    'Answer with JSON only. No prose, no code fence. Format:',
    '{"results":[{"id":"<id>","icon":"<icon name or null>","confidence":0.0}]}',
    'Return one result for each input id.',
    '',
    'ICON LIST (name: tags):',
    catalog.promptText(),
  ].join('\n');
}

const cut = (s: string, n: number) => (s && s.length > n ? s.slice(0, n) : s);

export function buildUser(requests: Request[]): string {
  const symbols = requests.map(r => {
    const d = r.descriptor;
    const o: Record<string, string> = { id: r.id, kind: d.kind, name: d.name, language: d.language };
    if (d.container) o.container = d.container;
    if (d.signature) o.signature = cut(d.signature, 200);
    if (d.comments) o.comments = cut(d.comments, 600);
    return o;
  });
  return JSON.stringify({ symbols });
}

/** Accepts {"results":[...]} or a bare array, with or without surrounding text. */
export function parseResponse(text: string): Answer[] {
  if (!text || !text.trim()) return [];
  const o = text.indexOf('{');
  const a = text.indexOf('[');
  let json: string;
  if (o >= 0 && (a < 0 || o < a)) {
    const end = text.lastIndexOf('}');
    if (end <= o) return [];
    json = text.slice(o, end + 1);
  } else if (a >= 0) {
    const end = text.lastIndexOf(']');
    if (end <= a) return [];
    json = text.slice(a, end + 1);
  } else return [];

  let root: unknown;
  try { root = JSON.parse(json); } catch { return []; }
  const arr: unknown = Array.isArray(root) ? root : (root as { results?: unknown })?.results;
  if (!Array.isArray(arr)) return [];

  const out: Answer[] = [];
  for (const item of arr) {
    if (!item || typeof item !== 'object' || !('id' in item)) continue;
    const it = item as { id: unknown; icon?: unknown; confidence?: unknown };
    const id = String(it.id);
    if (!id) continue;
    let icon: string | null = typeof it.icon === 'string' ? it.icon.trim() : null;
    if (!icon || /^(null|none)$/i.test(icon)) icon = null;
    out.push({ id, icon, confidence: typeof it.confidence === 'number' ? it.confidence : 0 });
  }
  return out;
}
