using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using DataverseCartographer.Models;
using DataverseCartographer.UI;
using DataverseCartographer.UI.Rendering;

namespace DataverseCartographer.Export
{
    /// <summary>
    /// Picture of the whole displayed graph (not only the visible part of the screen), with a title line.
    /// </summary>
    public static class ImageExporter
    {
        /// <summary>Beyond this many pixels, the PNG resolution is lowered to keep the memory reasonable.</summary>
        private const double MaxPixels = 45_000_000;
        private const int MaxSide = 16000;

        public static void SavePng(Graph graph, string path, string title, string subtitle)
        {
            var bounds = GraphRenderer.ExportBounds(graph, !string.IsNullOrEmpty(title));
            var scale = Math.Min(2.0, Math.Sqrt(MaxPixels / (bounds.Width * bounds.Height)));
            scale = Math.Min(scale, Math.Min(MaxSide / bounds.Width, MaxSide / bounds.Height));
            var width = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
            var height = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.White);
                    g.ScaleTransform((float)scale, (float)scale);
                    g.TranslateTransform(-bounds.X, -bounds.Y);
                    GraphRenderer.RenderExport(new GdiSurface(g), graph, bounds, title, subtitle);
                }
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        public static void SaveSvg(Graph graph, string path, string title, string subtitle)
        {
            var bounds = GraphRenderer.ExportBounds(graph, !string.IsNullOrEmpty(title));
            var surface = new SvgSurface(bounds, Color.White);
            GraphRenderer.RenderExport(surface, graph, bounds, title, subtitle);
            File.WriteAllText(path, surface.Complete(), new UTF8Encoding(false));
        }
    }
}
