using CodeGlyph;
using CodeGlyph.Selectors;
using Xunit;

namespace CodeGlyph.Core.Tests;

public class PlacementTests
{
    private static readonly GlyphStyle Style = new() { AllowOverCode = false };

    [Fact]
    public void OverCodeModeShowsIconAtViewportRightEvenWithLongLines()
    {
        var style = new GlyphStyle();
        var p = PlacementCalculator.Calculate(0, 400, 5000, 800, 600, style)!.Value;
        Assert.Equal(800 - style.MarginToEdge, p.Left + p.Size, 3);
    }

    [Fact]
    public void IconNeverOverlapsCode()
    {
        foreach (var h in Enum.GetValues<HorizontalAlignment>())
        foreach (var v in Enum.GetValues<VerticalAlignment>())
        foreach (var codeRight in new double[] { 100, 300, 500 })
        {
            var style = Style with { HorizontalAlignment = h, VerticalAlignment = v };
            var p = PlacementCalculator.Calculate(0, 400, codeRight, 800, 600, style);
            if (p is { } placed)
            {
                Assert.True(placed.Left >= codeRight + style.MarginToCode - 0.001, $"{h}/{v}/{codeRight}");
                Assert.True(placed.Left + placed.Size <= 800 - style.MarginToEdge + 0.001);
                Assert.True(placed.Size >= style.MinSize);
            }
        }
    }

    [Fact]
    public void NoIconWhenFreeWidthIsBelowMinimum()
    {
        Assert.Null(PlacementCalculator.Calculate(0, 400, 760, 800, 600, Style));
    }

    [Fact]
    public void NoIconWhenSymbolIsTooShort()
    {
        Assert.Null(PlacementCalculator.Calculate(0, 40, 100, 800, 600, Style));
    }

    [Fact]
    public void ViewportRightAlignsToEdge()
    {
        var p = PlacementCalculator.Calculate(0, 400, 100, 800, 600, Style)!.Value;
        Assert.Equal(800 - Style.MarginToEdge, p.Left + p.Size, 3);
        Assert.Equal(Style.MaxSize, p.Size, 3);
    }

    [Fact]
    public void AfterCodeAlignsToCodeMargin()
    {
        var style = Style with { HorizontalAlignment = HorizontalAlignment.AfterCode };
        var p = PlacementCalculator.Calculate(0, 400, 100, 800, 600, style)!.Value;
        Assert.Equal(100 + style.MarginToCode, p.Left, 3);
    }

    [Fact]
    public void VerticalAlignmentUsesSymbolBand()
    {
        var top = PlacementCalculator.Calculate(100, 400, 100, 800, 600, Style with { VerticalAlignment = VerticalAlignment.Top })!.Value;
        var bottom = PlacementCalculator.Calculate(100, 400, 100, 800, 600, Style with { VerticalAlignment = VerticalAlignment.Bottom })!.Value;
        Assert.Equal(100, top.Top, 3);
        Assert.Equal(400, bottom.Top + bottom.Size, 3);
    }

    [Fact]
    public void StickyKeepsIconInVisiblePartAndScrollingDoesNot()
    {
        // Band runs from -300 to 300, so only 0..300 is visible.
        var sticky = PlacementCalculator.Calculate(-300, 300, 100, 800, 600, Style with { VerticalBehavior = VerticalBehavior.StickyInSymbol })!.Value;
        Assert.True(sticky.Top >= 0);
        var scrolling = PlacementCalculator.Calculate(-300, 300, 100, 800, 600, Style with { VerticalBehavior = VerticalBehavior.ScrollWithCode })!.Value;
        Assert.Equal(0, scrolling.Top + scrolling.Size / 2, 3);
    }
}

public class DescriptorTests
{
    private static SymbolDescriptor Make(string summary = "Creates orders.") => new()
    {
        Kind = SymbolKind.Type,
        Name = "OrdersController",
        Namespace = "Shop.Api",
        Summary = summary,
        BaseTypes = { "ControllerBase" },
    };

    [Fact]
    public void KeyIsStableAndIgnoresWhitespaceAndFile()
    {
        var a = Make("Creates   orders.");
        var b = Make("Creates orders.");
        b.FileName = "Other.cs";
        Assert.Equal(a.Key("1", "1"), b.Key("1", "1"));
    }

    [Fact]
    public void KeyChangesWithContentCatalogAndPromptVersion()
    {
        var a = Make();
        Assert.NotEqual(a.Key("1", "1"), Make("Deletes orders.").Key("1", "1"));
        Assert.NotEqual(a.Key("1", "1"), a.Key("2", "1"));
        Assert.NotEqual(a.Key("1", "1"), a.Key("1", "2"));
    }
}

