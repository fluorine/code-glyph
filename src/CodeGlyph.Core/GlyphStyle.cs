namespace CodeGlyph
{
    public enum ColorMode { Theme, Custom, BySymbolKind, ByIconHash }

    public enum RenderStyle { Outline, Filled, Duotone }

    /// <summary>How sure the model must be before an icon is shown. Strict is 0.7, Balanced 0.5, Generous 0.4.</summary>
    public enum IconSensitivity { Strict, Balanced, Generous }

    public enum HorizontalAlignment { ViewportRight, AfterCode, CenterOfFreeSpace }

    public enum VerticalAlignment { Top, Center, Bottom }

    public enum VerticalBehavior { ScrollWithCode, StickyInSymbol }

    public enum SymbolKind { Type, Method, Property }

    public sealed record GlyphStyle
    {
        public ColorMode ColorMode { get; init; } = ColorMode.Theme;
        public string CustomColor { get; init; } = "#808080";
        public string TypeColor { get; init; } = "#4EC9B0";
        public string MethodColor { get; init; } = "#DCDCAA";
        public string PropertyColor { get; init; } = "#9CDCFE";
        public double Opacity { get; init; } = 0.06;
        public RenderStyle RenderStyle { get; init; } = RenderStyle.Outline;
        public double StrokeWidth { get; init; } = 2;
        public HorizontalAlignment HorizontalAlignment { get; init; } = HorizontalAlignment.ViewportRight;
        public VerticalAlignment VerticalAlignment { get; init; } = VerticalAlignment.Center;
        public VerticalBehavior VerticalBehavior { get; init; } = VerticalBehavior.StickyInSymbol;
        public double MaxSize { get; init; } = 200;
        public double MinSize { get; init; } = 48;
        public double MarginToCode { get; init; } = 24;
        public double MarginToEdge { get; init; } = 24;
        public bool AllowOverCode { get; init; } = true;
        public int FadeInMs { get; init; } = 700;
        public bool VerbBadges { get; init; } = true;
        public bool UseLocalWords { get; init; } = true;
        public bool ShowForTypes { get; init; } = true;
        public bool ShowForMethods { get; init; } = true;
        public bool ShowForProperties { get; init; } = true;
        public int MinSymbolLines { get; init; } = 4;
        public IconSensitivity Sensitivity { get; init; } = IconSensitivity.Balanced;

        /// <summary>Advanced. An exact limit from 0 to 1. 0 means use Sensitivity.</summary>
        public double MinConfidence { get; init; } = 0;

        public double ConfidenceLimit =>
            MinConfidence > 0 ? MinConfidence
            : Sensitivity == IconSensitivity.Strict ? 0.7
            : Sensitivity == IconSensitivity.Generous ? 0.4 : 0.5;

        public bool Shows(SymbolKind kind) =>
            kind == SymbolKind.Type ? ShowForTypes :
            kind == SymbolKind.Method ? ShowForMethods : ShowForProperties;
    }
}
