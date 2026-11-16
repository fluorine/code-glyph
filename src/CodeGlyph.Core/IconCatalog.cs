using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CodeGlyph
{
    public sealed class IconElement
    {
        public IconElement(string kind, IReadOnlyDictionary<string, string> attributes)
        {
            Kind = kind;
            Attributes = attributes;
        }

        public string Kind { get; }
        public IReadOnlyDictionary<string, string> Attributes { get; }
    }

    public sealed class IconDefinition
    {
        public IconDefinition(string name, IReadOnlyList<string> tags, IReadOnlyList<IconElement> elements)
        {
            Name = name;
            Tags = tags;
            Elements = elements;
        }

        public string Name { get; }
        public IReadOnlyList<string> Tags { get; }
        public IReadOnlyList<IconElement> Elements { get; }
    }

    public sealed class IconCatalog
    {
        private readonly Dictionary<string, IconDefinition> _icons;

        private IconCatalog(string version, Dictionary<string, IconDefinition> icons)
        {
            Version = version;
            _icons = icons;
        }

        public string Version { get; }
        public int Count => _icons.Count;
        public IEnumerable<string> Names => _icons.Keys;

        public bool TryGet(string name, out IconDefinition icon) => _icons.TryGetValue(name, out icon!);

        public static IconCatalog LoadEmbedded()
        {
            var asm = typeof(IconCatalog).Assembly;
            using (var stream = asm.GetManifestResourceStream("CodeGlyph.icons.json.gz"))
            {
                if (stream == null) throw new InvalidOperationException("icons.json.gz is not embedded.");
                return Load(stream);
            }
        }

        public static IconCatalog Load(Stream gzipStream)
        {
            using (var gz = new GZipStream(gzipStream, CompressionMode.Decompress))
            using (var doc = JsonDocument.Parse(gz))
            {
                var root = doc.RootElement;
                var version = root.GetProperty("version").GetString() ?? "0";
                var icons = new Dictionary<string, IconDefinition>(StringComparer.Ordinal);
                foreach (var item in root.GetProperty("icons").EnumerateArray())
                {
                    var name = item.GetProperty("n").GetString()!;
                    var tags = item.GetProperty("t").EnumerateArray().Select(t => t.GetString()!).ToList();
                    var elements = new List<IconElement>();
                    foreach (var e in item.GetProperty("e").EnumerateArray())
                    {
                        var attrs = new Dictionary<string, string>();
                        foreach (var p in e.GetProperty("a").EnumerateObject())
                        {
                            attrs[p.Name] = p.Value.GetString() ?? "";
                        }
                        elements.Add(new IconElement(e.GetProperty("k").GetString()!, attrs));
                    }
                    icons[name] = new IconDefinition(name, tags, elements);
                }
                return new IconCatalog(version, icons);
            }
        }

        /// <summary>One line per icon: "name: tag, tag, tag". Sorted for a stable prompt-cache prefix.</summary>
        public string BuildPromptText(int maxTags = 3)
        {
            var sb = new StringBuilder();
            foreach (var icon in _icons.Values.OrderBy(i => i.Name, StringComparer.Ordinal))
            {
                sb.Append(icon.Name);
                if (icon.Tags.Count > 0)
                {
                    sb.Append(": ").Append(string.Join(", ", icon.Tags.Take(maxTags)));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
