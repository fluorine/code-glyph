using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeGlyph
{
    public sealed class SymbolSpan
    {
        public SymbolSpan(SymbolDescriptor descriptor, int startLine, int endLine, int parentIndex)
        {
            Descriptor = descriptor;
            StartLine = startLine;
            EndLine = endLine;
            ParentIndex = parentIndex;
        }

        public SymbolDescriptor Descriptor { get; }

        /// <summary>Zero-based, inclusive.</summary>
        public int StartLine { get; }
        public int EndLine { get; }

        /// <summary>Index into <see cref="ParsedFile.Symbols"/>, or -1 for a top-level symbol.</summary>
        public int ParentIndex { get; }
    }

    public sealed class ParsedFile
    {
        public ParsedFile(IReadOnlyList<SymbolSpan> symbols, int[] columnsPerLine)
        {
            Symbols = symbols;
            ColumnsPerLine = columnsPerLine;
        }

        public IReadOnlyList<SymbolSpan> Symbols { get; }

        /// <summary>Width of each line in character columns, tabs already expanded.</summary>
        public int[] ColumnsPerLine { get; }

        public int MaxColumns(int startLine, int endLine)
        {
            var max = 0;
            var last = Math.Min(endLine, ColumnsPerLine.Length - 1);
            for (var i = Math.Max(startLine, 0); i <= last; i++)
            {
                if (ColumnsPerLine[i] > max) max = ColumnsPerLine[i];
            }
            return max;
        }
    }

    public static class LineBands
    {
        /// <summary>
        /// The largest run of lines in [start, end] that no child range uses.
        /// A type uses it for its own icon, so the icon never sits behind a member's icon.
        /// Returns null when every line belongs to a child.
        /// </summary>
        public static (int Start, int End)? LargestFree(int start, int end, IEnumerable<(int Start, int End)> children)
        {
            var taken = children
                .Where(c => c.End >= start && c.Start <= end)
                .OrderBy(c => c.Start)
                .ToList();

            (int Start, int End)? best = null;
            var cursor = start;
            foreach (var c in taken)
            {
                if (c.Start > cursor) Consider(cursor, c.Start - 1, ref best);
                cursor = Math.Max(cursor, c.End + 1);
            }
            if (cursor <= end) Consider(cursor, end, ref best);
            return best;
        }

        private static void Consider(int s, int e, ref (int Start, int End)? best)
        {
            if (best == null || e - s > best.Value.End - best.Value.Start) best = (s, e);
        }
    }
}
