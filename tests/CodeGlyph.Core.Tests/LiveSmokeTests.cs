using CodeGlyph;
using CodeGlyph.Selectors;
using Xunit;

namespace CodeGlyph.Core.Tests;

/// <summary>
/// Calls a real provider. Runs only when CODEGLYPH_LIVE is set to cli, anthropic or openai.
/// </summary>
public class LiveSmokeTests
{
    [SkippableFact]
    public async Task SpecificSymbolsGetIconsAndGenericOnesGetNone()
    {
        var mode = Environment.GetEnvironmentVariable("CODEGLYPH_LIVE");
        Skip.If(string.IsNullOrEmpty(mode), "Set CODEGLYPH_LIVE=cli|anthropic|openai to run.");

        var settings = new ProviderSettings
        {
            Provider = mode switch { "anthropic" => ProviderKind.Anthropic, "openai" => ProviderKind.OpenAI, _ => ProviderKind.ClaudeCli },
        };
        using var http = new HttpClient();
        var selector = SelectorFactory.Create(settings, http);
        Assert.NotNull(selector);

        var catalog = IconCatalog.LoadEmbedded();
        var requests = new[]
        {
            Req("0", SymbolKind.Type, "EmailSender", "Sends transactional email messages to patients."),
            Req("1", SymbolKind.Type, "OrdersController", "Creates and cancels customer purchase orders."),
            Req("2", SymbolKind.Method, "ValidateAccessToken", "Checks that the JWT is signed and not expired."),
            Req("3", SymbolKind.Type, "Helper", ""),
            Req("4", SymbolKind.Method, "DoWork", ""),
        };

        var answers = await selector!.SelectAsync(SelectionPrompt.BuildSystem(catalog), requests, CancellationToken.None);
        foreach (var a in answers) Console.WriteLine($"{a.Id} -> {a.Icon ?? "none"} ({a.Confidence})");

        var byId = answers.ToDictionary(a => a.Id);
        Assert.NotNull(byId["0"].Icon);
        Assert.True(catalog.TryGet(byId["0"].Icon!, out _));
        Assert.Null(byId["3"].Icon);
        Assert.Null(byId["4"].Icon);
    }

    private static SelectionRequest Req(string id, SymbolKind kind, string name, string summary) =>
        new(id, new SymbolDescriptor { Kind = kind, Name = name, Summary = summary, LineCount = 10 });
}
