import * as vscode from 'vscode';
import { iconToSvg, svgToDataUri, type IconCatalog } from './catalog';
import { columnFor, layout, maxLineWidth, type Vertical } from './placement';
import type { Sym } from './symbols';

export interface RenderConfig {
  enabled: boolean;
  colorMode: 'theme' | 'custom' | 'bySymbolKind';
  customColor: string;
  kindColors: { types: string; methods: string; properties: string };
  opacity: number;
  renderStyle: 'outline' | 'filled';
  strokeWidth: number;
  placement: 'fixedColumn' | 'afterCode';
  column: number;
  overlapCode: boolean;
  verticalAlignment: Vertical;
  minLines: number;
  maxLines: number;
}

const MAX_TYPES = 1500;

/**
 * Draws each icon as a CSS background on whole-line decorations. The decoration layer sits behind the text.
 * An icon of N lines is cut into N slices, one for each line. Units lh and ch keep it exact at any zoom.
 */
export class GlyphRenderer {
  private types = new Map<string, vscode.TextEditorDecorationType>();
  private applied = new WeakMap<vscode.TextEditor, Set<vscode.TextEditorDecorationType>>();

  constructor(private readonly catalog: () => IconCatalog, private readonly iconFor: (s: Sym) => string | null) {}

  reset(): void {
    for (const t of this.types.values()) t.dispose();
    this.types.clear();
  }

  clear(editor: vscode.TextEditor): void {
    for (const t of this.applied.get(editor) ?? []) editor.setDecorations(t, []);
    this.applied.delete(editor);
  }

  private color(cfg: RenderConfig, kind: Sym['descriptor']['kind']): string {
    if (cfg.colorMode === 'custom') return cfg.customColor;
    if (cfg.colorMode === 'bySymbolKind') {
      return kind === 'Type' ? cfg.kindColors.types : kind === 'Method' ? cfg.kindColors.methods : cfg.kindColors.properties;
    }
    switch (vscode.window.activeColorTheme.kind) {
      case vscode.ColorThemeKind.Light: return '#333333';
      case vscode.ColorThemeKind.HighContrastLight: return '#000000';
      case vscode.ColorThemeKind.HighContrast: return '#ffffff';
      default: return '#d4d4d4';
    }
  }

  private typeFor(key: string, make: () => vscode.DecorationRenderOptions): vscode.TextEditorDecorationType {
    let t = this.types.get(key);
    if (!t) {
      t = vscode.window.createTextEditorDecorationType(make());
      this.types.set(key, t);
    }
    return t;
  }

  render(editor: vscode.TextEditor, syms: Sym[], cfg: RenderConfig): boolean {
    if (!cfg.enabled) { this.clear(editor); return true; }
    if (this.types.size > MAX_TYPES) return false;

    const doc = editor.document;
    const lines = doc.getText().split(/\r?\n/);
    const tabSize = typeof editor.options.tabSize === 'number' ? editor.options.tabSize : 4;
    const ranges = new Map<vscode.TextEditorDecorationType, vscode.Range[]>();

    for (const s of syms) {
      const icon = this.iconFor(s);
      const def = icon ? this.catalog().get(icon) : undefined;
      if (!def) continue;
      const box = layout(s.band, cfg.minLines, cfg.maxLines, cfg.verticalAlignment);
      if (!box) continue;
      const width = maxLineWidth(lines, box.startLine, box.startLine + box.sizeLines - 1, tabSize);
      const col = columnFor(width, box.sizeLines, cfg);
      if (col === undefined) continue;

      const color = this.color(cfg, s.descriptor.kind);
      const uri = svgToDataUri(iconToSvg(def, { color, opacity: cfg.opacity, strokeWidth: cfg.strokeWidth, filled: cfg.renderStyle === 'filled' }));
      for (let k = 0; k < box.sizeLines; k++) {
        const line = box.startLine + k;
        if (line >= doc.lineCount) break;
        const key = [icon, color, cfg.opacity, cfg.strokeWidth, cfg.renderStyle, box.sizeLines, k, col].join('|');
        const type = this.typeFor(key, () => ({
          isWholeLine: true,
          // The value is injected as raw CSS. It gives the line a background image and nothing else.
          backgroundColor: `transparent;background-image:url("${uri}");background-repeat:no-repeat;`
            + `background-size:${box.sizeLines}lh ${box.sizeLines}lh;background-position:${col}ch calc(-${k} * 1lh)`,
        }));
        const list = ranges.get(type) ?? [];
        list.push(new vscode.Range(line, 0, line, 0));
        ranges.set(type, list);
      }
    }

    const previous = this.applied.get(editor) ?? new Set();
    for (const t of previous) if (!ranges.has(t)) editor.setDecorations(t, []);
    for (const [t, r] of ranges) editor.setDecorations(t, r);
    this.applied.set(editor, new Set(ranges.keys()));
    return true;
  }
}
