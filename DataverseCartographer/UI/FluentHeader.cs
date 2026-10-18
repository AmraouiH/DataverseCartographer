using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace DataverseCartographer.UI
{
    /// <summary>
    /// Command bar of the author's tools (same look as Entity Fields Analyser): white surface, bottom hairline,
    /// rounded hover states, Windows icon-font glyphs. Items tagged "primary" are drawn as the accent button.
    /// </summary>
    internal sealed class FluentHeaderRenderer : ToolStripProfessionalRenderer
    {
        public static readonly Color Surface = Color.White;
        public static readonly Color Stroke = Color.FromArgb(224, 224, 224);
        public static readonly Color StrokeSubtle = Color.FromArgb(240, 240, 240);
        public static readonly Color SurfacePressed = Color.FromArgb(224, 224, 224);
        public static readonly Color TextPrimary = Color.FromArgb(36, 36, 36);
        public static readonly Color TextSecondary = Color.FromArgb(97, 97, 97);
        public static readonly Color TextDisabled = Color.FromArgb(189, 189, 189);
        public static readonly Color Brand = Color.FromArgb(15, 108, 189);
        public static readonly Color BrandHover = Color.FromArgb(17, 94, 163);
        public static readonly Color BrandPressed = Color.FromArgb(12, 59, 94);

        public FluentHeaderRenderer()
        {
            RoundedEdges = false;
        }

        private static bool IsPrimary(ToolStripItem item) => "primary".Equals(item.Tag as string);

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(Surface)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Stroke)) e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) => PaintBackground(e);

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => PaintBackground(e);

        private static void PaintBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0.5f, 1.5f, item.Width - 1.5f, item.Height - 3f);

            if (IsPrimary(item))
            {
                var fill = !item.Enabled ? StrokeSubtle : item.Pressed ? BrandPressed : item.Selected ? BrandHover : Brand;
                Fill(g, fill, bounds);
                return;
            }
            if (!item.Enabled) return;
            if (item.Pressed) Fill(g, SurfacePressed, bounds);
            else if (item.Selected) Fill(g, Theme.Blend(Surface, SurfacePressed, 0.45f), bounds);
        }

        private static void Fill(Graphics g, Color color, RectangleF bounds)
        {
            using (var path = Theme.RoundedRect(bounds, 4))
            using (var brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = IsPrimary(e.Item) ? Color.White : TextSecondary;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                base.OnRenderItemText(e);
                return;
            }
            e.TextColor = !e.Item.Enabled ? TextDisabled : IsPrimary(e.Item) ? Color.White : TextPrimary;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Item.Enabled || e.Image == null)
            {
                base.OnRenderItemImage(e);
                return;
            }
            var c = TextDisabled;
            var matrix = new ColorMatrix(new[]
            {
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new[] { c.R / 255f, c.G / 255f, c.B / 255f, 0, 1 }
            });
            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(matrix);
                var r = e.ImageRectangle;
                e.Graphics.DrawImage(e.Image, r, 0, 0, e.Image.Width, e.Image.Height, GraphicsUnit.Pixel, attributes);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                base.OnRenderSeparator(e);
                return;
            }
            var x = e.Item.Width / 2;
            using (var pen = new Pen(Stroke)) e.Graphics.DrawLine(pen, x, 8, x, e.Item.Height - 8);
        }
    }

    /// <summary>Glyphs of the Windows icon fonts (Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10).</summary>
    internal static class FluentIcons
    {
        public const char Close = '';
        public const char Play = '';
        public const char Refresh = '';
        public const char Export = '';
        public const char Globe = '';
        public const char Heart = '';
        public const char Contact = '';
        public const char Help = '';

        private static string _fontName;
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

        private static string FontName
        {
            get
            {
                if (_fontName != null) return _fontName;
                _fontName = "Segoe MDL2 Assets";
                using (var fonts = new InstalledFontCollection())
                {
                    foreach (var family in fonts.Families)
                    {
                        if (family.Name != "Segoe Fluent Icons") continue;
                        _fontName = family.Name;
                        break;
                    }
                }
                return _fontName;
            }
        }

        public static Bitmap Icon(char glyph, Color color, int size = 16)
        {
            var key = $"{(int)glyph}-{color.ToArgb()}-{size}";
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var bitmap = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bitmap))
            using (var font = new Font(FontName, size, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 1, size, size), format);
            }
            Cache[key] = bitmap;
            return bitmap;
        }
    }
}
