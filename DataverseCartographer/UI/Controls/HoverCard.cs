using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DataverseCartographer.UI.Controls
{
    /// <summary>What a hover card shows: a title, a subtitle, an optional badge, then key / value rows grouped in sections.</summary>
    public sealed class HoverContent
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Badge { get; set; }
        public Color Accent { get; set; } = Theme.Accent;
        public List<HoverRow> Rows { get; } = new List<HoverRow>();

        public HoverContent Section(string title)
        {
            Rows.Add(new HoverRow { Key = title, IsSection = true });
            return this;
        }

        public HoverContent Row(string key, string value, Color? color = null)
        {
            if (!string.IsNullOrEmpty(value)) Rows.Add(new HoverRow { Key = key, Value = value, Color = color });
            return this;
        }
    }

    public sealed class HoverRow
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public Color? Color { get; set; }
        public bool IsSection { get; set; }
    }

    /// <summary>
    /// Floating card shown when hovering the map: rounded, shadowed, never takes the focus.
    /// It stays open while the mouse is on it, and long contents scroll (mouse wheel or the slim scrollbar).
    /// </summary>
    [DesignerCategory("Code")]
    public sealed class HoverCard : Form
    {
        private const int CardWidth = 400;
        private const int MaxBodyHeight = 330;
        private const int HeaderHeight = 58;
        private const int Pad = 16;
        private const int KeyWidth = 128;
        private const int RowGap = 7;
        private const int ScrollWidth = 6;

        private HoverContent _content;
        private readonly List<(HoverRow Row, int Y, int Height)> _layout = new List<(HoverRow, int, int)>();
        private int _contentHeight;
        private int _scroll;
        private bool _draggingThumb;
        private int _dragStartY, _dragStartScroll;

        public HoverCard()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Card;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.Base;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int csDropShadow = 0x00020000;
                const int wsExNoActivate = 0x08000000;
                const int wsExToolWindow = 0x00000080;
                var cp = base.CreateParams;
                cp.ClassStyle |= csDropShadow;
                cp.ExStyle |= wsExNoActivate | wsExToolWindow;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                // Windows 11: rounded window corners
                var preference = 2;
                DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public object Target { get; private set; }

        /// <summary>Shows the card for a target near a screen point, kept inside the working area of the screen.</summary>
        public void ShowFor(object target, HoverContent content, Point screen, IWin32Window owner)
        {
            Target = target;
            _content = content;
            _scroll = 0;
            Measure();

            var height = HeaderHeight + Math.Min(_contentHeight, MaxBodyHeight) + Pad;
            var area = Screen.FromPoint(screen).WorkingArea;
            var x = screen.X + 18;
            var y = screen.Y + 20;
            if (x + CardWidth > area.Right) x = screen.X - CardWidth - 12;
            if (y + height > area.Bottom) y = Math.Max(area.Top, area.Bottom - height - 8);
            Bounds = new Rectangle(Math.Max(area.Left, x), y, CardWidth, height);

            if (!Visible)
            {
                if (owner is Control control && control.FindForm() is Form form && Owner != form) Owner = form;
                Show();
            }
            Invalidate();
        }

        public void HideCard()
        {
            Target = null;
            if (Visible) Hide();
        }

        private bool Scrollable => _contentHeight > MaxBodyHeight;

        private int BodyHeight => Math.Min(_contentHeight, MaxBodyHeight);

        private int TextWidth => CardWidth - Pad * 2 - KeyWidth - (Scrollable ? ScrollWidth + 6 : 0);

        private void Measure()
        {
            _layout.Clear();
            var y = 0;
            using (var g = CreateGraphics())
            {
                // First pass without scrollbar, second pass with it when needed
                for (var pass = 0; pass < 2; pass++)
                {
                    _layout.Clear();
                    y = 0;
                    var width = CardWidth - Pad * 2 - KeyWidth - (pass == 1 ? ScrollWidth + 6 : 0);
                    foreach (var row in _content.Rows)
                    {
                        int height;
                        if (row.IsSection)
                        {
                            if (y > 0) y += 6;
                            height = 22;
                        }
                        else
                        {
                            var value = TextRenderer.MeasureText(g, row.Value ?? string.Empty, Theme.Base, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                            var key = TextRenderer.MeasureText(g, row.Key ?? string.Empty, Theme.Small, new Size(KeyWidth - 8, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                            height = Math.Max(value.Height, key.Height) + RowGap;
                        }
                        _layout.Add((row, y, height));
                        y += height;
                    }
                    if (y <= MaxBodyHeight) break;
                }
            }
            _contentHeight = y;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            if (_content == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var border = new Pen(Theme.Border)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            // Header: accent bar, title, subtitle, badge
            using (var accent = new SolidBrush(_content.Accent)) g.FillRectangle(accent, 0, 0, Width, 3);
            var badgeWidth = 0;
            if (!string.IsNullOrEmpty(_content.Badge))
            {
                badgeWidth = TextRenderer.MeasureText(g, _content.Badge, Theme.SmallBold, Size.Empty, TextFormatFlags.NoPadding).Width + 16;
                var badge = new RectangleF(Width - Pad - badgeWidth, 14, badgeWidth, 20);
                using (var path = Theme.RoundedRect(badge, 10))
                using (var fill = new SolidBrush(Theme.Blend(Color.White, _content.Accent, 0.14f)))
                {
                    g.FillPath(fill, path);
                }
                TextRenderer.DrawText(g, _content.Badge, Theme.SmallBold, Rectangle.Round(badge), _content.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                badgeWidth += 8;
            }
            TextRenderer.DrawText(g, _content.Title, Theme.CardTitle, new Rectangle(Pad, 12, Width - Pad * 2 - badgeWidth, 22), Theme.Text,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, _content.Subtitle, Theme.Small, new Rectangle(Pad, 35, Width - Pad * 2, 16), Theme.Muted,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using (var divider = new Pen(Theme.Divider)) g.DrawLine(divider, Pad, HeaderHeight - 4, Width - Pad, HeaderHeight - 4);

            // Body (clipped, scrolled)
            var body = new Rectangle(0, HeaderHeight, Width, BodyHeight);
            var state = g.Save();
            g.SetClip(body);
            foreach (var (row, y, height) in _layout)
            {
                var top = HeaderHeight + y - _scroll;
                if (top + height < HeaderHeight || top > body.Bottom) continue;
                if (row.IsSection)
                {
                    TextRenderer.DrawText(g, row.Key.ToUpperInvariant(), Theme.SmallBold, new Rectangle(Pad, top + 4, Width - Pad * 2, 16), _content.Accent,
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    continue;
                }
                TextRenderer.DrawText(g, row.Key, Theme.Small, new Rectangle(Pad, top + 1, KeyWidth - 8, height), Theme.Muted,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, row.Value, Theme.Base, new Rectangle(Pad + KeyWidth, top, TextWidth, height), row.Color ?? Theme.Text,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }
            g.Restore(state);

            if (Scrollable)
            {
                var track = TrackBounds;
                using (var path = Theme.RoundedRect(track, ScrollWidth / 2f))
                using (var fill = new SolidBrush(Theme.Divider))
                {
                    g.FillPath(fill, path);
                }
                using (var path = Theme.RoundedRect(ThumbBounds, ScrollWidth / 2f))
                using (var fill = new SolidBrush(_draggingThumb ? Theme.Muted : Theme.Subtle))
                {
                    g.FillPath(fill, path);
                }

                // Fade at the bottom while more rows are hidden
                if (_scroll < _contentHeight - BodyHeight)
                {
                    var fade = new Rectangle(1, body.Bottom - 22, Width - ScrollWidth - 14, 22);
                    using (var brush = new LinearGradientBrush(fade, Color.FromArgb(0, Theme.Card), Theme.Card, LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(brush, fade);
                    }
                }
            }
        }

        private RectangleF TrackBounds => new RectangleF(Width - Pad / 2f - ScrollWidth, HeaderHeight + 2, ScrollWidth, BodyHeight - 4);

        private RectangleF ThumbBounds
        {
            get
            {
                var track = TrackBounds;
                var height = Math.Max(24, track.Height * BodyHeight / _contentHeight);
                var range = Math.Max(1, _contentHeight - BodyHeight);
                var top = track.Top + (track.Height - height) * _scroll / range;
                return new RectangleF(track.X, top, ScrollWidth, height);
            }
        }

        private void ScrollTo(int value)
        {
            var max = Math.Max(0, _contentHeight - BodyHeight);
            value = Math.Max(0, Math.Min(max, value));
            if (value == _scroll) return;
            _scroll = value;
            Invalidate();
        }

        /// <summary>Wheel: called by the map too, so that the card scrolls while the mouse is still on the hovered element.</summary>
        public bool ScrollBy(int delta)
        {
            if (!Visible || !Scrollable) return false;
            ScrollTo(_scroll - delta / 120 * 48);
            return true;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(e.Delta);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Scrollable) return;
            var thumb = ThumbBounds;
            if (thumb.Contains(e.Location))
            {
                _draggingThumb = true;
                _dragStartY = e.Y;
                _dragStartScroll = _scroll;
                Capture = true;
                Invalidate();
            }
            else if (TrackBounds.Contains(e.Location))
            {
                ScrollTo(_scroll + (e.Y < thumb.Top ? -BodyHeight : BodyHeight));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_draggingThumb) return;
            var track = TrackBounds;
            var thumb = ThumbBounds;
            var range = Math.Max(1f, track.Height - thumb.Height);
            ScrollTo(_dragStartScroll + (int)((e.Y - _dragStartY) * (_contentHeight - BodyHeight) / range));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_draggingThumb) return;
            _draggingThumb = false;
            Capture = false;
            Invalidate();
        }
    }
}
