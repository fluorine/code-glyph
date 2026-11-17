using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Utilities;

namespace CodeGlyph.Vsix.Editor
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("CSharp")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class GlyphViewListener : IWpfTextViewCreationListener
    {
        public const string LayerName = "CodeGlyph";

        // Before Selection means behind selection, and so behind the text and caret too.
        [Export(typeof(AdornmentLayerDefinition))]
        [Name(LayerName)]
        [Order(Before = PredefinedAdornmentLayers.Selection)]
        [TextViewRole(PredefinedTextViewRoles.Document)]
        public AdornmentLayerDefinition? Layer;

        [Import]
        public IEditorFormatMapService? FormatMapService;

        public void TextViewCreated(IWpfTextView textView)
        {
            if (FormatMapService == null) return;
            textView.Properties.GetOrCreateSingletonProperty(
                () => new GlyphAdornmentManager(textView, FormatMapService.GetEditorFormatMap(textView)));
        }
    }

    internal sealed class GlyphAdornmentManager : IDisposable
    {
        private const int MaxFileChars = 600_000;

        private readonly IWpfTextView _view;
        private readonly IAdornmentLayer _layer;
        private readonly IEditorFormatMap _formatMap;
        private readonly DispatcherTimer _debounce;
        private readonly DispatcherTimer _resolveTimer;
        private readonly Dictionary<string, DateTime> _firstShown = new Dictionary<string, DateTime>();
        // Not cancelled by edits. A provider call that has started is already billed.
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Dictionary<string, Brush> _brushes = new Dictionary<string, Brush>();
        private ParsedFile? _parsed;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private Brush _themeBrush = Brushes.Gray;
        private bool _disposed;

        public GlyphAdornmentManager(IWpfTextView view, IEditorFormatMap formatMap)
        {
            _view = view;
            _formatMap = formatMap;
            _layer = view.GetAdornmentLayer(GlyphViewListener.LayerName);

            _debounce = new DispatcherTimer(DispatcherPriority.Background, view.VisualElement.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(500),
            };
            _debounce.Tick += (s, e) => { _debounce.Stop(); ParseNow(false); };
            _resolveTimer = new DispatcherTimer(DispatcherPriority.Background, view.VisualElement.Dispatcher);
            _resolveTimer.Tick += (s, e) => { _resolveTimer.Stop(); ResolveNow(); };

            _view.LayoutChanged += OnLayoutChanged;
            _view.ViewportWidthChanged += OnViewportChanged;
            _view.TextBuffer.Changed += OnBufferChanged;
            _view.Closed += OnClosed;
            _formatMap.FormatMappingChanged += OnFormatChanged;
            GlyphServices.SettingsChanged += OnSettingsChanged;
            GlyphServices.Pipeline.Resolved += OnResolved;

            UpdateThemeBrush();
            ParseNow();
        }

        private void OnClosed(object sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _debounce.Stop();
            _resolveTimer.Stop();
            _cts.Cancel();
            _lifetime.Cancel();
            _view.LayoutChanged -= OnLayoutChanged;
            _view.ViewportWidthChanged -= OnViewportChanged;
            _view.TextBuffer.Changed -= OnBufferChanged;
            _view.Closed -= OnClosed;
            _formatMap.FormatMappingChanged -= OnFormatChanged;
            GlyphServices.SettingsChanged -= OnSettingsChanged;
            GlyphServices.Pipeline.Resolved -= OnResolved;
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e) => Redraw();
        private void OnViewportChanged(object sender, EventArgs e) => Redraw();

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            _debounce.Stop();
            _debounce.Start();
        }

        private void OnFormatChanged(object sender, FormatItemsEventArgs e)
        {
            UpdateThemeBrush();
            _brushes.Clear();
            Redraw();
        }

        private void OnSettingsChanged() => OnUi(() =>
        {
            _brushes.Clear();
            ParseNow();
            Redraw();
        });

        private void OnResolved() => OnUi(Redraw);

        private void OnUi(Action action)
        {
            if (_disposed) return;
            var dispatcher = _view.VisualElement.Dispatcher;
            if (dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }

        private void UpdateThemeBrush()
        {
            try
            {
                var props = _formatMap.GetProperties("Plain Text");
                if (props.Contains(EditorFormatDefinition.ForegroundBrushId) && props[EditorFormatDefinition.ForegroundBrushId] is Brush b)
                {
                    _themeBrush = b;
                    return;
                }
                if (props.Contains(EditorFormatDefinition.ForegroundColorId) && props[EditorFormatDefinition.ForegroundColorId] is Color c)
                {
                    _themeBrush = new SolidColorBrush(c);
                }
            }
            catch (Exception)
            {
                _themeBrush = Brushes.Gray;
            }
        }

        private void ResolveNow()
        {
            var parsed = _parsed;
            if (_disposed || parsed == null || !GlyphServices.Settings.Enabled) return;
            var descriptors = parsed.Symbols.Select(s => s.Descriptor).ToList();
            var token = _lifetime.Token;
            Task.Run(async () =>
            {
                try { await GlyphServices.Pipeline.ResolveAsync(descriptors, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { GlyphServices.Log("CodeGlyph failed: " + ex.Message); }
            });
        }

        private void ParseNow(bool immediate = true)
        {
            if (_disposed) return;
            if (!GlyphServices.Settings.Enabled)
            {
                _layer.RemoveAllAdornments();
                return;
            }

            var snapshot = _view.TextSnapshot;
            if (snapshot.Length > MaxFileChars) return;

            var text = snapshot.GetText();
            var fileName = FileNameOf(_view.TextBuffer);
            var tabSize = _view.Options.GetOptionValue(DefaultOptions.TabSizeOptionId);

            _cts.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            // Parse and provider calls run on the thread pool. They never block the editor.
            Task.Run(async () =>
            {
                try
                {
                    var parsed = RoslynSymbolReader.Parse(text, fileName, tabSize);
                    if (ct.IsCancellationRequested) return;

                    OnUi(() =>
                    {
                        if (ct.IsCancellationRequested) return;
                        _parsed = parsed;
                        Redraw();

                        // Cached icons draw at once. Only cache misses wait for the edit delay.
                        var delay = GlyphServices.Settings.EditDelaySeconds;
                        if (immediate || delay <= 0)
                        {
                            _resolveTimer.Stop();
                            ResolveNow();
                        }
                        else
                        {
                            _resolveTimer.Stop();
                            _resolveTimer.Interval = TimeSpan.FromSeconds(delay);
                            _resolveTimer.Start();
                        }
                    });
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { GlyphServices.Log("CodeGlyph failed for " + fileName + ": " + ex.Message); }
            });
        }

        private static string FileNameOf(ITextBuffer buffer) =>
            buffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument doc) && doc.FilePath != null
                ? System.IO.Path.GetFileName(doc.FilePath)
                : "";

        private void Redraw()
        {
            if (_disposed) return;
            _layer.RemoveAllAdornments();

            var settings = GlyphServices.Settings;
            var parsed = _parsed;
            if (!settings.Enabled || parsed == null || _view.TextViewLines == null) return;

            var lines = _view.TextViewLines;
            if (lines.Count == 0) return;

            var style = settings.Style;
            var snapshot = _view.TextSnapshot;
            var pipeline = GlyphServices.Pipeline;
            var first = lines.FirstVisibleLine;
            var last = lines.LastVisibleLine;
            var firstNo = snapshot.GetLineNumberFromPosition(first.Start);
            var lastNo = snapshot.GetLineNumberFromPosition(Math.Min(last.Start, snapshot.Length));
            var columnWidth = _view.FormattedLineSource?.ColumnWidth ?? 8;

            var symbols = parsed.Symbols;
            var icons = new IconDefinition?[symbols.Count];
            var childRanges = new List<(int, int)>[symbols.Count];
            for (var i = 0; i < symbols.Count; i++)
            {
                icons[i] = pipeline.Lookup(symbols[i].Descriptor);
                if (icons[i] != null && symbols[i].ParentIndex >= 0)
                {
                    var p = symbols[i].ParentIndex;
                    (childRanges[p] ?? (childRanges[p] = new List<(int, int)>())).Add((symbols[i].StartLine, symbols[i].EndLine));
                }
            }

            for (var i = 0; i < symbols.Count; i++)
            {
                var icon = icons[i];
                if (icon == null) continue;
                var sym = symbols[i];
                if (!style.Shows(sym.Descriptor.Kind)) continue;

                var band = LineBands.LargestFree(sym.StartLine, sym.EndLine, childRanges[i] ?? Enumerable.Empty<(int, int)>());
                if (band == null) continue;
                if (band.Value.End < firstNo || band.Value.Start > lastNo) continue;

                var top = YOf(band.Value.Start, false, firstNo, lastNo, snapshot, lines) - _view.ViewportTop;
                var bottom = YOf(band.Value.End, true, firstNo, lastNo, snapshot, lines) - _view.ViewportTop;
                var codeRight = parsed.MaxColumns(band.Value.Start, band.Value.End) * columnWidth - _view.ViewportLeft;

                var placement = PlacementCalculator.Calculate(
                    top, bottom, codeRight, _view.ViewportWidth, _view.ViewportHeight, style);
                if (placement == null) continue;

                var path = BuildPath(icon, sym, style, placement.Value.Size);
                ApplyFade(path, pipeline.KeyOf(sym.Descriptor), style.FadeInMs);
                Canvas.SetLeft(path, placement.Value.Left + _view.ViewportLeft);
                Canvas.SetTop(path, placement.Value.Top + _view.ViewportTop);
                _layer.AddAdornment(AdornmentPositioningBehavior.OwnerControlled, null, null, path, null);
            }
        }

        /// <summary>
        /// Fades an icon in once, when it is first drawn in this view. Redraws during the fade
        /// continue it from the elapsed time. Scrolling never replays it.
        /// </summary>
        private void ApplyFade(Path path, string key, int fadeMs)
        {
            if (fadeMs <= 0) return;
            var now = DateTime.UtcNow;
            if (!_firstShown.TryGetValue(key, out var first))
            {
                first = now;
                _firstShown[key] = now;
            }
            var elapsed = (now - first).TotalMilliseconds;
            if (elapsed >= fadeMs) return;

            path.Opacity = 0;
            path.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(fadeMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(-elapsed),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
        }

        private static double YOf(int lineNo, bool bottom, int firstNo, int lastNo, ITextSnapshot snapshot, ITextViewLineCollection lines)
        {
            if (lineNo >= 0 && lineNo < snapshot.LineCount)
            {
                var sl = snapshot.GetLineFromLineNumber(lineNo);
                var vl = lines.GetTextViewLineContainingBufferPosition(bottom ? sl.End : sl.Start);
                if (vl != null) return bottom ? vl.Bottom : vl.Top;
            }

            var firstLine = lines.FirstVisibleLine;
            var lastLine = lines.LastVisibleLine;
            var height = firstLine.Height > 0 ? firstLine.Height : 16;
            if (lineNo < firstNo)
            {
                return firstLine.Top - (firstNo - lineNo - (bottom ? 0 : 0)) * height + (bottom ? height : 0);
            }
            return lastLine.Bottom + (lineNo - lastNo - (bottom ? 0 : 1)) * height + (bottom ? height : 0);
        }

        private Path BuildPath(IconDefinition icon, SymbolSpan sym, GlyphStyle style, double size)
        {
            var scale = size / 24.0;
            var stroke = BrushFor(icon, sym, style, style.Opacity);
            var path = new Path
            {
                Data = SvgGeometryBuilder.Get(icon),
                Stroke = stroke,
                StrokeThickness = style.StrokeWidth,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
                RenderTransform = new ScaleTransform(scale, scale),
                SnapsToDevicePixels = false,
            };
            if (style.RenderStyle == RenderStyle.Filled)
                path.Fill = BrushFor(icon, sym, style, style.Opacity);
            else if (style.RenderStyle == RenderStyle.Duotone)
                path.Fill = BrushFor(icon, sym, style, style.Opacity * 0.4);
            return path;
        }

        private Brush BrushFor(IconDefinition icon, SymbolSpan sym, GlyphStyle style, double opacity)
        {
            Color? color = null;
            switch (style.ColorMode)
            {
                case ColorMode.Custom: color = Parse(style.CustomColor); break;
                case ColorMode.BySymbolKind:
                    color = Parse(sym.Descriptor.Kind == SymbolKind.Type ? style.TypeColor
                        : sym.Descriptor.Kind == SymbolKind.Method ? style.MethodColor : style.PropertyColor);
                    break;
                case ColorMode.ByIconHash: color = HueFor(icon.Name); break;
            }

            var key = (color?.ToString() ?? "theme") + "|" + opacity.ToString("0.###", CultureInfo.InvariantCulture);
            if (_brushes.TryGetValue(key, out var cached)) return cached;

            Brush brush;
            if (color == null)
            {
                brush = _themeBrush.CloneCurrentValue();
            }
            else
            {
                brush = new SolidColorBrush(color.Value);
            }
            brush = brush.Clone();
            brush.Opacity = opacity;
            brush.Freeze();
            _brushes[key] = brush;
            return brush;
        }

        private static Color? Parse(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch (FormatException) { return null; }
        }

        private static Color HueFor(string name)
        {
            uint h = 2166136261;
            foreach (var c in name) h = (h ^ c) * 16777619;
            return FromHsl(h % 360, 0.55, 0.55);
        }

        private static Color FromHsl(double h, double s, double l)
        {
            var c = (1 - Math.Abs(2 * l - 1)) * s;
            var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
            var m = l - c / 2;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }
    }
}
