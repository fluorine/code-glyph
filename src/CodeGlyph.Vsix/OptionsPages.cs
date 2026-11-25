using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using CodeGlyph.Selectors;
using Microsoft.VisualStudio.Shell;

namespace CodeGlyph.Vsix
{
    [ComVisible(true)]
    [Guid("2b9e6f40-1d83-4c7a-8e52-a6f0d4c1b837")]
    public class ProviderOptionsPage : DialogPage
    {
        [Category("General")]
        [DisplayName("Show icons")]
        [Description("Turns all icons on or off.")]
        public bool Enabled { get; set; } = true;

        [Category("General")]
        [DisplayName("Provider")]
        [Description("Auto uses an API key when one exists, then falls back to the Claude Code CLI. The model receives symbol names and comments. It never receives method bodies.")]
        public ProviderKind Provider { get; set; } = ProviderKind.Auto;

        [Category("General")]
        [DisplayName("Minimum confidence")]
        [Description("The model must be at least this sure (0 to 1). Higher values show fewer icons.")]
        public double MinConfidence { get; set; } = 0.7;

        [Category("General")]
        [DisplayName("Wait after editing (seconds)")]
        [Description("Provider calls for new or changed symbols wait this long after your last edit. Cached icons are not delayed. 0 to 600.")]
        public int EditDelaySeconds { get; set; } = 15;

        [Category("General")]
        [DisplayName("Log usage")]
        [Description("Writes tokens, cost and session totals to the CodeGlyph Output pane after each provider call.")]
        public bool LogUsage { get; set; } = true;

        [Category("Claude API")]
        [DisplayName("API key")]
        [Description("Stored encrypted for your Windows user. If empty, ANTHROPIC_API_KEY is used.")]
        [PasswordPropertyText(true)]
        public string AnthropicKey { get; set; } = "";

        [Category("Claude API")]
        [DisplayName("Model")]
        public string AnthropicModel { get; set; } = "claude-haiku-5-5";

        [Category("OpenAI")]
        [DisplayName("API key")]
        [Description("Stored encrypted for your Windows user. If empty, OPENAI_API_KEY is used.")]
        [PasswordPropertyText(true)]
        public string OpenAiKey { get; set; } = "";

        [Category("OpenAI")]
        [DisplayName("Model")]
        public string OpenAiModel { get; set; } = "gpt-4.1-mini";

        [Category("OpenAI")]
        [DisplayName("Base URL")]
        [Description("Any OpenAI-compatible endpoint, for example Azure OpenAI or Ollama.")]
        public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";

        [Category("Claude Code CLI")]
        [DisplayName("Command")]
        public string ClaudeCliPath { get; set; } = "claude";

        [Category("Claude Code CLI")]
        [DisplayName("Model")]
        public string ClaudeCliModel { get; set; } = "haiku";

        public override void LoadSettingsFromStorage()
        {
            var s = GlyphServices.Settings;
            Enabled = s.Enabled;
            EditDelaySeconds = s.EditDelaySeconds;
            LogUsage = s.LogUsage;
            MinConfidence = s.Style.MinConfidence;
            Provider = s.Provider.Provider;
            AnthropicKey = s.Provider.AnthropicKey ?? "";
            AnthropicModel = s.Provider.AnthropicModel;
            OpenAiKey = s.Provider.OpenAiKey ?? "";
            OpenAiModel = s.Provider.OpenAiModel;
            OpenAiBaseUrl = s.Provider.OpenAiBaseUrl;
            ClaudeCliPath = s.Provider.ClaudeCliPath;
            ClaudeCliModel = s.Provider.ClaudeCliModel;
        }

        public override void SaveSettingsToStorage()
        {
            GlyphServices.Update(s =>
            {
                s.Enabled = Enabled;
                s.EditDelaySeconds = System.Math.Max(0, System.Math.Min(600, EditDelaySeconds));
                s.LogUsage = LogUsage;
                s.Style = s.Style with { MinConfidence = System.Math.Max(0, System.Math.Min(1, MinConfidence)) };
                s.Provider = new ProviderSettings
                {
                    Provider = Provider,
                    AnthropicKey = AnthropicKey,
                    AnthropicModel = AnthropicModel,
                    OpenAiKey = OpenAiKey,
                    OpenAiModel = OpenAiModel,
                    OpenAiBaseUrl = OpenAiBaseUrl,
                    ClaudeCliPath = ClaudeCliPath,
                    ClaudeCliModel = ClaudeCliModel,
                };
            });
        }
    }

    [ComVisible(true)]
    [Guid("d47a0c95-6e12-4b38-9f7d-1c8e5a2b6049")]
    public class AppearanceOptionsPage : DialogPage
    {
        [Category("Color")] [DisplayName("Color mode")]
        [Description("Theme follows the editor text color. Custom uses one color. BySymbolKind uses the three kind colors. ByIconHash gives each icon its own stable hue.")]
        public ColorMode ColorMode { get; set; } = ColorMode.Theme;

        [Category("Color")] [DisplayName("Custom color")]
        public Color CustomColor { get; set; } = Color.Gray;

        [Category("Color")] [DisplayName("Type color")]
        public Color TypeColor { get; set; } = ColorTranslator.FromHtml("#4EC9B0");

        [Category("Color")] [DisplayName("Method color")]
        public Color MethodColor { get; set; } = ColorTranslator.FromHtml("#DCDCAA");

        [Category("Color")] [DisplayName("Property color")]
        public Color PropertyColor { get; set; } = ColorTranslator.FromHtml("#9CDCFE");

