using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace CodeGlyph
{
    public sealed class SelectionRequest
    {
        public SelectionRequest(string id, SymbolDescriptor descriptor)
        {
            Id = id;
            Descriptor = descriptor;
        }

        public string Id { get; }
        public SymbolDescriptor Descriptor { get; }
    }

    public sealed class IconSelection
    {
        public IconSelection(string id, string? icon, double confidence)
        {
            Id = id;
            Icon = icon;
            Confidence = confidence;
        }

        public string Id { get; }
        public string? Icon { get; }
        public double Confidence { get; }
    }

    public static class SelectionPrompt
    {
        /// <summary>Part of every cache key. Raise it when the instructions change.</summary>
        public const string Version = "1";

        public static readonly string[] GenericIcons =
        {
            "file", "code", "braces", "box", "circle", "square", "shapes", "settings", "wrench", "puzzle"
        };

        public static string BuildSystem(IconCatalog catalog)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You choose a watermark icon for C# symbols in a code editor.");
            sb.AppendLine("For each symbol, pick ONE icon name from the ICON LIST that shows the specific domain function of the symbol.");
            sb.AppendLine("Base the choice on the symbol name, container, XML summary and comments. You never see the code body.");
            sb.AppendLine();
            sb.AppendLine("Rules:");
            sb.AppendLine("- Answer null when no icon clearly fits. No icon is better than a vague icon.");
            sb.AppendLine("- Do not use these generic icons unless the name or comments name that exact concept: "
                + string.Join(", ", GenericIcons) + ".");
            sb.AppendLine("- Do not pick an icon only because of the programming construct (class, method, handler, helper, manager, service, utility, base).");
            sb.AppendLine("- confidence is a number from 0 to 1. Use 0.9 or more only when the match is obvious.");
            sb.AppendLine("- Use only names that appear in the ICON LIST. Copy each name exactly.");
            sb.AppendLine();
            sb.AppendLine("Answer with JSON only. No prose, no code fence. Format:");
            sb.AppendLine("{\"results\":[{\"id\":\"<id>\",\"icon\":\"<icon name or null>\",\"confidence\":0.0}]}");
            sb.AppendLine("Return one result for each input id.");
            sb.AppendLine();
            sb.AppendLine("ICON LIST (name: tags):");
            sb.Append(catalog.BuildPromptText());
            return sb.ToString();
        }

        public static string BuildUser(IReadOnlyList<SelectionRequest> requests)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                using (var w = new Utf8JsonWriter(stream))
                {
                    w.WriteStartObject();
                    w.WriteStartArray("symbols");
                    foreach (var r in requests)
                    {
                        var d = r.Descriptor;
                        w.WriteStartObject();
                        w.WriteString("id", r.Id);
                        w.WriteString("kind", d.Kind.ToString());
                        w.WriteString("name", d.Name);
                        WriteIfNotEmpty(w, "container", d.Container);
                        WriteIfNotEmpty(w, "namespace", d.Namespace);
                        WriteIfNotEmpty(w, "file", d.FileName);
                        WriteIfNotEmpty(w, "summary", Truncate(d.Summary, 600));
                        WriteIfNotEmpty(w, "comments", Truncate(d.Comments, 600));
                        WriteList(w, "baseTypes", d.BaseTypes);
                        WriteList(w, "attributes", d.Attributes);
                        WriteList(w, "parameters", d.Parameters);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>Accepts {"results":[...]} or a bare array, with or without surrounding text.</summary>
        public static IReadOnlyList<IconSelection> ParseResponse(string text)
        {
            var results = new List<IconSelection>();
            if (string.IsNullOrWhiteSpace(text)) return results;

            var start = text.IndexOf('{');
            var arrayStart = text.IndexOf('[');
            string json;
            if (start >= 0 && (arrayStart < 0 || start < arrayStart))
            {
                var end = text.LastIndexOf('}');
                if (end <= start) return results;
                json = text.Substring(start, end - start + 1);
            }
            else if (arrayStart >= 0)
            {
                var end = text.LastIndexOf(']');
                if (end <= arrayStart) return results;
                json = text.Substring(arrayStart, end - arrayStart + 1);
            }
            else
            {
                return results;
            }

            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    JsonElement array;
                    if (root.ValueKind == JsonValueKind.Array) array = root;
                    else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("results", out array)
                             && array.ValueKind == JsonValueKind.Array) { }
                    else return results;

                    foreach (var item in array.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var idEl)) continue;
                        var id = idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : idEl.ToString();
                        if (string.IsNullOrEmpty(id)) continue;

                        string? icon = null;
                        if (item.TryGetProperty("icon", out var iconEl) && iconEl.ValueKind == JsonValueKind.String)
                        {
                            icon = iconEl.GetString();
                            if (string.Equals(icon, "null", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(icon, "none", StringComparison.OrdinalIgnoreCase)
                                || string.IsNullOrWhiteSpace(icon))
                            {
                                icon = null;
                            }
                        }

                        double confidence = 0;
                        if (item.TryGetProperty("confidence", out var cEl) && cEl.ValueKind == JsonValueKind.Number)
                        {
                            confidence = cEl.GetDouble();
                        }
                        results.Add(new IconSelection(id!, icon, confidence));
                    }
                }
            }
            catch (JsonException)
            {
                results.Clear();
            }
            return results;
        }

        private static void WriteIfNotEmpty(Utf8JsonWriter w, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) w.WriteString(name, value);
        }

        private static void WriteList(Utf8JsonWriter w, string name, IList<string> values)
        {
            if (values == null || values.Count == 0) return;
            w.WriteStartArray(name);
            foreach (var v in values) w.WriteStringValue(v);
            w.WriteEndArray();
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);
    }
}
