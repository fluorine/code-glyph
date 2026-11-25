using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeGlyph.Selectors;

namespace CodeGlyph
{
    public sealed class SelectionPipeline
    {
        private const int BatchSize = 12;

        private static readonly HashSet<string> SkippedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "ToString", "Equals", "GetHashCode", "Dispose", "DisposeAsync", "Main", "Clone", "CompareTo",
        };

        private readonly IconCatalog _catalog;
        private readonly IconCache _cache;
        private readonly Func<IIconSelector?> _selector;
        private readonly Func<GlyphStyle> _style;
        private readonly Action<string> _log;
        private readonly Func<DateTime> _now;
        private readonly Func<bool> _logUsage;
        private readonly object _gate = new object();
        private readonly HashSet<string> _inFlight = new HashSet<string>();
        private DateTime _blockedUntil = DateTime.MinValue;
        private TimeSpan _backoff = TimeSpan.Zero;
        private string? _systemPrompt;

        public SelectionPipeline(
            IconCatalog catalog, IconCache cache, Func<IIconSelector?> selector,
            Func<GlyphStyle> style, Action<string> log, Func<DateTime>? now = null,
            Func<bool>? logUsage = null)
        {
            _logUsage = logUsage ?? (() => true);
            _catalog = catalog;
            _cache = cache;
            _selector = selector;
            _style = style;
            _log = log;
            _now = now ?? (() => DateTime.UtcNow);
        }

        /// <summary>Raised after new answers are cached, so views can draw again.</summary>
        public event Action? Resolved;

        public UsageTracker Usage { get; } = new UsageTracker();

        public string KeyOf(SymbolDescriptor d) => d.Key(_catalog.Version, SelectionPrompt.Version);

        /// <summary>The icon to draw now, or null for none or not yet known.</summary>
        public IconDefinition? Lookup(SymbolDescriptor d)
        {
            if (!_cache.TryGet(KeyOf(d), out var choice) || choice.Icon == null) return null;
            return _catalog.TryGet(choice.Icon, out var icon) ? icon : null;
        }

        public bool ShouldRequest(SymbolDescriptor d)
        {
            var style = _style();
            if (!style.Shows(d.Kind)) return false;
            if (d.LineCount < style.MinSymbolLines) return false;
            if (SkippedNames.Contains(d.Name)) return false;
            return !string.IsNullOrWhiteSpace(d.Name);
        }

        /// <summary>Asks the provider for every uncached, eligible descriptor. Never throws on provider errors.</summary>
        public async Task ResolveAsync(IEnumerable<SymbolDescriptor> descriptors, CancellationToken ct)
        {
            if (_now() < _blockedUntil) return;

            var pending = new List<SelectionRequest>();
            var keyById = new Dictionary<string, string>();
            lock (_gate)
            {
                foreach (var d in descriptors)
                {
                    if (!ShouldRequest(d)) continue;
                    var key = KeyOf(d);
                    if (_cache.TryGet(key, out _) || _inFlight.Contains(key)) continue;
                    _inFlight.Add(key);
                    var id = pending.Count.ToString();
                    pending.Add(new SelectionRequest(id, d));
                    keyById[id] = key;
                }
            }
            if (pending.Count == 0) return;

            var selector = _selector();
            if (selector == null)
            {
                Release(keyById.Values);
                _log("No provider is configured. Set a key in Tools > Options > CodeGlyph, or install the claude CLI.");
                return;
            }

            _systemPrompt = _systemPrompt ?? SelectionPrompt.BuildSystem(_catalog);
            var anyCached = false;
            try
            {
                for (var i = 0; i < pending.Count; i += BatchSize)
                {
                    ct.ThrowIfCancellationRequested();
                    var batch = pending.Skip(i).Take(BatchSize).ToList();
                    try
                    {
                        var answers = await AskAsync(selector, batch, ct).ConfigureAwait(false);
                        var missed = Apply(batch, keyById, answers, final: false);
                        anyCached |= missed.Count < batch.Count;
                        if (missed.Count > 0)
                        {
                            var again = await AskAsync(selector, missed, ct).ConfigureAwait(false);
                            Apply(missed, keyById, again, final: true);
                            anyCached = true;
                        }
                        _backoff = TimeSpan.Zero;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _backoff = _backoff == TimeSpan.Zero
                            ? TimeSpan.FromSeconds(30)
                            : TimeSpan.FromSeconds(Math.Min(600, _backoff.TotalSeconds * 2));
                        _blockedUntil = _now() + _backoff;
                        _log(selector.Name + " failed: " + ex.Message + " Retry in " + (int)_backoff.TotalSeconds + " s.");
                        break;
                    }
                }
            }
            finally
            {
                Release(keyById.Values);
                if (anyCached)
                {
                    _cache.Save();
                    Resolved?.Invoke();
                }
            }
        }

        private async Task<IReadOnlyList<IconSelection>> AskAsync(IIconSelector selector, List<SelectionRequest> batch, CancellationToken ct)
        {
            SelectorUsage? usage = null;
            if (selector is IReportsUsage reporter) reporter.UsageReported = u => usage = u;
            var answers = await selector.SelectAsync(_systemPrompt!, batch, ct).ConfigureAwait(false);
            var line = Usage.Add(selector.Name, batch.Count, usage);
            if (_logUsage()) _log(line + ". " + Usage.Summary());
            // An empty reply is a damaged reply. It must not become a cached "none".
            if (answers.Count == 0) throw new InvalidOperationException("The reply had no usable answers.");
            return answers;
        }

        /// <summary>Caches each answer. A request with no answer is cached as "none" only when final, else it is returned.</summary>
        private List<SelectionRequest> Apply(List<SelectionRequest> batch, Dictionary<string, string> keyById, IReadOnlyList<IconSelection> answers, bool final)
        {
            var min = _style().MinConfidence;
            var byId = answers.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
            var missed = new List<SelectionRequest>();
            foreach (var request in batch)
            {
                if (!byId.TryGetValue(request.Id, out var answer) && !final)
                {
                    missed.Add(request);
                    continue;
                }
                string? icon = null;
                var confidence = 0.0;
                if (answer != null && answer.Icon != null
                    && _catalog.TryGet(answer.Icon, out _)
                    && answer.Confidence >= min
                    && !IsGeneric(answer.Icon, answer.Confidence))
                {
                    icon = answer.Icon;
                    confidence = answer.Confidence;
                }
                _cache.Set(keyById[request.Id], icon, confidence);
            }
            return missed;
        }

        private static bool IsGeneric(string icon, double confidence) =>
            confidence < 0.9 && Array.IndexOf(SelectionPrompt.GenericIcons, icon) >= 0;

        private void Release(IEnumerable<string> keys)
        {
            lock (_gate)
            {
                foreach (var k in keys) _inFlight.Remove(k);
            }
        }
    }
}
