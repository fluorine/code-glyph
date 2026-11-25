using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeGlyph;
using CodeGlyph.Selectors;
using Xunit;

public class OverrideFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cg3-" + Guid.NewGuid().ToString("N"));

    public OverrideFileTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private static SymbolDescriptor D(string name, string container = "Shop", SymbolKind kind = SymbolKind.Method, string path = "") =>
        new SymbolDescriptor { Name = name, Container = container, Kind = kind, Path = path, LineCount = 10 };

    [Fact]
    public void ReadsEntriesAndSkipsBadOnes()
    {
        var list = Overrides.Parse("{\"overrides\":[{\"name\":\"A\",\"icon\":\"mail\"},{\"name\":\"B\",\"icon\":null},{\"name\":\"C\",\"icon\":\"none\"},"
            + "{\"icon\":\"mail\"},{\"name\":\"D\",\"icon\":5},\"text\",{\"name\":\"E\",\"kind\":\"Bogus\",\"container\":\"X\",\"icon\":\"bell\"}]}");
        Assert.Equal(new[] { "A", "B", "C", "E" }, list.Select(o => o.Name));
        Assert.Equal(new string?[] { "mail", null, null, "bell" }, list.Select(o => o.Icon));
        Assert.Null(list[3].Kind);
        Assert.Equal("X", list[3].Container);
        Assert.Empty(Overrides.Parse("{ broken"));
    }

    [Fact]
    public void MostSpecificMatchWinsAndLaterWinsATie()
    {
        var list = Overrides.Parse("{\"overrides\":[{\"name\":\"Run\",\"icon\":\"a\"},{\"name\":\"Run\",\"container\":\"Shop\",\"icon\":\"b\"},"
            + "{\"name\":\"Run\",\"icon\":\"c\"},{\"name\":\"Run\",\"container\":\"Shop\",\"kind\":\"Method\",\"icon\":\"d\"}]}");
        Assert.Equal("d", Overrides.Match(list, D("Run"))!.Icon);
        Assert.Equal("c", Overrides.Match(list, D("Run", "Other"))!.Icon);
        Assert.Null(Overrides.Match(list, D("Nope")));
    }

    [Fact]
    public void FindsTheNearestFileAndFollowsEdits()
    {
        var sub = Path.Combine(_root, "src", "deep");
        Directory.CreateDirectory(sub);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var store = new OverrideStore(() => now, TimeSpan.FromSeconds(1));
        var d = D("Widget", path: Path.Combine(sub, "a.cs"));
        Assert.False(store.TryGet(d, out _));

        File.WriteAllText(Path.Combine(_root, Overrides.FileName), "{\"overrides\":[{\"name\":\"Widget\",\"icon\":\"bell\"}]}");
        now = now.AddSeconds(2);
        Assert.True(store.TryGet(d, out var icon));
        Assert.Equal("bell", icon);

        File.WriteAllText(Path.Combine(sub, Overrides.FileName), "{\"overrides\":[{\"name\":\"Widget\",\"icon\":null}]}");
        now = now.AddSeconds(2);
        Assert.True(store.TryGet(d, out icon));
        Assert.Null(icon);
        Assert.False(store.TryGet(D("Widget"), out _));
    }

    private sealed class CountingSelector : IIconSelector
    {
        public int Asked;
        public string Name => "count";

        public Task<IReadOnlyList<IconSelection>> SelectAsync(string systemPrompt, IReadOnlyList<SelectionRequest> requests, CancellationToken ct, IReadOnlyList<GlyphHint>? hints = null)
        {
            Asked += requests.Count;
            return Task.FromResult<IReadOnlyList<IconSelection>>(requests.Select(r => new IconSelection(r.Id, "package", 0.95)).ToList());
        }
    }

    private SelectionPipeline Pipeline(CountingSelector sel, Func<GlyphStyle>? style = null) =>
        new SelectionPipeline(IconCatalog.LoadEmbedded(), new IconCache(), () => sel, style ?? (() => new GlyphStyle()), _ => { }, null, null, null, new OverrideStore());

    [Fact]
    public async Task OverrideWinsOverWordsAndNeedsNoModelCall()
    {
        File.WriteAllText(Path.Combine(_root, Overrides.FileName), "{\"overrides\":[{\"name\":\"SendEmail\",\"icon\":\"bell\"},{\"name\":\"HideMe\",\"icon\":null},{\"name\":\"Bad\",\"icon\":\"not-an-icon\"}]}");
        var sel = new CountingSelector();
        var p = Pipeline(sel);
        var path = Path.Combine(_root, "a.cs");

        var set = D("SendEmail", path: path);
        Assert.Equal("bell", p.Lookup(set)?.Name);
        var hidden = D("HideMe", path: path);
        Assert.Null(p.Lookup(hidden));
        Assert.False(p.ShouldRequest(hidden));
        await p.ResolveAsync(new[] { set, hidden }, default);
        Assert.Equal(0, sel.Asked);

        Assert.Equal("mail", p.Lookup(D("SendEmail", path: Path.Combine(Path.GetTempPath(), "x.cs")))?.Name);
        Assert.True(p.ShouldRequest(D("Bad", path: path)));
    }

    [Fact]
    public async Task LocalWordsCanBeTurnedOff()
    {
        var sel = new CountingSelector();
        var on = true;
        var p = Pipeline(sel, () => new GlyphStyle { UseLocalWords = on });
        var d = D("SendEmail");
        Assert.Equal("mail", p.Lookup(d)?.Name);
        on = false;
        Assert.Null(p.Lookup(d));
        await p.ResolveAsync(new[] { d }, default);
        Assert.Equal(1, sel.Asked);
        Assert.Equal("package", p.Lookup(d)?.Name);
    }
}
