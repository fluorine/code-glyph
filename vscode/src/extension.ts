import * as vscode from 'vscode';
import { join } from 'node:path';
import { IconCatalog } from './catalog';
import { IconCache } from './cache';
import { Glossary } from './glossary';
import { SelectionPipeline } from './pipeline';
import { createSelector, type ProviderSettings } from './providers';
import { GlyphRenderer, type RenderConfig } from './render';
import { readSymbols, type Sym } from './symbols';

const MAX_CHARS = 1_500_000;

interface Services {
  catalog: IconCatalog;
  cache: IconCache;
  pipeline: SelectionPipeline;
  renderer: GlyphRenderer;
}

export function activate(context: vscode.ExtensionContext): void {
  const out = vscode.window.createOutputChannel('CodeGlyph');
  const log = (m: string) => out.appendLine(new Date().toLocaleTimeString() + ' ' + m);
  const parsed = new Map<string, Sym[]>();
  const scanTimers = new Map<string, NodeJS.Timeout>();
  const resolveTimers = new Map<string, NodeJS.Timeout>();
  let services: Services | undefined;
  let keys: { anthropic?: string; openai?: string } = {};

  const cfg = () => vscode.workspace.getConfiguration('codeglyph');

  const providerSettings = (): ProviderSettings => ({
    provider: cfg().get('provider', 'auto'),
    anthropicKey: keys.anthropic,
    anthropicModel: cfg().get('anthropicModel', 'claude-haiku-5-5'),
    openAiKey: keys.openai,
    openAiModel: cfg().get('openAiModel', 'gpt-4.1-mini'),
    openAiBaseUrl: cfg().get('openAiBaseUrl', 'https://api.openai.com/v1'),
    claudeCliPath: cfg().get('claudeCliPath', 'claude'),
    claudeCliModel: cfg().get('claudeCliModel', 'haiku'),
  });

  const renderConfig = (): RenderConfig => {
    const c = cfg();
    return {
      enabled: c.get('enabled', true),
      colorMode: c.get('colorMode', 'theme'),
      customColor: c.get('customColor', '#808080'),
      kindColors: c.get('kindColors', { types: '#4EC9B0', methods: '#DCDCAA', properties: '#9CDCFE' }),
      opacity: c.get('opacity', 0.08),
      renderStyle: c.get('renderStyle', 'outline'),
      strokeWidth: c.get('strokeWidth', 2),
      placement: c.get('placement', 'fixedColumn'),
      column: c.get('column', 100),
      overlapCode: c.get('overlapCode', true),
      verticalAlignment: c.get('verticalAlignment', 'center'),
      minLines: c.get('minLines', 4),
      maxLines: c.get('maxLines', 10),
      fadeInMs: c.get('fadeInMs', 700),
      verbBadges: c.get('verbBadges', true),
    };
  };

  // Nothing heavy loads at startup. The icon set, cache and provider are created on the first scan.
  const getServices = async (): Promise<Services> => {
    if (services) return services;
    keys = {
      anthropic: await context.secrets.get('codeglyph.anthropicKey'),
      openai: await context.secrets.get('codeglyph.openAiKey'),
    };
    const catalog = IconCatalog.load(join(context.extensionPath, 'resources', 'icons.json.gz'));
    const cache = new IconCache(join(context.globalStorageUri.fsPath, 'selection-cache.json'));
    const pipeline = new SelectionPipeline(catalog, cache, () => createSelector(providerSettings()), {
      minConfidence: () => cfg().get('minConfidence', 0.7),
      minSymbolLines: () => cfg().get('minSymbolLines', 4),
      logUsage: () => cfg().get('logUsage', true),
      log,
      glossary: new Glossary(join(context.globalStorageUri.fsPath, 'glossary.json')),
    });
    const renderer = new GlyphRenderer(() => catalog, s => pipeline.lookup(s.descriptor), () => renderAll());
    pipeline.onResolved(() => renderAll());
    services = { catalog, cache, pipeline, renderer };
    return services;
  };

  const eligible = (doc: vscode.TextDocument): boolean => {
    if (doc.uri.scheme !== 'file' && doc.uri.scheme !== 'untitled' && doc.uri.scheme !== 'vscode-remote') return false;
    if (doc.getText().length > MAX_CHARS) return false;
    return !cfg().get<string[]>('excludedLanguages', []).includes(doc.languageId);
  };

  const lensLines = new Map<string, Set<number>>();
  const lastLensCount = new Map<string, number>();

  // Height in pixels of the row VS Code adds above a line for a CodeLens. It follows the editor's own rule.
  const lensRowPx = (doc: vscode.TextDocument): number => {
    const c = vscode.workspace.getConfiguration('editor', doc);
    const fontSize = c.get<number>('fontSize', 14);
    const configured = c.get<number>('lineHeight', 0);
    const lineHeight = configured === 0 ? Math.round(1.35 * fontSize) : configured < 8 ? Math.round(configured * fontSize) : configured;
    const lensFont = c.get<number>('codeLensFontSize', 0);
    const size = lensFont >= 5 ? lensFont : Math.floor(fontSize * 0.9);
    return Math.floor(size * Math.max(1.3, lineHeight / fontSize));
  };

  const render = (editor: vscode.TextEditor) => {
    if (!services) return;
    const draw = (e: vscode.TextEditor) => services!.renderer.render(
      e, parsed.get(e.document.uri.toString()) ?? [], renderConfig(), lensLines.get(e.document.uri.toString()), lensRowPx(e.document));
    if (!draw(editor)) {
      services.renderer.reset();
      for (const e of vscode.window.visibleTextEditors) draw(e);
    }
  };

  // Each CodeLens adds a row above its line. The icon must include it, or it looks broken.
  const readLensLines = async (doc: vscode.TextDocument): Promise<Set<number>> => {
    const lines = new Set<number>();
    if (!vscode.workspace.getConfiguration('editor', doc).get('codeLens', true)) return lines;
    try {
      const lenses = await vscode.commands.executeCommand<vscode.CodeLens[]>('vscode.executeCodeLensProvider', doc.uri);
      for (const l of lenses ?? []) lines.add(l.range.start.line);
    } catch {
      // No CodeLens provider.
    }
    return lines;
  };

  const renderAll = () => { for (const e of vscode.window.visibleTextEditors) render(e); };

  const scan = async (doc: vscode.TextDocument, immediate: boolean, attempt = 0): Promise<void> => {
    if (!cfg().get('enabled', true) || !eligible(doc)) return;
    const key = doc.uri.toString();
    try {
      const s = await getServices();
      const show = cfg().get<string[]>('showFor', ['types', 'methods', 'properties']);
      const syms = await readSymbols(doc, {
        types: show.includes('types'), methods: show.includes('methods'), properties: show.includes('properties'),
      }, cfg().get('minSymbolLines', 4));
      if (!parsed.has(key)) log(`${doc.fileName.split(/[\\/]/).pop()}: ${syms.length} symbols`);
      parsed.set(key, syms);
      const lenses = await readLensLines(doc);
      lensLines.set(key, lenses);
      if (lenses.size !== (lastLensCount.get(key) ?? -1)) { lastLensCount.set(key, lenses.size); log(`${doc.fileName.split(/[\/]/).pop()}: ${lenses.size} CodeLens lines`); }
      for (const e of vscode.window.visibleTextEditors) if (e.document === doc) render(e);

      // CodeLens providers are often slow to start. Look again when none showed up.
      const lensWanted = vscode.workspace.getConfiguration('editor', doc).get('codeLens', true);
      if (syms.length && lensWanted && !lenses.size && attempt < 3) {
        setTimeout(() => void scan(doc, true, attempt + 1), [3000, 10000, 30000][attempt]);
      }

      // A language server may need time to start. Try a few more times when it gave nothing.
      if (!syms.length && attempt < 5 && doc.lineCount > 4) {
        setTimeout(() => void scan(doc, immediate, attempt + 1), [2000, 6000, 15000, 30000, 60000][attempt]);
        return;
      }

      // Cached icons are drawn above. Only cache misses wait for the edit delay.
      clearTimeout(resolveTimers.get(key));
      const delay = immediate ? 0 : cfg().get('editDelaySeconds', 15) * 1000;
      const run = () => void s.pipeline.resolve(syms.map(x => x.descriptor)).catch(e => log('CodeGlyph failed: ' + (e as Error).message));
      if (delay <= 0) run(); else resolveTimers.set(key, setTimeout(run, delay));
    } catch (e) {
      log('CodeGlyph failed for ' + doc.fileName + ': ' + (e as Error).message);
    }
  };

  const scanVisible = (immediate: boolean) => { for (const e of vscode.window.visibleTextEditors) void scan(e.document, immediate); };

  context.subscriptions.push(
    out,
    vscode.window.onDidChangeVisibleTextEditors(() => scanVisible(true)),
    vscode.workspace.onDidChangeTextDocument(ev => {
      if (!ev.contentChanges.length) return;
      const key = ev.document.uri.toString();
      clearTimeout(scanTimers.get(key));
      scanTimers.set(key, setTimeout(() => void scan(ev.document, false), 500));
    }),
    vscode.workspace.onDidCloseTextDocument(d => { parsed.delete(d.uri.toString()); lensLines.delete(d.uri.toString()); }),
    // Diagnostics arrive when a language server is ready. Scan again for files that had no symbols.
    vscode.languages.onDidChangeDiagnostics(ev => {
      for (const uri of ev.uris) {
        const key = uri.toString();
        if ((parsed.get(key)?.length ?? 0) > 0) continue;
        const doc = vscode.window.visibleTextEditors.find(e => e.document.uri.toString() === key)?.document;
        if (!doc) continue;
        clearTimeout(scanTimers.get('diag:' + key));
        scanTimers.set('diag:' + key, setTimeout(() => void scan(doc, true), 700));
      }
    }),
    vscode.window.onDidChangeActiveColorTheme(() => { services?.renderer.reset(); renderAll(); }),
    vscode.workspace.onDidChangeConfiguration(ev => {
      if (!ev.affectsConfiguration('codeglyph')) return;
      services?.renderer.reset();
      if (!cfg().get('enabled', true)) { for (const e of vscode.window.visibleTextEditors) services?.renderer.clear(e); return; }
      scanVisible(true);
    }),
    vscode.commands.registerCommand('codeglyph.toggle', () => {
      void cfg().update('enabled', !cfg().get('enabled', true), vscode.ConfigurationTarget.Global);
    }),
    vscode.commands.registerCommand('codeglyph.clearCache', async () => {
      const s = await getServices();
      s.cache.clear();
      s.renderer.reset();
      renderAll();
      scanVisible(true);
      log('Icon cache cleared.');
    }),
    vscode.commands.registerCommand('codeglyph.showUsage', async () => {
      const s = await getServices();
      log(s.pipeline.usage.summary());
      out.show(true);
    }),
    vscode.commands.registerCommand('codeglyph.setAnthropicKey', () => setKey('codeglyph.anthropicKey', 'Claude API key', k => (keys.anthropic = k))),
    vscode.commands.registerCommand('codeglyph.setOpenAiKey', () => setKey('codeglyph.openAiKey', 'OpenAI API key', k => (keys.openai = k))),
  );

  async function setKey(id: string, label: string, apply: (k: string | undefined) => void): Promise<void> {
    const value = await vscode.window.showInputBox({ prompt: `${label}. Stored in VS Code secret storage. Leave empty to remove it.`, password: true, ignoreFocusOut: true });
    if (value === undefined) return;
    if (value.trim()) await context.secrets.store(id, value.trim()); else await context.secrets.delete(id);
    apply(value.trim() || undefined);
    scanVisible(true);
  }

  scanVisible(true);
}

export function deactivate(): void {}

