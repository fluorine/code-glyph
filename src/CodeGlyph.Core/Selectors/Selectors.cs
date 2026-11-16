using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeGlyph.Selectors
{
    public interface IIconSelector
    {
        string Name { get; }

        Task<IReadOnlyList<IconSelection>> SelectAsync(
            string systemPrompt, IReadOnlyList<SelectionRequest> requests, CancellationToken ct);
    }

    public enum ProviderKind { Auto, Anthropic, OpenAI, ClaudeCli }

    public sealed class ProviderSettings
    {
        public ProviderKind Provider { get; set; } = ProviderKind.Auto;
        public string? AnthropicKey { get; set; }
        public string AnthropicModel { get; set; } = "claude-haiku-5-5";
        public string? OpenAiKey { get; set; }
        public string OpenAiModel { get; set; } = "gpt-4.1-mini";
        public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";
        public string ClaudeCliPath { get; set; } = "claude";
        public string ClaudeCliModel { get; set; } = "haiku";
    }

    public static class SelectorFactory
    {
        /// <summary>Returns null when the chosen provider has no usable credentials.</summary>
        public static IIconSelector? Create(ProviderSettings s, HttpClient http)
        {
            var anthropicKey = FirstNonEmpty(s.AnthropicKey, Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
            var openAiKey = FirstNonEmpty(s.OpenAiKey, Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

            switch (s.Provider)
            {
                case ProviderKind.Anthropic:
                    return anthropicKey == null ? null : new AnthropicSelector(http, anthropicKey, s.AnthropicModel);
                case ProviderKind.OpenAI:
                    return openAiKey == null ? null : new OpenAiSelector(http, openAiKey, s.OpenAiModel, s.OpenAiBaseUrl);
                case ProviderKind.ClaudeCli:
                    return new ClaudeCliSelector(s.ClaudeCliPath, s.ClaudeCliModel);
                default:
                    if (anthropicKey != null) return new AnthropicSelector(http, anthropicKey, s.AnthropicModel);
                    if (openAiKey != null) return new OpenAiSelector(http, openAiKey, s.OpenAiModel, s.OpenAiBaseUrl);
                    return new ClaudeCliSelector(s.ClaudeCliPath, s.ClaudeCliModel);
            }
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v)) return v!.Trim();
            }
            return null;
        }
    }

    public sealed class AnthropicSelector : IIconSelector
    {
        private readonly HttpClient _http;
        private readonly string _key;
        private readonly string _model;

        public AnthropicSelector(HttpClient http, string key, string model)
        {
            _http = http;
            _key = key;
            _model = model;
        }

        public string Name => "Anthropic " + _model;

        public async Task<IReadOnlyList<IconSelection>> SelectAsync(
            string systemPrompt, IReadOnlyList<SelectionRequest> requests, CancellationToken ct)
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = _model,
                ["max_tokens"] = 2048,
                ["system"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "text",
                        ["text"] = systemPrompt,
                        ["cache_control"] = new Dictionary<string, string> { ["type"] = "ephemeral" },
                    },
                },
                ["messages"] = new[]
                {
                    new Dictionary<string, string> { ["role"] = "user", ["content"] = SelectionPrompt.BuildUser(requests) },
                },
            };

            using (var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages"))
            {
                req.Headers.Add("x-api-key", _key);
                req.Headers.Add("anthropic-version", "2023-06-01");
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using (var resp = await _http.SendAsync(req, ct).ConfigureAwait(false))
                {
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        throw new HttpRequestException("Anthropic returned " + (int)resp.StatusCode + ": " + Shorten(text));

                    using (var doc = JsonDocument.Parse(text))
                    {
                        var sb = new StringBuilder();
                        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
                        {
                            if (block.TryGetProperty("type", out var t) && t.GetString() == "text")
                                sb.Append(block.GetProperty("text").GetString());
                        }
                        return SelectionPrompt.ParseResponse(sb.ToString());
                    }
                }
            }
        }

        internal static string Shorten(string s) => s.Length <= 300 ? s : s.Substring(0, 300);
    }

    public sealed class OpenAiSelector : IIconSelector
    {
        private readonly HttpClient _http;
        private readonly string _key;
        private readonly string _model;
        private readonly string _baseUrl;

        public OpenAiSelector(HttpClient http, string key, string model, string baseUrl)
        {
            _http = http;
            _key = key;
            _model = model;
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public string Name => "OpenAI " + _model;

        public async Task<IReadOnlyList<IconSelection>> SelectAsync(
            string systemPrompt, IReadOnlyList<SelectionRequest> requests, CancellationToken ct)
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = _model,
                ["temperature"] = 0,
                ["response_format"] = new Dictionary<string, string> { ["type"] = "json_object" },
                ["messages"] = new[]
                {
                    new Dictionary<string, string> { ["role"] = "system", ["content"] = systemPrompt },
                    new Dictionary<string, string> { ["role"] = "user", ["content"] = SelectionPrompt.BuildUser(requests) },
                },
            };

            using (var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions"))
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _key);
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using (var resp = await _http.SendAsync(req, ct).ConfigureAwait(false))
                {
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        throw new HttpRequestException("OpenAI returned " + (int)resp.StatusCode + ": " + AnthropicSelector.Shorten(text));

                    using (var doc = JsonDocument.Parse(text))
                    {
                        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message")
                            .GetProperty("content").GetString() ?? "";
                        return SelectionPrompt.ParseResponse(content);
                    }
                }
            }
        }
    }

    public sealed class ClaudeCliSelector : IIconSelector
    {
        private static readonly SemaphoreSlim OneAtATime = new SemaphoreSlim(1, 1);
        private readonly string _path;
        private readonly string _model;

        public ClaudeCliSelector(string path, string model)
        {
            _path = string.IsNullOrWhiteSpace(path) ? "claude" : path;
            _model = model;
        }

        public string Name => "Claude CLI " + _model;

        public async Task<IReadOnlyList<IconSelection>> SelectAsync(
            string systemPrompt, IReadOnlyList<SelectionRequest> requests, CancellationToken ct)
        {
            await OneAtATime.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _path,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetTempPath(),
                    StandardOutputEncoding = Encoding.UTF8,
                };
                psi.Environment["CLAUDE_CODE_ENTRYPOINT"] = "codeglyph";
                var list = psi.ArgumentList_Add();
                list("-p");
                list("--output-format"); list("json");
                list("--model"); list(_model);
                list("--tools"); list("");
                list("--no-session-persistence");
                // The icon list is far over the Windows command-line limit, so the full prompt goes on stdin.
                list("--system-prompt"); list("You answer with JSON only.");

                using (var proc = new Process { StartInfo = psi })
                {
                    try
                    {
                        proc.Start();
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        throw new InvalidOperationException("Cannot start '" + _path + "'. Install Claude Code or set its path.", ex);
                    }

                    var errTask = proc.StandardError.ReadToEndAsync();
                    var outTask = proc.StandardOutput.ReadToEndAsync();
                    await proc.StandardInput.WriteAsync(
                        systemPrompt + "\n\nSYMBOLS (JSON):\n" + SelectionPrompt.BuildUser(requests)).ConfigureAwait(false);
                    proc.StandardInput.Close();

                    using (ct.Register(() => { try { proc.Kill(); } catch { } }))
                    {
                        var output = await outTask.ConfigureAwait(false);
                        await Task.Run(() => proc.WaitForExit(), ct).ConfigureAwait(false);
                        if (proc.ExitCode != 0)
                            throw new InvalidOperationException("claude exited with " + proc.ExitCode + ": " + AnthropicSelector.Shorten(await errTask.ConfigureAwait(false)));

                        using (var doc = JsonDocument.Parse(output))
                        {
                            var result = doc.RootElement.TryGetProperty("result", out var r) ? r.GetString() ?? "" : output;
                            return SelectionPrompt.ParseResponse(result);
                        }
                    }
                }
            }
            finally
            {
                OneAtATime.Release();
            }
        }
    }

    internal static class ProcessStartInfoExtensions
    {
        /// <summary>
        /// .NET Framework has no ArgumentList. This builds the quoted Arguments string instead.
        /// </summary>
        public static Action<string> ArgumentList_Add(this ProcessStartInfo psi)
        {
            var sb = new StringBuilder();
            return arg =>
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append('"').Append(arg.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                psi.Arguments = sb.ToString();
            };
        }
    }
}
