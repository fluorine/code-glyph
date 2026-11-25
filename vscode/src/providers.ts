import { spawn } from 'node:child_process';
import { tmpdir } from 'node:os';
import type { Hint } from './glossary';
import { buildUser, parseResponse, type Answer, type Request } from './prompt';
import { emptyUsage, type Usage } from './usage';

export interface SelectResult { answers: Answer[]; usage: Usage; }

export interface Selector {
  readonly name: string;
  /** True when the provider has no prompt cache. It gets a short icon list for each batch. */
  readonly shortlist?: boolean;
  select(system: string, requests: Request[], signal?: AbortSignal, hints?: Hint[]): Promise<SelectResult>;
}

export type ProviderKind = 'auto' | 'anthropic' | 'openai' | 'claudeCli';

export interface ProviderSettings {
  provider: ProviderKind;
  anthropicKey?: string;
  anthropicModel: string;
  openAiKey?: string;
  openAiModel: string;
  openAiBaseUrl: string;
  claudeCliPath: string;
  claudeCliModel: string;
}

const first = (...v: (string | undefined)[]) => v.map(x => x?.trim()).find(x => !!x);
const shorten = (s: string) => (s.length <= 300 ? s : s.slice(0, 300));
const num = (v: unknown) => (typeof v === 'number' ? v : 0);

/** Returns undefined when the chosen provider has no usable credentials. */
export function createSelector(s: ProviderSettings, env: NodeJS.ProcessEnv = process.env): Selector | undefined {
  const anthropicKey = first(s.anthropicKey, env.ANTHROPIC_API_KEY);
  const openAiKey = first(s.openAiKey, env.OPENAI_API_KEY);
  switch (s.provider) {
    case 'anthropic': return anthropicKey ? new AnthropicSelector(anthropicKey, s.anthropicModel) : undefined;
    case 'openai': return openAiKey ? new OpenAiSelector(openAiKey, s.openAiModel, s.openAiBaseUrl) : undefined;
    case 'claudeCli': return new ClaudeCliSelector(s.claudeCliPath, s.claudeCliModel);
    default:
      if (anthropicKey) return new AnthropicSelector(anthropicKey, s.anthropicModel);
      if (openAiKey) return new OpenAiSelector(openAiKey, s.openAiModel, s.openAiBaseUrl);
      return new ClaudeCliSelector(s.claudeCliPath, s.claudeCliModel);
  }
}

export class AnthropicSelector implements Selector {
  constructor(private readonly key: string, private readonly model: string) {}
  get name() { return 'Anthropic ' + this.model; }

  async select(system: string, requests: Request[], signal?: AbortSignal, hints?: Hint[]): Promise<SelectResult> {
    const resp = await fetch('https://api.anthropic.com/v1/messages', {
      method: 'POST', signal,
      headers: { 'content-type': 'application/json', 'x-api-key': this.key, 'anthropic-version': '2023-06-01' },
      body: JSON.stringify({
        model: this.model, max_tokens: 2048,
        system: [{ type: 'text', text: system, cache_control: { type: 'ephemeral' } }],
        messages: [{ role: 'user', content: buildUser(requests, hints) }],
      }),
    });
    const text = await resp.text();
    if (!resp.ok) throw new Error(`Anthropic returned ${resp.status}: ${shorten(text)}`);
    const json = JSON.parse(text);
    const u = json.usage ?? {};
    const content = (json.content ?? []).filter((b: { type: string }) => b.type === 'text').map((b: { text: string }) => b.text).join('');
    return {
      answers: parseResponse(content),
      usage: { ...emptyUsage(), inputTokens: num(u.input_tokens), outputTokens: num(u.output_tokens),
        cacheRead: num(u.cache_read_input_tokens), cacheWrite: num(u.cache_creation_input_tokens) },
    };
  }
}

export class OpenAiSelector implements Selector {
  private readonly base: string;
  constructor(private readonly key: string, private readonly model: string, baseUrl: string) { this.base = baseUrl.replace(/\/+$/, ''); }
  get name() { return 'OpenAI ' + this.model; }
  readonly shortlist = true;

  async select(system: string, requests: Request[], signal?: AbortSignal, hints?: Hint[]): Promise<SelectResult> {
    const resp = await fetch(this.base + '/chat/completions', {
      method: 'POST', signal,
      headers: { 'content-type': 'application/json', authorization: 'Bearer ' + this.key },
      body: JSON.stringify({
        model: this.model, temperature: 0, response_format: { type: 'json_object' },
        messages: [{ role: 'system', content: system }, { role: 'user', content: buildUser(requests, hints) }],
      }),
    });
    const text = await resp.text();
    if (!resp.ok) throw new Error(`OpenAI returned ${resp.status}: ${shorten(text)}`);
    const json = JSON.parse(text);
    const u = json.usage ?? {};
    return {
      answers: parseResponse(json.choices?.[0]?.message?.content ?? ''),
      usage: { ...emptyUsage(), inputTokens: num(u.prompt_tokens), outputTokens: num(u.completion_tokens) },
    };
  }
}

/** One call at a time. The icon list is far over the command-line limit, so the prompt goes on stdin. */
export class ClaudeCliSelector implements Selector {
  private static chain: Promise<unknown> = Promise.resolve();
  constructor(private readonly path: string, private readonly model: string) {}
  get name() { return 'Claude CLI ' + this.model; }
  readonly shortlist = true;

  select(system: string, requests: Request[], signal?: AbortSignal, hints?: Hint[]): Promise<SelectResult> {
    const run = ClaudeCliSelector.chain.then(() => this.run(system, requests, signal, hints));
    ClaudeCliSelector.chain = run.catch(() => undefined);
    return run;
  }

  private run(system: string, requests: Request[], signal?: AbortSignal, hints?: Hint[]): Promise<SelectResult> {
    return new Promise((resolve, reject) => {
      const args = ['-p', '--output-format', 'json', '--model', this.model, '--tools', '', '--no-session-persistence',
        '--system-prompt', 'You answer with JSON only.'];
      const proc = spawn(this.path || 'claude', args, {
        cwd: tmpdir(), env: { ...process.env, CLAUDE_CODE_ENTRYPOINT: 'codeglyph' }, windowsHide: true,
      });
      let out = '', err = '';
      proc.stdout.setEncoding('utf8').on('data', d => (out += d));
      proc.stderr.setEncoding('utf8').on('data', d => (err += d));
      proc.on('error', e => reject(new Error(`Cannot start '${this.path}'. Install Claude Code or set its path. ${e.message}`)));
      signal?.addEventListener('abort', () => proc.kill());
      proc.on('close', code => {
        if (code !== 0) return reject(new Error(`claude exited with ${code}: ${shorten(err)}`));
        try {
          const json = JSON.parse(out);
          const u = json.usage ?? {};
          resolve({
            answers: parseResponse(typeof json.result === 'string' ? json.result : out),
            usage: { ...emptyUsage(), inputTokens: num(u.input_tokens), outputTokens: num(u.output_tokens),
              cacheRead: num(u.cache_read_input_tokens), cacheWrite: num(u.cache_creation_input_tokens), costUsd: num(json.total_cost_usd) },
          });
        } catch (e) { reject(e as Error); }
      });
      proc.stdin.end(system + '\n\nSYMBOLS (JSON):\n' + buildUser(requests));
    });
  }
}
