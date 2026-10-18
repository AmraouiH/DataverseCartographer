using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DataverseCartographer.UI.Controls
{
    /// <summary>
    /// Loading layer covering the tool while a long operation runs: a blurred and dimmed picture of the screen,
    /// with a floating card showing an animated spinner, the current step and the elapsed time.
    /// </summary>
    [DesignerCategory("Code")]
    public class LoadingOverlay : Control
    {
        private const int CardWidth = 420;
        private const int SpinnerSize = 78;

        private readonly Timer timer = new Timer { Interval = 16 };
        private Bitmap backdrop;
        private string title;
        private string message;
        private DateTime started;
        private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();

        public LoadingOverlay()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Visible = false;
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Cursor = Cursors.WaitCursor;
            timer.Tick += (s, e) =>
            {
                Invalidate(Rectangle.Inflate(CardBounds, 24, 24));
            };
        }

        /// <summary>Covers the host with a blurred picture of its current content and starts the animation</summary>
        public void Begin(Control host, string title, string message)
        {
            this.title = title;
            this.message = message;

            backdrop?.Dispose();
            backdrop = Snapshot(host);

            // Not docked: the layer covers the whole host, header included, whatever the other docked controls
            Bounds = new Rectangle(Point.Empty, host.ClientSize);
            started = DateTime.Now;
            clock.Restart();
            Visible = true;
            BringToFront();
            Focus();
            timer.Start();
            Invalidate();
        }

        public void SetMessage(string text)
        {
            message = text;
            Invalidate(Rectangle.Inflate(CardBounds, 24, 24));
        }

        public void End()
        {
            timer.Stop();
            clock.Stop();
            Visible = false;
            backdrop?.Dispose();
            backdrop = null;
        }

        private Rectangle CardBounds
        {
            get
            {
                var width = Math.Min(CardWidth, Math.Max(200, Width - 40));
                return new Rectangle((Width - width) / 2, (Height - 118) / 2, width, 118);
            }
        }

        /// <summary>
        /// Picture of the host, blurred by shrinking then enlarging it, then dimmed with the header color
        /// </summary>
        private static Bitmap Snapshot(Control host)
        {
            var width = Math.Max(1, host.ClientSize.Width);
            var height = Math.Max(1, host.ClientSize.Height);
            var result = new Bitmap(width, height);

            try
            {
                using (var full = new Bitmap(width, height))
                using (var small = new Bitmap(Math.Max(1, width / 10), Math.Max(1, height / 10)))
                {
                    host.DrawToBitmap(full, new Rectangle(0, 0, width, height));
                    using (var g = Graphics.FromImage(small))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.DrawImage(full, new Rectangle(0, 0, small.Width, small.Height));
                    }
                    using (var g = Graphics.FromImage(result))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(small, new Rectangle(0, 0, width, height));
                    }
                }
            }
            catch (Exception)
            {
                // Some hosted controls cannot be drawn to a bitmap: a plain background is used instead
                using (var g = Graphics.FromImage(result))
                {
                    g.Clear(Theme.Canvas);
                }
            }

            using (var g = Graphics.FromImage(result))
            using (var dim = new SolidBrush(Color.FromArgb(120, Theme.HeaderStart)))
            {
                g.FillRectangle(dim, 0, 0, width, height);
            }
            return result;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (backdrop != null)
            {
                g.InterpolationMode = InterpolationMode.Low;
                g.DrawImage(backdrop, ClientRectangle);
            }
            else
            {
                g.Clear(Theme.Blend(Theme.Canvas, Theme.HeaderStart, 0.4f));
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            var card = CardBounds;

            // Soft shadow
            for (var i = 1; i <= 8; i++)
            {
                using (var path = Theme.RoundedRect(new RectangleF(card.X - i, card.Y - i + 4, card.Width + 2 * i, card.Height + 2 * i), 16 + i))
                using (var brush = new SolidBrush(Color.FromArgb(9 - i, 10, 12, 40)))
                {
                    g.FillPath(brush, path);
                }
            }

            using (var path = Theme.RoundedRect(card, 16))
            using (var fill = new SolidBrush(Theme.Card))
            {
                g.FillPath(fill, path);
            }

            // Thin gradient line at the top of the card, clipped to its rounded shape
            using (var path = Theme.RoundedRect(card, 16))
            {
                var state = g.Save();
                g.SetClip(path);
                var line = new Rectangle(card.X, card.Y, card.Width, 4);
                using (var brush = new LinearGradientBrush(line, Theme.Teal, Theme.Accent, LinearGradientMode.Horizontal))
                {
                    g.FillRectangle(brush, line);
                }
                g.Restore(state);
            }

            AnimatedLogo.Paint(g, new PointF(card.X + 18 + SpinnerSize / 2f, card.Y + card.Height / 2f), SpinnerSize, clock.Elapsed.TotalSeconds);

            var textLeft = card.X + 18 + SpinnerSize + 14;
            var textWidth = card.Right - textLeft - 22;
            TextRenderer.DrawText(g, title, Theme.CardTitle, new Rectangle(textLeft, card.Y + 24, textWidth, 22), Theme.Text,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, message, Theme.Base, new Rectangle(textLeft, card.Y + 50, textWidth, 36), Theme.Muted,
                TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            var elapsed = DateTime.Now - started;
            if (elapsed.TotalSeconds >= 2)
            {
                TextRenderer.DrawText(g, $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}", Theme.Small,
                    new Rectangle(textLeft, card.Bottom - 26, textWidth, 16), Theme.Subtle, TextFormatFlags.NoPadding | TextFormatFlags.Right);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Keys do not reach the controls hidden behind the layer
            e.Handled = true;
        }

        protected override bool IsInputKey(Keys keyData) => true;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                backdrop?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
