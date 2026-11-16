using CodeGlyph;
using Xunit;

namespace CodeGlyph.Core.Tests;

public class BandTests
{
    [Fact]
    public void NoChildrenGivesWholeRange()
    {
        Assert.Equal((0, 20), LineBands.LargestFree(0, 20, Array.Empty<(int, int)>()));
    }

    [Fact]
    public void PicksLargestGapBetweenChildren()
    {
        var children = new[] { (5, 9), (12, 30) };
        // Gaps: 0-4 (5 lines), 10-11 (2 lines), 31-40 (10 lines).
        Assert.Equal((31, 40), LineBands.LargestFree(0, 40, children));
    }

    [Fact]
    public void AllLinesTakenGivesNull()
    {
        Assert.Null(LineBands.LargestFree(0, 10, new[] { (0, 10) }));
    }

    [Fact]
    public void ChildrenOutsideRangeAreIgnored()
    {
        Assert.Equal((10, 20), LineBands.LargestFree(10, 20, new[] { (0, 5), (30, 40) }));
    }

    [Fact]
    public void MaxColumnsIsLimitedToTheBand()
    {
        var file = new ParsedFile(Array.Empty<SymbolSpan>(), new[] { 10, 80, 20, 5 });
        Assert.Equal(80, file.MaxColumns(0, 3));
        Assert.Equal(20, file.MaxColumns(2, 3));
        Assert.Equal(5, file.MaxColumns(3, 99));
    }
}
