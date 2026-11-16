using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace CodeGlyph.Vsix.Editor
{
    /// <summary>
    /// Turns Lucide elements into frozen WPF geometry on a 24 by 24 grid.
    /// Lucide uses only path, circle, ellipse, rect, line, polyline and polygon.
    /// </summary>
    internal static class SvgGeometryBuilder
    {
        private static readonly ConcurrentDictionary<string, Geometry> Cache = new ConcurrentDictionary<string, Geometry>();

        public static Geometry Get(IconDefinition icon) => Cache.GetOrAdd(icon.Name, _ => Build(icon));

        private static Geometry Build(IconDefinition icon)
        {
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };
            foreach (var element in icon.Elements)
            {
                var g = Convert(element);
                if (g != null) group.Children.Add(g);
            }
            group.Freeze();
            return group;
        }

        private static Geometry? Convert(IconElement e)
        {
            var a = e.Attributes;
            switch (e.Kind)
            {
                case "path":
                    return a.TryGetValue("d", out var d) ? Geometry.Parse(d) : null;
                case "circle":
                    return new EllipseGeometry(new Point(Num(a, "cx"), Num(a, "cy")), Num(a, "r"), Num(a, "r"));
                case "ellipse":
                    return new EllipseGeometry(new Point(Num(a, "cx"), Num(a, "cy")), Num(a, "rx"), Num(a, "ry"));
                case "rect":
                    {
                        var rx = a.ContainsKey("rx") ? Num(a, "rx") : (a.ContainsKey("ry") ? Num(a, "ry") : 0);
                        var ry = a.ContainsKey("ry") ? Num(a, "ry") : rx;
                        return new RectangleGeometry(new Rect(Num(a, "x"), Num(a, "y"), Num(a, "width"), Num(a, "height")), rx, ry);
                    }
                case "line":
                    return new LineGeometry(new Point(Num(a, "x1"), Num(a, "y1")), new Point(Num(a, "x2"), Num(a, "y2")));
                case "polyline":
                case "polygon":
                    return a.TryGetValue("points", out var pts) ? Poly(pts, e.Kind == "polygon") : null;
                default:
                    return null;
            }
        }

        private static Geometry? Poly(string points, bool closed)
        {
            var n = points.Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            if (n.Length < 4) return null;

            var figure = new PathFigure { StartPoint = new Point(n[0], n[1]), IsClosed = closed, IsFilled = closed };
            for (var i = 2; i + 1 < n.Length; i += 2)
            {
                figure.Segments.Add(new LineSegment(new Point(n[i], n[i + 1]), true));
            }
            return new PathGeometry(new[] { figure });
        }

        private static double Num(System.Collections.Generic.IReadOnlyDictionary<string, string> a, string key) =>
            a.TryGetValue(key, out var v) ? double.Parse(v, CultureInfo.InvariantCulture) : 0;
    }
}
