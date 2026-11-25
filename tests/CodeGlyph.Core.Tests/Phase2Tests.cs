using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeGlyph;
using CodeGlyph.Selectors;
using Xunit;

public class VocabularyTests
{
    [Fact]
    public void SplitsNamesIntoWords()
    {
        Assert.Equal(new[] { "send", "welcome", "email" }, Vocabulary.Tokens("SendWelcomeEmail"));
        Assert.Equal(new[] { "parse", "http", "response2" }, Vocabulary.Tokens("parse_HTTPResponse2"));
    }

    [Fact]
    public void UsesOnlyIconsThatExist()
    {
        var catalog = IconCatalog.LoadEmbedded();
        foreach (var icon in Vocabulary.Words.Values.Concat(Vocabulary.Badges.Values))
            Assert.True(catalog.TryGet(icon, out _), icon);
    }

    [Fact]
    public void ResolvesWhenKnownWordsAgreeAndDefersOtherwise()
    {
        Assert.Equal("mail", Vocabulary.LocalIcon("SendWelcomeEmail"));
        Assert.Equal("mail", Vocabulary.LocalIcon("EmailTemplates"));
        Assert.Equal("shopping-cart", Vocabulary.LocalIcon("Orders"));
        Assert.Null(Vocabulary.LocalIcon("OrderEmailValidator"));
        Assert.Null(Vocabulary.LocalIcon("Helper"));
    }

    [Fact]
    public void GivesBadgesForMethodVerbsOnly()
    {
        SymbolDescriptor D(string name, SymbolKind kind = SymbolKind.Method) => new SymbolDescriptor { Name = name, Kind = kind };
        Assert.Equal("x", Vocabulary.BadgeFor(D("CancelOrder")));
        Assert.Equal("search", Vocabulary.BadgeFor(D("getUserById")));
        Assert.Null(Vocabulary.BadgeFor(D("Widget")));
        Assert.Null(Vocabulary.BadgeFor(D("CancelOrder", SymbolKind.Type)));
    }

    [Fact]
    public void ConceptWordsSkipVerbsAndConstructs()
    {
        Assert.Equal(new[] { "order" }, Vocabulary.ConceptWords("CancelOrderHandler"));
    }

    [Fact]
    public void TablesMatchTheVsCodeSource()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "vscode", "src", "vocabulary.ts")))
            dir = System.IO.Path.GetDirectoryName(dir);
        if (dir == null) return;
        var ts = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "vscode", "src", "vocabulary.ts"));
        var rows = System.Text.RegularExpressions.Regex.Matches(ts, "group\\('([^']+)', '([^']+)'\\)");
        var words = rows.Cast<System.Text.RegularExpressions.Match>().SelectMany(m => m.Groups[2].Value.Split(' ').Select(w => (w, m.Groups[1].Value)));
        foreach (var (word, icon) in words)
            Assert.True(Vocabulary.Words.TryGetValue(word, out var a) && a == icon || Vocabulary.Badges.TryGetValue(word, out var b) && b == icon, word);
    }
}

public class GlossaryTests
{
    private static SymbolDescriptor D(string name) => new SymbolDescriptor { Name = name, Kind = SymbolKind.Type, LineCount = 10 };

    [Fact]
    public void HintsOnlyAWordSeenTwiceWithOneDominantIcon()
    {
        var g = new Glossary();
        g.Learn(D("SupplyBatch"), "package");
        Assert.Empty(g.Hints(new[] { D("Supply") }));
        g.Learn(D("SupplyRun"), "package");
        var hint = Assert.Single(g.Hints(new[] { D("SupplyList") }));
        Assert.Equal(("supply", "package"), (hint.Word, hint.Icon));
        g.Learn(D("SupplyX"), "truck");
        g.Learn(D("SupplyY"), "truck");
        Assert.Empty(g.Hints(new[] { D("Supply") }));
        g.Dispose();
    }
}

public class Phase2PipelineTests
{
    private sealed class Spy : IIconSelector, IWantsShortlist
    {
        public bool Short = true;
        public List<string> Systems = new List<string>();
        public List<IReadOnlyList<GlyphHint>?> Hints = new List<IReadOnlyList<GlyphHint>?>();
        public double Confidence = 0.95;
        public string Name => "spy";

        public Task<IReadOnlyList<IconSelection>> SelectAsync(string systemPrompt, IReadOnlyList<SelectionRequest> requests, CancellationToken ct, IReadOnlyList<GlyphHint>? hints = null)
        {
            Systems.Add(systemPrompt);
            Hints.Add(hints);
            return Task.FromResult<IReadOnlyList<IconSelection>>(requests.Select(r => new IconSelection(r.Id, "package", Confidence)).ToList());
        }
    }

