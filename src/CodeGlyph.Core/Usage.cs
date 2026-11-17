using System;
using System.Globalization;

namespace CodeGlyph
{
    /// <summary>Tokens and cost reported by one provider call. A value of 0 means not reported.</summary>
    public sealed class SelectorUsage
    {
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public long CacheReadTokens { get; set; }
        public long CacheWriteTokens { get; set; }
        public double CostUsd { get; set; }
    }

    /// <summary>Implemented by selectors that can report usage after each call.</summary>
    public interface IReportsUsage
    {
        Action<SelectorUsage>? UsageReported { get; set; }
    }

    /// <summary>Running totals for the current session. Thread safe.</summary>
    public sealed class UsageTracker
    {
        private readonly object _gate = new object();
        private int _calls;
        private int _symbols;
        private long _in, _out, _cacheRead, _cacheWrite;
        private double _cost;

        public string Add(string provider, int symbols, SelectorUsage? u)
        {
            u = u ?? new SelectorUsage();
            lock (_gate)
            {
                _calls++;
                _symbols += symbols;
                _in += u.InputTokens;
                _out += u.OutputTokens;
                _cacheRead += u.CacheReadTokens;
                _cacheWrite += u.CacheWriteTokens;
                _cost += u.CostUsd;
            }
            return provider + ": " + symbols + " symbols, " + Describe(u.InputTokens, u.OutputTokens,
                u.CacheReadTokens, u.CacheWriteTokens, u.CostUsd);
        }

        public string Summary()
        {
            lock (_gate)
            {
                return "Session total: " + _calls + " calls, " + _symbols + " symbols, "
                    + Describe(_in, _out, _cacheRead, _cacheWrite, _cost);
            }
        }

        private static string Describe(long input, long output, long cacheRead, long cacheWrite, double cost)
        {
            var s = "in " + input + ", out " + output + " tokens";
            if (cacheRead > 0 || cacheWrite > 0) s += " (cache read " + cacheRead + ", write " + cacheWrite + ")";
            if (cost > 0) s += ", cost $" + cost.ToString("0.0000", CultureInfo.InvariantCulture);
            return s;
        }
    }
}
