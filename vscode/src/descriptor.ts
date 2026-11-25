import { createHash } from 'node:crypto';

export type SymbolKind = 'Type' | 'Method' | 'Property';

/** Everything the model may see about a symbol. It never holds a code body. */
export interface Descriptor {
  kind: SymbolKind;
  name: string;
  container: string;
  /** Last two folder names of the file. A hint only. It is part of the cache key. */
  folder?: string;
  /** Full file path. It finds .codeglyph.json. It is not in the key and never goes to the model. */
  path?: string;
  language: string;
  lineCount: number;
  signature: string;
  comments: string;
}

const normalize = (s: string) => (s ?? '').split(/\s+/).filter(Boolean).join(' ');

/** Stable across files and workspaces. The file path is not part of it. */
export function descriptorKey(d: Descriptor, catalogVersion: string, promptVersion: string): string {
  const text = [catalogVersion, promptVersion, d.kind, d.name, d.container, d.folder ?? '', normalize(d.signature), normalize(d.comments)].join('|');
  return createHash('sha256').update(text, 'utf8').digest('hex');
}
