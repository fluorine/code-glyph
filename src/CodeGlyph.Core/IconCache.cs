using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CodeGlyph
{
    public sealed class CachedChoice
    {
        public string? Icon { get; set; }
        public double Confidence { get; set; }
    }

    /// <summary>Descriptor key to icon choice. A null icon is a cached "none".</summary>
    public sealed class IconCache
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, CachedChoice> _entries = new Dictionary<string, CachedChoice>();
        private readonly string? _path;
        private bool _dirty;

        public IconCache(string? path = null)
        {
            _path = path;
            if (path != null && File.Exists(path))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, CachedChoice>>(File.ReadAllText(path));
                    if (loaded != null)
                    {
                        foreach (var kv in loaded) _entries[kv.Key] = kv.Value;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is JsonException)
                {
                    // A damaged cache file only costs a refill.
                }
            }
        }

        public int Count
        {
            get { lock (_gate) return _entries.Count; }
        }

        public bool TryGet(string key, out CachedChoice choice)
        {
            lock (_gate) return _entries.TryGetValue(key, out choice!);
        }

        public void Set(string key, string? icon, double confidence)
        {
            lock (_gate)
            {
                _entries[key] = new CachedChoice { Icon = icon, Confidence = confidence };
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

        public void Save()
        {
            if (_path == null) return;
            string json;
            lock (_gate)
            {
                if (!_dirty) return;
                json = JsonSerializer.Serialize(_entries);
                _dirty = false;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path);
            }
            catch (IOException)
            {
                lock (_gate) _dirty = true;
            }
        }
    }
}
