using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CodeGlyph
{
    /// <summary>One entry of .codeglyph.json. A null Icon hides the icon. Container and Kind are optional.</summary>
    public sealed class OverrideEntry
    {
        public string Name { get; set; } = "";
        public string? Container { get; set; }
        public SymbolKind? Kind { get; set; }
        public string? Icon { get; set; }

        internal int Specificity => (Container == null ? 0 : 1) + (Kind == null ? 0 : 1);
    }

    /// <summary>The file format is shared with the VS Code extension.</summary>
    public static class Overrides
    {
        public const string FileName = ".codeglyph.json";

        /// <summary>Bad entries are skipped, so a typo never breaks the editor.</summary>
        public static IReadOnlyList<OverrideEntry> Parse(string text)
        {
            var list = new List<OverrideEntry>();
            try
            {
                using (var doc = JsonDocument.Parse(text))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object
                        || !doc.RootElement.TryGetProperty("overrides", out var array)
                        || array.ValueKind != JsonValueKind.Array) return list;
                    foreach (var e in array.EnumerateArray())
                    {
                        if (e.ValueKind != JsonValueKind.Object) continue;
                        if (!e.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(n.GetString())) continue;
                        if (!e.TryGetProperty("icon", out var i) || (i.ValueKind != JsonValueKind.Null && i.ValueKind != JsonValueKind.String)) continue;

                        var entry = new OverrideEntry { Name = n.GetString()! };
                        var icon = i.ValueKind == JsonValueKind.String ? i.GetString()!.Trim() : null;
                        entry.Icon = icon == null || icon.Equals("none", StringComparison.OrdinalIgnoreCase)
                            || icon.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : icon;
                        if (e.TryGetProperty("container", out var c) && c.ValueKind == JsonValueKind.String) entry.Container = c.GetString();
                        if (e.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
                            && Enum.TryParse<SymbolKind>(k.GetString(), out var kind)
                            && (kind == SymbolKind.Type || kind == SymbolKind.Method || kind == SymbolKind.Property))
                            entry.Kind = kind;
                        list.Add(entry);
                    }
                }
            }
            catch (JsonException)
            {
                list.Clear();
            }
            return list;
        }

        /// <summary>The most specific match wins. A later entry wins a tie.</summary>
        public static OverrideEntry? Match(IReadOnlyList<OverrideEntry> list, SymbolDescriptor d)
        {
            OverrideEntry? best = null;
            foreach (var o in list)
            {
                var hit = o.Name == d.Name
                    && (o.Container == null || o.Container == d.Container)
                    && (o.Kind == null || o.Kind == d.Kind);
                if (hit && (best == null || o.Specificity >= best.Specificity)) best = o;
            }
            return best;
        }
    }

    /// <summary>Finds the nearest .codeglyph.json above a source file. It checks the file for changes every few seconds.</summary>
    public sealed class OverrideStore
    {
        private sealed class Loaded
        {
            public string? File;
            public DateTime Mtime;
            public IReadOnlyList<OverrideEntry> List = new List<OverrideEntry>();
            public DateTime Checked;
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, Loaded> _byDir = new Dictionary<string, Loaded>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<DateTime> _now;
        private readonly TimeSpan _recheck;

        public OverrideStore(Func<DateTime>? now = null, TimeSpan? recheck = null)
        {
            _now = now ?? (() => DateTime.UtcNow);
            _recheck = recheck ?? TimeSpan.FromSeconds(3);
        }

        public string? Find(string startPath)
        {
            try
            {
                for (var dir = Path.GetDirectoryName(startPath); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
                {
                    var candidate = Path.Combine(dir, Overrides.FileName);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch (ArgumentException)
            {
                // A path with bad characters has no overrides.
            }
            return null;
        }

        /// <summary>False: no override. True with a null icon: the icon is hidden.</summary>
        public bool TryGet(SymbolDescriptor d, out string? icon)
        {
            icon = null;
            if (string.IsNullOrEmpty(d.Path)) return false;
            IReadOnlyList<OverrideEntry> list;
            lock (_gate) list = Load(d.Path).List;
            var match = Overrides.Match(list, d);
            if (match == null) return false;
            icon = match.Icon;
            return true;
        }

        private Loaded Load(string startPath)
        {
            var dir = Path.GetDirectoryName(startPath) ?? "";
            var now = _now();
            if (_byDir.TryGetValue(dir, out var cached) && now - cached.Checked < _recheck) return cached;

            var file = Find(startPath);
            var mtime = DateTime.MinValue;
            try { if (file != null) mtime = File.GetLastWriteTimeUtc(file); }
            catch (IOException) { }
            if (cached != null && cached.File == file && cached.Mtime == mtime)
            {
                cached.Checked = now;
                return cached;
            }

            IReadOnlyList<OverrideEntry> list = new List<OverrideEntry>();
            try { if (file != null) list = Overrides.Parse(File.ReadAllText(file)); }
            catch (IOException) { }
            var loaded = new Loaded { File = file, Mtime = mtime, List = list, Checked = now };
            _byDir[dir] = loaded;
            return loaded;
        }
    }
}
