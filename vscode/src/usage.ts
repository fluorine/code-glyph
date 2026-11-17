export interface Usage { inputTokens: number; outputTokens: number; cacheRead: number; cacheWrite: number; costUsd: number; }

export const emptyUsage = (): Usage => ({ inputTokens: 0, outputTokens: 0, cacheRead: 0, cacheWrite: 0, costUsd: 0 });

const describe = (u: Usage) =>
  `in ${u.inputTokens}, out ${u.outputTokens} tokens`
  + (u.cacheRead || u.cacheWrite ? ` (cache read ${u.cacheRead}, write ${u.cacheWrite})` : '')
  + (u.costUsd > 0 ? `, cost $${u.costUsd.toFixed(4)}` : '');

/** Running totals for the current session. */
export class UsageTracker {
  private calls = 0;
  private symbols = 0;
  private total = emptyUsage();

  add(provider: string, symbols: number, u: Usage): string {
    this.calls++;
    this.symbols += symbols;
    this.total = {
      inputTokens: this.total.inputTokens + u.inputTokens,
      outputTokens: this.total.outputTokens + u.outputTokens,
      cacheRead: this.total.cacheRead + u.cacheRead,
      cacheWrite: this.total.cacheWrite + u.cacheWrite,
      costUsd: this.total.costUsd + u.costUsd,
    };
    return `${provider}: ${symbols} symbols, ${describe(u)}`;
  }

  summary(): string {
    return `Session total: ${this.calls} calls, ${this.symbols} symbols, ${describe(this.total)}`;
  }
}
