using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Text;

namespace DataverseCartographer.UI.Rendering
{
    /// <summary>Fonts and sizes of the graph, shared by the screen, PNG and SVG renderings.</summary>
    internal static class GraphStyle
    {
        public const float NodeHeight = 50;
        public const float NodeRadius = 9;

        public static readonly Font TitleFont = new Font("Segoe UI Semibold", 9.5F);
        public static readonly Font CaptionFont = new Font("Segoe UI", 8F);
        public static readonly Font LabelFont = new Font("Segoe UI", 7.5F);
        public static readonly Font HeadingFont = new Font("Segoe UI Semibold", 14F);

        private static readonly Bitmap MeasureBitmap = new Bitmap(1, 1);
        private static readonly Graphics MeasureGraphics = Graphics.FromImage(MeasureBitmap);

        public static SizeF Measure(string text, Font font)
        {
            lock (MeasureGraphics)
            {
                return MeasureGraphics.MeasureString(text ?? string.Empty, font, PointF.Empty, StringFormat.GenericTypographic);
            }
        }
    }

    /// <summary>
    /// The few drawing primitives the graph needs. Implemented on GDI+ (screen, PNG) and as SVG markup.
    /// </summary>
    internal interface IGraphSurface
    {
        void FillRoundRect(RectangleF bounds, float radius, Color color);
        void StrokeRoundRect(RectangleF bounds, float radius, Color color, float width, bool dashed);
        void FillCircle(PointF center, float radius, Color color);
        void Line(PointF from, PointF to, Color color, float width);
        void Bezier(PointF p0, PointF c1, PointF c2, PointF p3, Color color, float width, bool dashed);

        /// <summary>Single line of text, vertically centered in <paramref name="bounds"/>.</summary>
        void Text(string text, Font font, Color color, RectangleF bounds, bool center);
    }

    internal sealed class GdiSurface : IGraphSurface
    {
        private readonly Graphics _g;

        private static readonly StringFormat NearFormat = Format(StringAlignment.Near);
        private static readonly StringFormat CenterFormat = Format(StringAlignment.Center);

        public GdiSurface(Graphics graphics)
        {
            _g = graphics;
            _g.SmoothingMode = SmoothingMode.AntiAlias;
            _g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            _g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        private static StringFormat Format(StringAlignment alignment)
        {
            var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.Alignment = alignment;
            format.LineAlignment = StringAlignment.Center;
            format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
            format.Trimming = StringTrimming.None;
            return format;
        }

        public void FillRoundRect(RectangleF bounds, float radius, Color color)
        {
            using (var path = Theme.RoundedRect(bounds, radius))
            using (var brush = new SolidBrush(color))
            {
                _g.FillPath(brush, path);
            }
        }

        public void StrokeRoundRect(RectangleF bounds, float radius, Color color, float width, bool dashed)
        {
            using (var path = Theme.RoundedRect(bounds, radius))
            using (var pen = NewPen(color, width, dashed))
            {
                _g.DrawPath(pen, path);
            }
        }

        public void FillCircle(PointF center, float radius, Color color)
        {
            using (var brush = new SolidBrush(color))
            {
                _g.FillEllipse(brush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
            }
        }

        public void Line(PointF from, PointF to, Color color, float width)
        {
            using (var pen = NewPen(color, width, false))
            {
                _g.DrawLine(pen, from, to);
            }
        }

        public void Bezier(PointF p0, PointF c1, PointF c2, PointF p3, Color color, float width, bool dashed)
        {
            using (var pen = NewPen(color, width, dashed))
            {
                _g.DrawBezier(pen, p0, c1, c2, p3);
            }
        }

        public void Text(string text, Font font, Color color, RectangleF bounds, bool center)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var brush = new SolidBrush(color))
            {
                _g.DrawString(text, font, brush, bounds, center ? CenterFormat : NearFormat);
            }
        }

        private static Pen NewPen(Color color, float width, bool dashed)
        {
            var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            if (dashed)
            {
                pen.DashCap = DashCap.Round;
                pen.DashPattern = new[] { 4f, 3f };
            }
            return pen;
        }
    }

