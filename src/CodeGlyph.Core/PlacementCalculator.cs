using System;

namespace CodeGlyph
{
    public readonly struct Placement
    {
        public Placement(double left, double top, double size)
        {
            Left = left;
            Top = top;
            Size = size;
        }

        public double Left { get; }
        public double Top { get; }
        public double Size { get; }
    }

    public static class PlacementCalculator
    {
        /// <summary>The icon of a partly visible block shrinks to this size, then goes away.</summary>
        private const double VanishSize = 6;

        /// <summary>
        /// All inputs are in view pixels. Returns null when the icon cannot fit
        /// without touching code, which is the only way to guarantee no overlap.
        /// </summary>
        /// <param name="bandTop">Top of the symbol's own band, in viewport coordinates.</param>
        /// <param name="bandBottom">Bottom of the symbol's own band, in viewport coordinates.</param>
        /// <param name="codeRight">Right edge of the longest code line in the band, in viewport coordinates.</param>
        /// <param name="viewportWidth">Visible width of the text area.</param>
        /// <param name="viewportHeight">Visible height of the text area.</param>
        public static Placement? Calculate(
            double bandTop, double bandBottom, double codeRight,
            double viewportWidth, double viewportHeight, GlyphStyle style)
        {
            // A block that is short in total never gets an icon.
            if (bandBottom - bandTop < style.MinSize) return null;

            var visTop = Math.Max(bandTop, 0);
            var visBottom = Math.Min(bandBottom, viewportHeight);

            // StickyInSymbol keeps the icon inside the visible part of the band. It shrinks while the
            // block scrolls out and goes away only when almost none of the block is left.
            // ScrollWithCode measures the whole band, so a partly hidden icon scrolls out.
            var sticky = style.VerticalBehavior == VerticalBehavior.StickyInSymbol;
            var spanTop = sticky ? visTop : bandTop;
            var spanBottom = sticky ? visBottom : bandBottom;
            var spanHeight = spanBottom - spanTop;
            if (spanHeight <= 0) return null;

            var freeRight = viewportWidth - style.MarginToEdge;
            // Over-code mode: the icon sits behind the text and uses the whole viewport width.
            var freeLeft = style.AllowOverCode ? style.MarginToEdge : codeRight + style.MarginToCode;
            var freeWidth = freeRight - freeLeft;

            // Not enough room beside the code, or a very narrow editor.
            if (freeWidth < style.MinSize) return null;

            var size = Math.Min(Math.Min(spanHeight, freeWidth), style.MaxSize);
            if (size < (sticky ? VanishSize : style.MinSize)) return null;

            double left;
            switch (style.HorizontalAlignment)
            {
                case HorizontalAlignment.AfterCode:
                    left = style.AllowOverCode
                        ? Math.Max(freeLeft, Math.Min(codeRight + style.MarginToCode, freeRight - size))
                        : freeLeft;
                    break;
                case HorizontalAlignment.CenterOfFreeSpace:
                    left = freeLeft + (freeWidth - size) / 2;
                    break;
                default:
                    left = freeRight - size;
                    break;
            }

            double top;
            switch (style.VerticalAlignment)
            {
                case VerticalAlignment.Top:
                    top = spanTop;
                    break;
                case VerticalAlignment.Bottom:
                    top = spanBottom - size;
                    break;
                default:
                    top = spanTop + (spanHeight - size) / 2;
                    break;
            }

            return new Placement(left, top, size);
        }
    }
}
