using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace CodeGlyph
{
    public sealed class CachedChoice
    {
        public string? Icon { get; set; }
        public double Confidence { get; set; }

        /// <summary>Day number (days since 1970) of the last use. 0 means unknown.</summary>
        public int Seen { get; set; }
    }

    /// <summary>Descriptor key to icon choice. A null icon is a cached "none".</summary>
    public sealed class IconCache : IDisposable
    {
        public const int MaxAgeDays = 90;
        public const int MaxEntries = 20000;

        private readonly object _gate = new object();
        private readonly object _io = new object();
        private readonly Dictionary<string, CachedChoice> _entries = new Dictionary<string, CachedChoice>();
        private readonly string? _path;
        private readonly Func<DateTime> _now;
        private Timer? _timer;
        private bool _dirty;

        public IconCache(string? path = null, Func<DateTime>? now = null)
        {
            _path = path;
            _now = now ?? (() => DateTime.UtcNow);
            if (path != null && File.Exists(path))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, CachedChoice>>(File.ReadAllText(path));
                    if (loaded != null)
                    {
                        foreach (var kv in loaded)
                        {
                            if (kv.Value.Seen == 0) kv.Value.Seen = Today();
                            _entries[kv.Key] = kv.Value;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is JsonException)
                {
                    // A damaged cache file only costs a refill.
                }
                lock (_gate) Prune();
            }
        }

        public int Count
        {
            get { lock (_gate) return _entries.Count; }
        }

        private int Today() => (int)(_now().Date - new DateTime(1970, 1, 1)).TotalDays;

        public bool TryGet(string key, out CachedChoice choice)
        {
            bool found;
            var touched = false;
            lock (_gate)
            {
                found = _entries.TryGetValue(key, out choice!);
                if (found && choice.Seen != Today())
                {
                    choice.Seen = Today();
                    _dirty = true;
                    touched = true;
                }
            }
            if (touched) SaveSoon();
            return found;
        }

        public void Set(string key, string? icon, double confidence)
        {
            lock (_gate)
            {
                _entries[key] = new CachedChoice { Icon = icon, Confidence = confidence, Seen = Today() };
                _dirty = true;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
                _dirty = true;
            }
            Save();
        }

        /// <summary>Removes entries not used for MaxAgeDays, then the oldest above MaxEntries. Call inside the gate.</summary>
        private void Prune()
        {
            var limit = Today() - MaxAgeDays;
            foreach (var key in _entries.Where(kv => kv.Value.Seen < limit).Select(kv => kv.Key).ToList())
            {
                _entries.Remove(key);
                _dirty = true;
            }
            if (_entries.Count <= MaxEntries) return;
            var extra = _entries.Count - MaxEntries;
            foreach (var key in _entries.OrderBy(kv => kv.Value.Seen).Take(extra).Select(kv => kv.Key).ToList())
                _entries.Remove(key);
            _dirty = true;
        }

        private void SaveSoon()
        {
            lock (_gate)
            {
                if (_path == null) return;
                _timer ??= new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
                _timer.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
            }
        }

        public void Save()
        {
            if (_path == null) return;
            string json;
            lock (_gate)
            {
                Prune();
                if (!_dirty) return;
                json = JsonSerializer.Serialize(_entries);
                _dirty = false;
            }
            try
            {
                lock (_io)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    var tmp = _path + ".tmp";
                    File.WriteAllText(tmp, json);
                    if (File.Exists(_path)) File.Delete(_path);
                    File.Move(tmp, _path);
                }
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