        [Category("Color")] [DisplayName("Opacity (%)")]
        [Description("1 to 30. Low values keep the icon subtle.")]
        public int OpacityPercent { get; set; } = 6;

        [Category("Style")] [DisplayName("Render style")]
        public RenderStyle RenderStyle { get; set; } = RenderStyle.Outline;

        [Category("Style")] [DisplayName("Stroke width")]
        [Description("1 to 4, on the 24 unit icon grid.")]
        public double StrokeWidth { get; set; } = 2;

        [Category("Layout")] [DisplayName("Horizontal alignment")]
        public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.ViewportRight;

        [Category("Layout")] [DisplayName("Vertical alignment")]
        public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Center;

        [Category("Layout")] [DisplayName("Vertical behavior")]
        [Description("StickyInSymbol keeps the icon visible while any part of the symbol is on screen.")]
        public VerticalBehavior VerticalBehavior { get; set; } = VerticalBehavior.StickyInSymbol;

        [Category("Style")] [DisplayName("Fade-in time (ms)")]
        [Description("An icon fades in softly when it first appears. 0 turns the fade off.")]
        public int FadeInMs { get; set; } = 700;

        [Category("Layout")] [DisplayName("Maximum size (px)")]
        public int MaxSize { get; set; } = 200;

        [Category("Layout")] [DisplayName("Minimum size (px)")]
        [Description("If the free space is smaller than this, no icon is shown. This keeps icons off the code.")]
        public int MinSize { get; set; } = 48;

        [Category("Layout")] [DisplayName("Margin to code (px)")]
        public int MarginToCode { get; set; } = 24;

        [Category("Layout")] [DisplayName("Margin to edge (px)")]
        public int MarginToEdge { get; set; } = 24;

        [Category("Layout")] [DisplayName("Allow icon behind code")]
        [Description("True: the icon sits behind the text, always visible, no scrolling. False: the icon uses only free space right of the code.")]
        public bool AllowOverCode { get; set; } = true;

        [Category("Appearance")] [DisplayName("Verb badges")]
        [Description("Show a small badge for the first word of a method name: Create, Delete, Get, Send, Validate, Update, Sync.")]
        public bool VerbBadges { get; set; } = true;

        [Category("Show for")] [DisplayName("Types")]
        public bool ShowForTypes { get; set; } = true;

        [Category("Show for")] [DisplayName("Methods")]
        public bool ShowForMethods { get; set; } = true;

        [Category("Show for")] [DisplayName("Properties")]
        public bool ShowForProperties { get; set; } = true;

        [Category("Show for")] [DisplayName("Minimum symbol length (lines)")]
        public int MinSymbolLines { get; set; } = 4;

        public override void LoadSettingsFromStorage()
        {
            var s = GlyphServices.Settings.Style;
            ColorMode = s.ColorMode;
            CustomColor = ColorTranslator.FromHtml(s.CustomColor);
            TypeColor = ColorTranslator.FromHtml(s.TypeColor);
            MethodColor = ColorTranslator.FromHtml(s.MethodColor);
            PropertyColor = ColorTranslator.FromHtml(s.PropertyColor);
            OpacityPercent = (int)System.Math.Round(s.Opacity * 100);
            RenderStyle = s.RenderStyle;
            StrokeWidth = s.StrokeWidth;
            HorizontalAlignment = s.HorizontalAlignment;
            VerticalAlignment = s.VerticalAlignment;
            VerticalBehavior = s.VerticalBehavior;
            MaxSize = (int)s.MaxSize;
            MinSize = (int)s.MinSize;
            MarginToCode = (int)s.MarginToCode;
            MarginToEdge = (int)s.MarginToEdge;
            AllowOverCode = s.AllowOverCode;
            FadeInMs = s.FadeInMs;
            VerbBadges = s.VerbBadges;
            ShowForTypes = s.ShowForTypes;
            ShowForMethods = s.ShowForMethods;
            ShowForProperties = s.ShowForProperties;
            MinSymbolLines = s.MinSymbolLines;
        }

        public override void SaveSettingsToStorage()
        {
            GlyphServices.Update(s =>
            {
                s.Style = s.Style with
                {
                    ColorMode = ColorMode,
                    CustomColor = Hex(CustomColor),
                    TypeColor = Hex(TypeColor),
                    MethodColor = Hex(MethodColor),
                    PropertyColor = Hex(PropertyColor),
                    Opacity = Clamp(OpacityPercent, 1, 30) / 100.0,
                    RenderStyle = RenderStyle,
                    StrokeWidth = Clamp(StrokeWidth, 1, 4),
                    HorizontalAlignment = HorizontalAlignment,
                    VerticalAlignment = VerticalAlignment,
                    VerticalBehavior = VerticalBehavior,
                    MaxSize = Clamp(MaxSize, 24, 600),
                    MinSize = Clamp(MinSize, 16, 600),
                    MarginToCode = Clamp(MarginToCode, 0, 400),
                    MarginToEdge = Clamp(MarginToEdge, 0, 400),
                    AllowOverCode = AllowOverCode,
                    FadeInMs = Clamp(FadeInMs, 0, 5000),
                    VerbBadges = VerbBadges,
                    ShowForTypes = ShowForTypes,
                    ShowForMethods = ShowForMethods,
                    ShowForProperties = ShowForProperties,
                    MinSymbolLines = Clamp(MinSymbolLines, 1, 200),
                };
            });
        }

        private static string Hex(Color c) =>
            string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);

        private static double Clamp(double v, double lo, double hi) => System.Math.Max(lo, System.Math.Min(hi, v));
        private static int Clamp(int v, int lo, int hi) => System.Math.Max(lo, System.Math.Min(hi, v));
    }
}
