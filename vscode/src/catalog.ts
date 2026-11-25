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

  /** One line per icon: "name: tag, tag, tag". Sorted so the prompt prefix stays stable. */
  promptText(maxTags = 3): string {
    return [...this.byName.values()]
      .sort((x, y) => (x.n < y.n ? -1 : x.n > y.n ? 1 : 0))
      .map(i => i.n + (i.t.length ? ': ' + i.t.slice(0, maxTags).join(', ') : ''))
      .join('\n') + '\n';
  }
}

export interface SvgOptions { color: string; opacity: number; strokeWidth: number; filled: boolean; }

const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');

/** Lucide uses only path, circle, ellipse, rect, line, polyline and polygon on a 24 grid. strokeWidth is in pixels at any icon size. */
export function iconToSvg(icon: IconDef, o: SvgOptions): string {
  const allowed = new Set(['path', 'circle', 'ellipse', 'rect', 'line', 'polyline', 'polygon']);
  const body = icon.e
    .filter(el => allowed.has(el.k))
    .map(el => '<' + el.k + Object.entries(el.a).map(([k, v]) => ` ${esc(k)}="${esc(v)}"`).join('') + ' vector-effect="non-scaling-stroke"/>')
    .join('');
  const fill = o.filled ? `fill="${o.color}" fill-opacity="0.5"` : 'fill="none"';
  // Opacity sits on the group. Per-path opacity makes crossing paths look brighter.
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><g opacity="${o.opacity.toFixed(3)}" `
    + `${fill} stroke="${o.color}" stroke-width="${o.strokeWidth}" stroke-linecap="round" `
    + `stroke-linejoin="round">${body}</g></svg>`;
}

export function svgToDataUri(svg: string): string {
  return 'data:image/svg+xml;base64,' + Buffer.from(svg, 'utf8').toString('base64');
}