    internal sealed class SvgSurface : IGraphSurface
    {
        private readonly StringBuilder _svg = new StringBuilder();
        private readonly RectangleF _viewBox;

        public SvgSurface(RectangleF viewBox, Color background)
        {
            _viewBox = viewBox;
            _svg.AppendFormat(CultureInfo.InvariantCulture,
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}\" height=\"{1}\" viewBox=\"{2} {3} {0} {1}\" font-family=\"Segoe UI, Arial, sans-serif\">\n",
                N(viewBox.Width), N(viewBox.Height), N(viewBox.X), N(viewBox.Y));
            _svg.AppendFormat("<rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\" {4}/>\n", N(viewBox.X), N(viewBox.Y), N(viewBox.Width), N(viewBox.Height), Fill(background));
        }

        public string Complete() => _svg + "</svg>\n";

        public void FillRoundRect(RectangleF b, float radius, Color color)
        {
            _svg.AppendFormat("<rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\" rx=\"{4}\" {5}/>\n", N(b.X), N(b.Y), N(b.Width), N(b.Height), N(radius), Fill(color));
        }

        public void StrokeRoundRect(RectangleF b, float radius, Color color, float width, bool dashed)
        {
            _svg.AppendFormat("<rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\" rx=\"{4}\" fill=\"none\" {5}/>\n", N(b.X), N(b.Y), N(b.Width), N(b.Height), N(radius), Stroke(color, width, dashed));
        }

        public void FillCircle(PointF c, float radius, Color color)
        {
            _svg.AppendFormat("<circle cx=\"{0}\" cy=\"{1}\" r=\"{2}\" {3}/>\n", N(c.X), N(c.Y), N(radius), Fill(color));
        }

        public void Line(PointF a, PointF b, Color color, float width)
        {
            _svg.AppendFormat("<line x1=\"{0}\" y1=\"{1}\" x2=\"{2}\" y2=\"{3}\" {4}/>\n", N(a.X), N(a.Y), N(b.X), N(b.Y), Stroke(color, width, false));
        }

        public void Bezier(PointF p0, PointF c1, PointF c2, PointF p3, Color color, float width, bool dashed)
        {
            _svg.AppendFormat("<path d=\"M{0},{1} C{2},{3} {4},{5} {6},{7}\" fill=\"none\" {8}/>\n",
                N(p0.X), N(p0.Y), N(c1.X), N(c1.Y), N(c2.X), N(c2.Y), N(p3.X), N(p3.Y), Stroke(color, width, dashed));
        }

        public void Text(string text, Font font, Color color, RectangleF b, bool center)
        {
            if (string.IsNullOrEmpty(text)) return;
            var weight = font.Name.IndexOf("Semibold", StringComparison.OrdinalIgnoreCase) >= 0 || font.Bold ? " font-weight=\"600\"" : string.Empty;
            var x = center ? b.X + b.Width / 2 : b.X;
            _svg.AppendFormat("<text x=\"{0}\" y=\"{1}\" font-size=\"{2}\"{3}{4} dominant-baseline=\"central\" {5}>{6}</text>\n",
                N(x), N(b.Y + b.Height / 2), N(font.SizeInPoints * 96f / 72f), weight, center ? " text-anchor=\"middle\"" : string.Empty, Fill(color), Escape(text));
        }

        private static string Fill(Color color) =>
            color.A == 255 ? $"fill=\"{Hex(color)}\"" : $"fill=\"{Hex(color)}\" fill-opacity=\"{N(color.A / 255f)}\"";

        private static string Stroke(Color color, float width, bool dashed)
        {
            var result = $"stroke=\"{Hex(color)}\" stroke-width=\"{N(width)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"";
            if (color.A != 255) result += $" stroke-opacity=\"{N(color.A / 255f)}\"";
            if (dashed) result += $" stroke-dasharray=\"{N(4 * width)} {N(3 * width)}\"";
            return result;
        }

        private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        private static string N(float value) => Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);

        private static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
