using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace CodeGlyph
{
    public sealed class GlyphHint
    {
        public GlyphHint(string word, string icon)
        {
            Word = word;
            Icon = icon;
        }

        public string Word { get; }
        public string Icon { get; }
    }

    /// <summary>
    /// Remembers which icon the model chose for each concept word. A word seen twice or more
    /// with one dominant icon becomes a hint, so equal concepts get equal icons.
    /// </summary>
    public sealed class Glossary : IDisposable
    {
        private const int MinCount = 2;
        private const double MinShare = 0.6;
        private const int MaxWords = 5000;

        private readonly object _gate = new object();
        private readonly Dictionary<string, Dictionary<string, int>> _words =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        private readonly string? _path;
        private Timer? _timer;
        private bool _dirty;

        public Glossary(string? path = null)
        {
            _path = path;
            if (path == null || !File.Exists(path)) return;
            try
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(File.ReadAllText(path));
                if (loaded != null)
                    foreach (var kv in loaded) _words[kv.Key] = kv.Value;
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                // A damaged file only costs the hints.
            }
        }

        public void Learn(SymbolDescriptor d, string icon)
        {
            lock (_gate)
            {
                foreach (var w in Vocabulary.ConceptWords(d.Name))
                {
                    if (!_words.TryGetValue(w, out var counts)) _words[w] = counts = new Dictionary<string, int>();
                    counts[icon] = (counts.TryGetValue(icon, out var n) ? n : 0) + 1;
                }
                if (_words.Count > MaxWords)
                {
                    foreach (var w in _words.OrderBy(kv => kv.Value.Values.Sum()).Take(_words.Count - MaxWords).Select(kv => kv.Key).ToList())
                        _words.Remove(w);
                }
                _dirty = true;
                _timer ??= new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
                _timer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>Hints for the words of these symbols, strongest first, at most max.</summary>
        public IReadOnlyList<GlyphHint> Hints(IEnumerable<SymbolDescriptor> descriptors, int max = 30)
        {
            var found = new List<(GlyphHint Hint, int Total)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            lock (_gate)
            {
                foreach (var d in descriptors)
                {
                    foreach (var w in Vocabulary.ConceptWords(d.Name))
                    {
                        if (!seen.Add(w) || !_words.TryGetValue(w, out var counts)) continue;
                        var total = counts.Values.Sum();
                        var top = counts.OrderByDescending(kv => kv.Value).First();
                        if (total >= MinCount && (double)top.Value / total >= MinShare)
                            found.Add((new GlyphHint(w, top.Key), total));
                    }
                }
            }
            return found.OrderByDescending(f => f.Total).Take(max).Select(f => f.Hint).ToList();
        }

        public void Save()
        {
            if (_path == null) return;
            string json;
            lock (_gate)
            {
                if (!_dirty) return;
                json = JsonSerializer.Serialize(_words);
                _dirty = false;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, json);
            }
            catch (IOException)
            {
                lock (_gate) _dirty = true;
            }
        }

        public void Dispose()
        {
            lock (_gate) _timer?.Dispose();
        }
    }
}
