using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeGlyph.Selectors;

namespace CodeGlyph.Vsix
{
    internal sealed class GlyphSettings
    {
        public bool Enabled { get; set; } = true;
        public ProviderSettings Provider { get; set; } = new ProviderSettings();
        public GlyphStyle Style { get; set; } = new GlyphStyle();
    }

    /// <summary>
    /// Settings live in a JSON file so the editor part works without loading the package.
    /// API keys are stored with DPAPI for the current Windows user.
    /// </summary>
    internal static class SettingsStore
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static string Folder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeGlyph");

        public static string SettingsPath => Path.Combine(Folder, "settings.json");
        public static string CachePath => Path.Combine(Folder, "selection-cache.json");

        private sealed class FileModel
        {
            public bool Enabled { get; set; } = true;
            public ProviderKind Provider { get; set; } = ProviderKind.Auto;
            public string? AnthropicKeyProtected { get; set; }
            public string AnthropicModel { get; set; } = "claude-haiku-5-5";
            public string? OpenAiKeyProtected { get; set; }
            public string OpenAiModel { get; set; } = "gpt-4.1-mini";
            public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";
            public string ClaudeCliPath { get; set; } = "claude";
            public string ClaudeCliModel { get; set; } = "haiku";
            public GlyphStyle Style { get; set; } = new GlyphStyle();
        }

        public static GlyphSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var m = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(SettingsPath), Json);
                    if (m != null)
                    {
                        return new GlyphSettings
                        {
                            Enabled = m.Enabled,
                            Style = m.Style ?? new GlyphStyle(),
                            Provider = new ProviderSettings
                            {
                                Provider = m.Provider,
                                AnthropicKey = Unprotect(m.AnthropicKeyProtected),
                                AnthropicModel = m.AnthropicModel,
                                OpenAiKey = Unprotect(m.OpenAiKeyProtected),
                                OpenAiModel = m.OpenAiModel,
                                OpenAiBaseUrl = m.OpenAiBaseUrl,
                                ClaudeCliPath = m.ClaudeCliPath,
                                ClaudeCliModel = m.ClaudeCliModel,
                            },
                        };
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                // Fall back to defaults. A bad file must never break the editor.
            }
            return new GlyphSettings();
        }

        public static void Save(GlyphSettings s)
        {
            var p = s.Provider;
            var model = new FileModel
            {
                Enabled = s.Enabled,
                Provider = p.Provider,
                AnthropicKeyProtected = Protect(p.AnthropicKey),
                AnthropicModel = p.AnthropicModel,
                OpenAiKeyProtected = Protect(p.OpenAiKey),
                OpenAiModel = p.OpenAiModel,
                OpenAiBaseUrl = p.OpenAiBaseUrl,
                ClaudeCliPath = p.ClaudeCliPath,
                ClaudeCliModel = p.ClaudeCliModel,
                Style = s.Style,
            };
            Directory.CreateDirectory(Folder);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(model, Json));
        }

        private static string? Protect(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value!.Trim()), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }

        private static string? Unprotect(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
            {
                return null;
            }
        }
    }
}
