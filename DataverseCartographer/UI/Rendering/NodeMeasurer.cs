using System;
using System.Drawing;
using DataverseCartographer.Models;

namespace DataverseCartographer.UI.Rendering
{
    /// <summary>Sizes each node to its texts (display name + logical name), with an ellipsis beyond the maximum width.</summary>
    internal sealed class NodeMeasurer : IDisposable
    {
        private const float MinWidth = 150;
        private const float MaxWidth = 250;
        private const float TextLeft = 30;
        private const float TextRight = 12;

        private readonly Bitmap _bitmap = new Bitmap(1, 1);
        private readonly Graphics _graphics;

        public NodeMeasurer()
        {
            _graphics = Graphics.FromImage(_bitmap);
        }

        public void Fit(GraphNode node)
        {
            var title = node.Label;
            var caption = node.SubLabel;
            var textWidth = Math.Max(Measure(title, GraphStyle.TitleFont), Measure(caption, GraphStyle.CaptionFont));
            node.Width = (float)Math.Ceiling(Math.Max(MinWidth, Math.Min(MaxWidth, textWidth + TextLeft + TextRight)));
            node.Height = GraphStyle.NodeHeight;
            var available = node.Width - TextLeft - TextRight;
            node.Title = Ellipsis(title, GraphStyle.TitleFont, available);
            node.Caption = Ellipsis(caption, GraphStyle.CaptionFont, available);
        }

        private float Measure(string text, Font font) =>
            _graphics.MeasureString(text ?? string.Empty, font, PointF.Empty, StringFormat.GenericTypographic).Width;

        private string Ellipsis(string text, Font font, float width)
        {
            if (string.IsNullOrEmpty(text) || Measure(text, font) <= width) return text;
            var length = text.Length;
            while (length > 1 && Measure(text.Substring(0, length) + "…", font) > width) length--;
            return text.Substring(0, length) + "…";
        }

        public void Dispose()
        {
            _graphics.Dispose();
            _bitmap.Dispose();
        }
    }
}
