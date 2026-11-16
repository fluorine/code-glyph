using CodeGlyph;
using CodeGlyph.Vsix.Editor;
using Xunit;

namespace CodeGlyph.Core.Tests;

public class ReaderTests
{
    private const string Source = @"
namespace Shop.Orders
{
    // Handles the order checkout flow.
    /// <summary>
    /// Creates and cancels customer <see cref=""Order""/> records.
    /// </summary>
    [ApiController]
    public class OrdersController : ControllerBase, IDisposable
    {
        private readonly int _x;

        /// <summary>Sends the welcome email.</summary>
        public void SendWelcomeEmail(string address, int retries)
        {
            var secretBodyText = ""do-not-send"";
            Console.WriteLine(secretBodyText);
        }

        public string Name { get; set; }
    }
}
";

    [Fact]
    public void ReadsTypeMethodAndProperty()
    {
        var file = RoslynSymbolReader.Parse(Source, "Orders.cs", 4);
        Assert.Equal(3, file.Symbols.Count);

        var type = file.Symbols[0];
        Assert.Equal(SymbolKind.Type, type.Descriptor.Kind);
        Assert.Equal("OrdersController", type.Descriptor.Name);
        Assert.Equal("Shop.Orders", type.Descriptor.Namespace);
        Assert.Contains("Order records", type.Descriptor.Summary);
        Assert.Contains("order checkout", type.Descriptor.Comments);
        Assert.Contains("ControllerBase", type.Descriptor.BaseTypes);
        Assert.Contains("ApiController", type.Descriptor.Attributes);
        Assert.Equal(-1, type.ParentIndex);

        var method = file.Symbols[1];
        Assert.Equal(SymbolKind.Method, method.Descriptor.Kind);
        Assert.Equal("OrdersController", method.Descriptor.Container);
        Assert.Equal(0, method.ParentIndex);
        Assert.Equal("Sends the welcome email.", method.Descriptor.Summary);
        Assert.Equal(new[] { "address", "retries" }, method.Descriptor.Parameters);

        Assert.Equal(SymbolKind.Property, file.Symbols[2].Descriptor.Kind);
    }

    [Fact]
    public void NeverPutsBodyTextInADescriptor()
    {
        var file = RoslynSymbolReader.Parse(Source, "Orders.cs", 4);
        var all = SelectionPrompt.BuildUser(file.Symbols.Select((s, i) => new SelectionRequest(i.ToString(), s.Descriptor)).ToList());
        Assert.DoesNotContain("do-not-send", all);
        Assert.DoesNotContain("secretBodyText", all);
    }

    [Fact]
    public void LinesAndColumnsAreMeasured()
    {
        var file = RoslynSymbolReader.Parse("class A\n{\n\tvoid M()\n\t{\n\t}\n}\n", "A.cs", 4);
        var method = file.Symbols[1];
        Assert.Equal(2, method.StartLine);
        Assert.Equal(4, method.EndLine);
        Assert.Equal(3, method.Descriptor.LineCount);
        Assert.Equal(4 + "void M()".Length, file.ColumnsPerLine[2]);
    }

    [Fact]
    public void SameSymbolInTwoFilesHasTheSameKey()
    {
        var a = RoslynSymbolReader.Parse(Source, "A.cs", 4).Symbols[1].Descriptor;
        var b = RoslynSymbolReader.Parse(Source, "B.cs", 8).Symbols[1].Descriptor;
        Assert.Equal(a.Key("1", "1"), b.Key("1", "1"));
    }

    [Fact]
    public void BrokenCodeDoesNotThrow()
    {
        var file = RoslynSymbolReader.Parse("class { void ( {{{", "X.cs", 4);
        Assert.NotNull(file);
    }
}
