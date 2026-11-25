import { gunzipSync } from 'node:zlib';
import { readFileSync } from 'node:fs';

export interface IconElement { k: string; a: Record<string, string>; }
export interface IconDef { n: string; t: string[]; e: IconElement[]; }

export class IconCatalog {
  private readonly byName = new Map<string, IconDef>();
  readonly version: string;

  constructor(json: { version: string; icons: IconDef[] }) {
    this.version = json.version;
    for (const i of json.icons) this.byName.set(i.n, i);
  }

  static load(path: string): IconCatalog {
    return new IconCatalog(JSON.parse(gunzipSync(readFileSync(path)).toString('utf8')));
  }

  get(name: string): IconDef | undefined { return this.byName.get(name); }
  has(name: string): boolean { return this.byName.has(name); }
  get size(): number { return this.byName.size; }

  /**
   * Icons whose name or tags match the words, best first. Used for providers with no prompt cache.
   * The result is empty when nothing matches. Callers then send the full list.
   */
  shortlist(words: string[], extra: string[] = [], max = 150): string[] {
    const wanted = [...new Set(words.filter(w => w.length >= 3))];
    const scored: Array<[string, number]> = [];
    for (const icon of this.byName.values()) {
      const nameParts = icon.n.split('-');
      const tagParts = new Set(icon.t.flatMap(t => t.toLowerCase().split(/[^a-z0-9]+/)));
      let score = 0;
      for (const w of wanted) {
        for (const p of nameParts) {
          if (p === w) score += 3;
          else if (p.length >= 4 && w.length >= 4 && (p.startsWith(w) || w.startsWith(p))) score += 2;
        }
        if (tagParts.has(w)) score += 1;
      }
      if (score > 0) scored.push([icon.n, score]);
    }
    const names = scored.sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? -1 : 1)).slice(0, max).map(s => s[0]);
    const all = new Set(names);
    for (const e of extra) if (this.byName.has(e)) all.add(e);
    return all.size ? [...all] : [];
  }

  /** One line per icon: "name: tag, tag, tag". Sorted so the prompt prefix stays stable. */
  promptText(maxTags = 3, only?: string[]): string {
    const keep = only ? new Set(only) : undefined;
    return [...this.byName.values()]
      .filter(i => !keep || keep.has(i.n))
      .sort((x, y) => (x.n < y.n ? -1 : x.n > y.n ? 1 : 0))
      .map(i => i.n + (i.t.length ? ': ' + i.t.slice(0, maxTags).join(', ') : ''))
      .join('\n') + '\n';
  }
}

export interface SvgOptions { color: string; opacity: number; strokeWidth: number; filled: boolean; badge?: IconDef; }

const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');

/** Lucide uses only path, circle, ellipse, rect, line, polyline and polygon on a 24 grid. strokeWidth is in pixels at any icon size. */
const ALLOWED = new Set(['path', 'circle', 'ellipse', 'rect', 'line', 'polyline', 'polygon']);

const elementsOf = (icon: IconDef) => icon.e
  .filter(el => ALLOWED.has(el.k))
  .map(el => '<' + el.k + Object.entries(el.a).map(([k, v]) => ` ${esc(k)}="${esc(v)}"`).join('') + ' vector-effect="non-scaling-stroke"/>')
  .join('');

export function iconToSvg(icon: IconDef, o: SvgOptions): string {
  // A badge takes the bottom right corner. The main icon shrinks to leave room.
  const body = o.badge
    ? `<g transform="scale(0.78)">${elementsOf(icon)}</g>`
      + `<g transform="translate(14 14) scale(0.4)" stroke-width="${(o.strokeWidth * 0.8).toFixed(2)}">${elementsOf(o.badge)}</g>`
    : elementsOf(icon);
  const fill = o.filled ? `fill="${o.color}" fill-opacity="0.5"` : 'fill="none"';
  // Opacity sits on the group. Per-path opacity makes crossing paths look brighter.
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><g opacity="${o.opacity.toFixed(3)}" `
    + `${fill} stroke="${o.color}" stroke-width="${o.strokeWidth}" stroke-linecap="round" `
    + `stroke-linejoin="round">${body}</g></svg>`;
}

export function svgToDataUri(svg: string): string {
  return 'data:image/svg+xml;base64,' + Buffer.from(svg, 'utf8').toString('base64');
}