    private sealed class Plain : IIconSelector
    {
        public string? System;
        public string Name => "plain";

        public Task<IReadOnlyList<IconSelection>> SelectAsync(string systemPrompt, IReadOnlyList<SelectionRequest> requests, CancellationToken ct, IReadOnlyList<GlyphHint>? hints = null)
        {
            System = systemPrompt;
            return Task.FromResult<IReadOnlyList<IconSelection>>(requests.Select(r => new IconSelection(r.Id, null, 0)).ToList());
        }
    }

    private static SymbolDescriptor D(string name, string comments = "") =>
        new SymbolDescriptor { Kind = SymbolKind.Type, Name = name, LineCount = 10, Comments = comments };

    [Fact]
    public async Task LocalIconsNeedNoModelCall()
    {
        var spy = new Spy();
        var p = new SelectionPipeline(IconCatalog.LoadEmbedded(), new IconCache(), () => spy, () => new GlyphStyle(), _ => { });
        var d = D("SendWelcomeEmail");
        Assert.Equal("mail", p.Lookup(d)?.Name);
        await p.ResolveAsync(new[] { d }, default);
        Assert.Empty(spy.Systems);
    }

    [Fact]
    public async Task GlossaryLearnsFromConfidentAnswersAndSendsHints()
    {
        var spy = new Spy();
        var g = new Glossary();
        var p = new SelectionPipeline(IconCatalog.LoadEmbedded(), new IconCache(), () => spy, () => new GlyphStyle(), _ => { }, null, null, g);
        await p.ResolveAsync(new[] { D("SupplyOne"), D("SupplyTwo") }, default);
        await p.ResolveAsync(new[] { D("SupplyThree") }, default);
        var hint = Assert.Single(spy.Hints[1]!);
        Assert.Equal("supply", hint.Word);

        spy.Confidence = 0.8;
        await p.ResolveAsync(new[] { D("GadgetOne"), D("GadgetTwo") }, default);
        Assert.Empty(g.Hints(new[] { D("Gadget") }));
        g.Dispose();
    }

    [Fact]
    public async Task ShortListGoesOnlyToProvidersWithNoPromptCache()
    {
        var catalog = IconCatalog.LoadEmbedded();
        var spy = new Spy();
        var plain = new Plain();
        var d = D("Reconcile", "Matches ledger entries with bank lines.");
        await new SelectionPipeline(catalog, new IconCache(), () => spy, () => new GlyphStyle(), _ => { }).ResolveAsync(new[] { d }, default);
        await new SelectionPipeline(catalog, new IconCache(), () => plain, () => new GlyphStyle(), _ => { }).ResolveAsync(new[] { d }, default);

        Assert.True(spy.Systems[0].Length < plain.System!.Length / 4);
        Assert.Contains("short list", spy.Systems[0]);
        Assert.DoesNotContain("short list", plain.System);
    }
}

public class PromptPhase2Tests
{
    [Fact]
    public void FolderIsInTheKeyAndTheUserPrompt()
    {
        var a = new SymbolDescriptor { Name = "X", Folder = "billing/invoices" };
        var b = new SymbolDescriptor { Name = "X", Folder = "admin/users" };
        Assert.NotEqual(a.Key("1", "2"), b.Key("1", "2"));
        Assert.Contains("\"folder\":\"billing/invoices\"", SelectionPrompt.BuildUser(new[] { new SelectionRequest("0", a) }));
    }

    [Fact]
    public void HintsAppearOnlyWhenThereAreSome()
    {
        var r = new[] { new SelectionRequest("0", new SymbolDescriptor { Name = "X" }) };
        Assert.DoesNotContain("hints", SelectionPrompt.BuildUser(r));
        Assert.Contains("\"hints\":[{\"word\":\"supply\",\"icon\":\"package\"}]",
            SelectionPrompt.BuildUser(r, new[] { new GlyphHint("supply", "package") }));
    }

    [Fact]
    public void ShortlistFindsMatchesAndFallsBackToEmpty()
    {
        var catalog = IconCatalog.LoadEmbedded();
        var only = catalog.Shortlist(new[] { "invoice", "receipt", "email" }, new[] { "truck" });
        Assert.Contains("receipt", only);
        Assert.Contains("mail", only);
        Assert.Contains("truck", only);
        Assert.True(only.Count <= 151);
        Assert.Empty(catalog.Shortlist(new[] { "zzzqqq" }));
    }
}
