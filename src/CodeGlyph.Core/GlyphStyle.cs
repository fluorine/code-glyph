namespace CodeGlyph
{
    public enum ColorMode { Theme, Custom, BySymbolKind, ByIconHash }

    public enum RenderStyle { Outline, Filled, Duotone }

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
        public bool ShowForTypes { get; init; } = true;
        public bool ShowForMethods { get; init; } = true;
        public bool ShowForProperties { get; init; } = true;
        public int MinSymbolLines { get; init; } = 4;
        public double MinConfidence { get; init; } = 0.7;

        public bool Shows(SymbolKind kind) =>
            kind == SymbolKind.Type ? ShowForTypes :
            kind == SymbolKind.Method ? ShowForMethods : ShowForProperties;
    }
}
