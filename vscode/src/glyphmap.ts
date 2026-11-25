import * as vscode from 'vscode';
import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { iconToSvg, type IconCatalog } from './catalog';
import { buildEntries, type MapEntry } from './mapentries';
import type { Sym } from './symbols';

/** A tree of the icons in the active file. Each row shows its icon, and a click goes to the symbol. */
export class GlyphMap implements vscode.TreeDataProvider<MapEntry> {
  private readonly emitter = new vscode.EventEmitter<MapEntry | undefined>();
  readonly onDidChangeTreeData = this.emitter.event;
  private roots: MapEntry[] = [];
  view: vscode.TreeView<MapEntry> | undefined;

  constructor(private readonly dir: string, private readonly catalog: () => IconCatalog | undefined) {}

  setSymbols(uri: string | undefined, syms: Sym[], iconFor: (s: Sym) => string | null): void {
    this.roots = uri ? buildEntries(uri, syms, iconFor) : [];
    if (this.view) {
      this.view.message = this.roots.length ? undefined : 'No icons in this file yet. They appear when the model has answered.';
    }
    this.emitter.fire(undefined);
  }

  getChildren(e?: MapEntry): MapEntry[] { return e ? e.children : this.roots; }

  getParent(): undefined { return undefined; }

  getTreeItem(e: MapEntry): vscode.TreeItem {
    const d = e.sym.descriptor;
    const item = new vscode.TreeItem(d.name, e.children.length ? vscode.TreeItemCollapsibleState.Expanded : vscode.TreeItemCollapsibleState.None);
    item.description = e.icon;
    item.tooltip = `${d.kind}${d.container ? ' in ' + d.container : ''}: ${e.icon}`;
    item.contextValue = 'glyph';
    const light = this.svg(e.icon, '#333333');
    const dark = this.svg(e.icon, '#d4d4d4');
    if (light && dark) item.iconPath = { light, dark };
    item.command = { command: 'codeglyph.revealSymbol', title: 'Go to symbol', arguments: [e.uri, e.sym.start] };
    return item;
  }

  /** Writes the icon once as an SVG file, because tree icons must be files. */
  private svg(icon: string, color: string): vscode.Uri | undefined {
    const def = this.catalog()?.get(icon);
    if (!def) return undefined;
    const file = join(this.dir, `${icon}-${color.slice(1)}.svg`);
    try {
      if (!existsSync(file)) {
        mkdirSync(this.dir, { recursive: true });
        writeFileSync(file, iconToSvg(def, { color, opacity: 1, strokeWidth: 1.5, filled: false }));
      }
      return vscode.Uri.file(file);
    } catch {
      return undefined;
    }
  }
}
