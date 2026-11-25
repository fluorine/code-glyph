using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CodeGlyph
{
    /// <summary>Everything the LLM may see about a symbol. Never contains a body.</summary>
    public sealed class SymbolDescriptor
    {
        public SymbolKind Kind { get; set; }
        public string Name { get; set; } = "";
        public string Container { get; set; } = "";
        public string Namespace { get; set; } = "";
        public string FileName { get; set; } = "";

        /// <summary>Last two folder names of the file. A weak hint. It is part of the key.</summary>
        public string Folder { get; set; } = "";

        /// <summary>Full file path. It finds .codeglyph.json. It is not in the key and never goes to the model.</summary>
        public string Path { get; set; } = "";
        public int LineCount { get; set; }
        public string Summary { get; set; } = "";
        public string Comments { get; set; } = "";
        public IList<string> BaseTypes { get; set; } = new List<string>();
        public IList<string> Attributes { get; set; } = new List<string>();
        public IList<string> Parameters { get; set; } = new List<string>();

        /// <summary>Stable across files and solutions. The file path is not part of it.</summary>
        public string Key(string catalogVersion, string promptVersion)
        {
            var sb = new StringBuilder();
            sb.Append(catalogVersion).Append('|').Append(promptVersion).Append('|')
              .Append(Kind).Append('|').Append(Name).Append('|').Append(Container).Append('|')
              .Append(Folder).Append('|')
              .Append(Namespace).Append('|').Append(Normalize(Summary)).Append('|')
              .Append(Normalize(Comments)).Append('|')
              .Append(string.Join(",", BaseTypes.OrderBy(x => x, StringComparer.Ordinal))).Append('|')
              .Append(string.Join(",", Attributes.OrderBy(x => x, StringComparer.Ordinal))).Append('|')
              .Append(string.Join(",", Parameters));
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string Normalize(string text) =>
            string.Join(" ", (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
