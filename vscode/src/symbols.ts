import * as vscode from 'vscode';
import type { Descriptor, SymbolKind } from './descriptor';
import { extractComments, isEligibleName } from './comments';
import { largestFree, type Band } from './placement';

export interface Sym {
  descriptor: Descriptor;
  band: Band;
  start: number;
  end: number;
  sigLine: number;
}

export type ShowFor = { types: boolean; methods: boolean; properties: boolean };

interface Node {
  name: string;
  detail: string;
  kind: vscode.SymbolKind;
  start: number;
  end: number;
  sigLine: number;
  children: Node[];
}

const K = vscode.SymbolKind;
const TYPE_KINDS = new Set<vscode.SymbolKind>([K.Class, K.Interface, K.Struct, K.Enum]);
const METHOD_KINDS = new Set<vscode.SymbolKind>([K.Method, K.Function]);
const PROPERTY_KINDS = new Set<vscode.SymbolKind>([K.Property]);

function kindOf(k: vscode.SymbolKind): SymbolKind | undefined {
  if (TYPE_KINDS.has(k)) return 'Type';
  if (METHOD_KINDS.has(k)) return 'Method';
  if (PROPERTY_KINDS.has(k)) return 'Property';
  return undefined;
}

function toNodes(raw: Array<vscode.DocumentSymbol | vscode.SymbolInformation>): Node[] {
  if (!raw.length) return [];
  if ('selectionRange' in raw[0]) {
    const conv = (s: vscode.DocumentSymbol): Node => ({
      name: s.name, detail: s.detail ?? '', kind: s.kind, start: s.range.start.line, end: s.range.end.line,
      sigLine: s.selectionRange.start.line, children: s.children.map(conv),
    });
    return (raw as vscode.DocumentSymbol[]).map(conv);
  }
  // Flat list: nest by line containment.
  const flat = (raw as vscode.SymbolInformation[])
    .map(s => ({ name: s.name, detail: '', kind: s.kind, start: s.location.range.start.line, end: s.location.range.end.line, sigLine: s.location.range.start.line, children: [] as Node[] }))
    .sort((a, b) => a.start - b.start || b.end - a.end);
  const roots: Node[] = [];
  const stack: Node[] = [];
  for (const n of flat) {
    while (stack.length && stack[stack.length - 1].end < n.start) stack.pop();
    (stack.length ? stack[stack.length - 1].children : roots).push(n);
    stack.push(n);
  }
  return roots;
}

export async function readSymbols(doc: vscode.TextDocument, show: ShowFor, minSymbolLines: number): Promise<Sym[]> {
  const raw = await vscode.commands.executeCommand<Array<vscode.DocumentSymbol | vscode.SymbolInformation>>(
    'vscode.executeDocumentSymbolProvider', doc.uri);
  if (!raw || !raw.length) return [];

  const lines = doc.getText().split(/\r?\n/);
  const folder = doc.uri.path.split('/').slice(0, -1).slice(-2).join('/');
  const out: Sym[] = [];
  const allowed = (k: SymbolKind) => (k === 'Type' ? show.types : k === 'Method' ? show.methods : show.properties);

  const walk = (nodes: Node[], path: string[]) => {
    for (const n of nodes) {
      const kind = kindOf(n.kind);
      const lineCount = n.end - n.start + 1;
      if (kind && allowed(kind) && lineCount >= minSymbolLines && isEligibleName(n.name, kind)) {
        const covered: Band[] = n.children
          .filter(c => { const ck = kindOf(c.kind); return ck && allowed(ck) && c.end - c.start + 1 >= minSymbolLines; })
          .map(c => ({ start: c.start, end: c.end }));
        const band = largestFree(n.start, n.end, covered);
        if (band) {
          out.push({
            start: n.start, end: n.end, sigLine: n.sigLine, band,
            descriptor: {
              kind, name: n.name, container: path.join('.'), folder, language: doc.languageId, lineCount,
              signature: n.detail.trim().slice(0, 200),
              comments: extractComments(lines, n.sigLine, n.sigLine),
            },
          });
        }
      }
      const isScope = kind === 'Type' || n.kind === K.Namespace || n.kind === K.Module || n.kind === K.Package;
      walk(n.children, isScope ? [...path, n.name] : path);
    }
  };
  walk(toNodes(raw), []);
  return out;
}