public class CacheTests
{
    [Fact]
    public void RoundTripsIncludingNone()
    {
        var path = Path.Combine(Path.GetTempPath(), "codeglyph-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var cache = new IconCache(path);
            cache.Set("k1", "mail", 0.9);
            cache.Set("k2", null, 0);
            cache.Save();

            var again = new IconCache(path);
            Assert.True(again.TryGet("k1", out var c1));
            Assert.Equal("mail", c1.Icon);
            Assert.True(again.TryGet("k2", out var c2));
            Assert.Null(c2.Icon);
            Assert.False(again.TryGet("k3", out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RemovesEntriesUnusedForNinetyDays()
    {
        var path = Path.Combine(Path.GetTempPath(), "codeglyph-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            using (var cache = new IconCache(path, () => day))
            {
                cache.Set("old", "mail", 0.9);
                cache.Set("keep", "mail", 0.9);
                cache.Save();
                day = day.AddDays(60);
                Assert.True(cache.TryGet("keep", out _));
                cache.Save();
            }
            day = day.AddDays(40);

            using var again = new IconCache(path, () => day);
            Assert.False(again.TryGet("old", out _));
            Assert.True(again.TryGet("keep", out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DamagedFileGivesEmptyCache()
    {
        var path = Path.Combine(Path.GetTempPath(), "codeglyph-test-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ not json");
        try { Assert.Equal(0, new IconCache(path).Count); }
        finally { File.Delete(path); }
    }
}

public class CatalogTests
{
    [Fact]
    public void EmbeddedCatalogLoads()
    {
        var catalog = IconCatalog.LoadEmbedded();
        Assert.True(catalog.Count > 1000);
        Assert.True(catalog.TryGet("mail", out var mail));
        Assert.NotEmpty(mail.Elements);
        Assert.Contains("mail", catalog.BuildPromptText());
    }
}

public class PromptTests
{
    [Fact]
    public void ParsesObjectArrayAndFencedText()
    {
        var obj = SelectionPrompt.ParseResponse("{\"results\":[{\"id\":\"0\",\"icon\":\"mail\",\"confidence\":0.95},{\"id\":\"1\",\"icon\":null,\"confidence\":0}]}");
        Assert.Equal(2, obj.Count);
        Assert.Equal("mail", obj[0].Icon);
        Assert.Null(obj[1].Icon);

        var fenced = SelectionPrompt.ParseResponse("```json\n[{\"id\":\"0\",\"icon\":\"none\",\"confidence\":0.1}]\n```");
        Assert.Single(fenced);
        Assert.Null(fenced[0].Icon);
    }

    [Fact]
    public void GarbageGivesEmpty()
    {
        Assert.Empty(SelectionPrompt.ParseResponse("sorry, no"));
        Assert.Empty(SelectionPrompt.ParseResponse("{ broken"));
        Assert.Empty(SelectionPrompt.ParseResponse(""));
    }

    [Fact]
    public void UserPromptHasNoBodyFields()
    {
        var json = SelectionPrompt.BuildUser(new[] { new SelectionRequest("0", new SymbolDescriptor { Name = "Send", Summary = "Sends mail." }) });
        Assert.Contains("\"name\":\"Send\"", json);
        Assert.DoesNotContain("body", json, StringComparison.OrdinalIgnoreCase);
    }
}

public class PipelineTests
{
    private sealed class FakeSelector : IIconSelector
    {
        public int Calls;
        public Func<IReadOnlyList<SelectionRequest>, IReadOnlyList<IconSelection>> Answer = _ => Array.Empty<IconSelection>();
        public string Name => "fake";

        public Task<IReadOnlyList<IconSelection>> SelectAsync(string systemPrompt, IReadOnlyList<SelectionRequest> requests, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Answer(requests));
        }
    }

    private static SymbolDescriptor Sym(string name, int lines = 10) =>
        new() { Kind = SymbolKind.Method, Name = name, LineCount = lines };

    private static (SelectionPipeline, FakeSelector, List<string>) Make(DateTime? now = null)
    {
        var fake = new FakeSelector();
        var log = new List<string>();
        var clock = now ?? DateTime.UtcNow;
        var pipeline = new SelectionPipeline(IconCatalog.LoadEmbedded(), new IconCache(), () => fake, () => new GlyphStyle(), log.Add, () => clock);
        return (pipeline, fake, log);
    }

    [Fact]
    public async Task CachesAnswersAndAsksOnlyOnce()
    {
        var (pipeline, fake, _) = Make();
        fake.Answer = reqs => reqs.Select(r => new IconSelection(r.Id, "mail", 0.95)).ToList();
        var s = Sym("SendWelcomeEmail");

        await pipeline.ResolveAsync(new[] { s }, default);
        await pipeline.ResolveAsync(new[] { s }, default);

        Assert.Equal(1, fake.Calls);
        Assert.Equal("mail", pipeline.Lookup(s)?.Name);
    }

    [Fact]
    public async Task RejectsUnknownLowConfidenceAndGenericIcons()
    {
        var (pipeline, fake, _) = Make();
        fake.Answer = reqs => new[]
        {
            new IconSelection("0", "not-a-real-icon", 0.99),
            new IconSelection("1", "mail", 0.3),
            new IconSelection("2", "box", 0.8),
        };
        var a = Sym("A"); var b = Sym("B"); var c = Sym("C");
        await pipeline.ResolveAsync(new[] { a, b, c }, default);

        Assert.Null(pipeline.Lookup(a));
        Assert.Null(pipeline.Lookup(b));
        Assert.Null(pipeline.Lookup(c));

        // "none" is cached, so a second call must not ask again.
        await pipeline.ResolveAsync(new[] { a, b, c }, default);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task SkipsShortAndTrivialSymbols()
    {
        var (pipeline, fake, _) = Make();
        await pipeline.ResolveAsync(new[] { Sym("Tiny", 2), Sym("ToString"), Sym("Dispose") }, default);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task FailureBacksOffAndDoesNotThrow()
    {
        var fake = new FakeSelector { Answer = _ => throw new InvalidOperationException("boom") };
        var now = DateTime.UtcNow;
        var log = new List<string>();
        var pipeline = new SelectionPipeline(IconCatalog.LoadEmbedded(), new IconCache(), () => fake, () => new GlyphStyle(), log.Add, () => now);

        await pipeline.ResolveAsync(new[] { Sym("A") }, default);
        await pipeline.ResolveAsync(new[] { Sym("B") }, default);

        Assert.Equal(1, fake.Calls);
        Assert.Contains(log, l => l.Contains("boom"));
    }

    [Fact]
    public async Task MissingAnswerIsAskedAgainBeforeItIsNone()
    {
        var (pipeline, fake, _) = Make();
        fake.Answer = reqs => reqs
            .Where(r => fake.Calls > 1 || r.Id == "0")
            .Select(r => new IconSelection(r.Id, "mail", 0.95)).ToList();
        var a = Sym("A"); var b = Sym("B");

        await pipeline.ResolveAsync(new[] { a, b }, default);

        Assert.Equal(2, fake.Calls);
        Assert.Equal("mail", pipeline.Lookup(a)?.Name);
        Assert.Equal("mail", pipeline.Lookup(b)?.Name);
    }

    [Fact]
    public async Task StillMissingAfterRetryIsCachedAsNone()
    {
        var (pipeline, fake, _) = Make();
        fake.Answer = reqs => reqs
            .Where(r => r.Id == "0").Select(r => new IconSelection(r.Id, "mail", 0.95))
            .Append(new IconSelection("99", null, 0)).ToList();
        var a = Sym("A"); var b = Sym("B");

        await pipeline.ResolveAsync(new[] { a, b }, default);
        await pipeline.ResolveAsync(new[] { a, b }, default);

        Assert.Equal(2, fake.Calls);
        Assert.Null(pipeline.Lookup(b));
    }

    [Fact]
    public async Task EmptyReplyIsNotCachedAsNone()
    {
        var (pipeline, fake, log) = Make();
        var a = Sym("A");

        await pipeline.ResolveAsync(new[] { a }, default);

        Assert.Equal(1, fake.Calls);
        Assert.Contains(log, l => l.Contains("no usable answers"));
        Assert.Null(pipeline.Lookup(a));
        Assert.True(pipeline.ShouldRequest(a));
    }

    [Fact]
    public async Task RaisesResolvedWhenAnswersArrive()
    {
        var (pipeline, fake, _) = Make();
        fake.Answer = reqs => reqs.Select(r => new IconSelection(r.Id, "shield", 0.9)).ToList();
        var raised = 0;
        pipeline.Resolved += () => raised++;
        await pipeline.ResolveAsync(new[] { Sym("ValidateToken") }, default);
        Assert.Equal(1, raised);
    }
}
