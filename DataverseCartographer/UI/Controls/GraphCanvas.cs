using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using DataverseCartographer.Models;
using DataverseCartographer.UI.Rendering;

namespace DataverseCartographer.UI.Controls
{
    /// <summary>
    /// Interactive graph view: wheel to zoom (around the cursor), drag the background to pan, drag a table to move it,
    /// click to select, double-click to explore around a table, right-click for the table menu.
    /// Hovering a table highlights its relationships and fades the rest.
    /// </summary>
    [DesignerCategory("Code")]
    public class GraphCanvas : Control
    {
        private const float MinScale = 0.04f;
        private const float MaxScale = 3f;
        private const int DragThreshold = 4;

        private Graph _graph;
        private float _scale = 1f;
        private PointF _offset;

        private Point _mouseDown;
        private bool _mouseIsDown;
        private bool _dragging;
        private GraphNode _dragNode;
        private PointF _dragNodeStart;
        private PointF _panStart;

        private readonly RenderState _state = new RenderState();
        private readonly HoverCard _card = new HoverCard();
        private readonly Timer _hoverDelay = new Timer { Interval = 380 };
        private readonly Timer _hoverWatch = new Timer { Interval = 120 };
        private object _hoverTarget;

        public event EventHandler SelectionChanged;
        public event EventHandler<GraphNode> NodeDoubleClicked;
        public event EventHandler<NodeMenuEventArgs> NodeMenuRequested;

        public enum LegendStyle
        {
            Dot,
            Square,
            Line,
            Dashed
        }

        public sealed class LegendItem
        {
            public LegendItem(string text, Color color, LegendStyle style)
            {
                Text = text;
                Color = color;
                Style = style;
            }

            public string Text { get; }
            public Color Color { get; }
            public LegendStyle Style { get; }
        }

        public sealed class NodeMenuEventArgs : EventArgs
        {
            public GraphNode Node { get; set; }
            public Point Location { get; set; }
        }

        public GraphCanvas()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.GraphCanvas;

            // The card opens after a short pause on an element, then follows the hovered element; it stays open while
            // the mouse is on it (to scroll a long content) and closes as soon as the mouse is elsewhere.
            _hoverDelay.Tick += (s, e) =>
            {
                _hoverDelay.Stop();
                ShowCard();
            };
            _hoverWatch.Tick += (s, e) =>
            {
                if (!_card.Visible)
                {
                    _hoverWatch.Stop();
                    return;
                }
                if (_card.Bounds.Contains(Cursor.Position) || _card.Target != null && _card.Target == _hoverTarget) return;
                HideToolTip();
            };
        }

        /// <summary>Contents of the hover card of a node or an edge (null = no card).</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Func<object, HoverContent> HoverContentProvider { get; set; } = HoverContentBuilder.Build;

        private void ShowCard()
        {
            if (_hoverTarget == null || !IsHandleCreated || !Visible) return;
            HoverContent content;
            try
            {
                content = HoverContentProvider?.Invoke(_hoverTarget);
            }
            catch (Exception)
            {
                content = null;
            }
            if (content == null) return;
            _card.ShowFor(_hoverTarget, content, Cursor.Position, this);
            _hoverWatch.Start();
        }

        #region Properties

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Graph Graph
        {
            get => _graph;
            set
            {
                _graph = value;
                _state.SelectedNode = null;
                _state.SelectedEdge = null;
                _state.HoverNode = null;
                _state.HoverEdge = null;
                _state.SearchHits = null;
                Invalidate();
            }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public GraphNode SelectedNode => _state.SelectedNode;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public GraphEdge SelectedEdge => _state.SelectedEdge;

        [DefaultValue(false)]
        public bool ShowLabels
        {
            get => _state.ShowLabels;
            set { _state.ShowLabels = value; Invalidate(); }
        }

        /// <summary>Message shown in the middle when there is nothing to draw.</summary>
        [DefaultValue(null)]
        public string EmptyText { get; set; }

        public float ZoomLevel => _scale;

        /// <summary>Items of the legend drawn at the bottom left.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public LegendItem[] Legend { get; set; } = new LegendItem[0];

        #endregion

        #region Public API

        public void SelectNode(GraphNode node)
        {
            _state.SelectedNode = node;
            _state.SelectedEdge = null;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SelectEdge(GraphEdge edge)
        {
            _state.SelectedEdge = edge;
            _state.SelectedNode = null;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetSearchHits(IEnumerable<GraphNode> nodes)
        {
            var hits = nodes?.ToList();
            _state.SearchHits = hits == null || hits.Count == 0 ? null : new HashSet<GraphNode>(hits);
            Invalidate();
        }

        public void FitToView()
        {
            if (_graph == null || _graph.Nodes.Count == 0 || Width < 10 || Height < 10)
            {
                _scale = 1;
                _offset = new PointF(Width / 2f, Height / 2f);
                Invalidate();
                return;
            }
            // The legend takes the bottom strip of the view.
            const float legend = 44;
            var bounds = GraphRenderer.ExportBounds(_graph, false);
            var height = Math.Max(10, Height - legend);
            _scale = Clamp(Math.Min(Width / bounds.Width, height / bounds.Height));
            _scale = Math.Min(_scale, 1.25f);
            _offset = new PointF(Width / 2f - (bounds.X + bounds.Width / 2) * _scale, height / 2f - (bounds.Y + bounds.Height / 2) * _scale);
            Invalidate();
        }

        public void ZoomBy(float factor) => ZoomAt(new PointF(Width / 2f, Height / 2f), factor);

        /// <summary>Centers the view on a table, zooming in if the graph is too small to read.</summary>
        public void CenterOn(GraphNode node)
        {
            if (node == null) return;
            if (_scale < 0.7f) _scale = 0.9f;
            var center = node.Center;
            _offset = new PointF(Width / 2f - center.X * _scale, Height / 2f - center.Y * _scale);
            Invalidate();
        }

        #endregion

        #region Coordinates

        private PointF ToWorld(Point screen) => new PointF((screen.X - _offset.X) / _scale, (screen.Y - _offset.Y) / _scale);

        private static float Clamp(float scale) => Math.Max(MinScale, Math.Min(MaxScale, scale));

        private void ZoomAt(PointF screen, float factor)
        {
            var world = new PointF((screen.X - _offset.X) / _scale, (screen.Y - _offset.Y) / _scale);
            _scale = Clamp(_scale * factor);
            _offset = new PointF(screen.X - world.X * _scale, screen.Y - world.Y * _scale);
            Invalidate();
        }

        private GraphNode NodeAt(PointF world)
        {
            if (_graph == null) return null;
            for (var i = _graph.Nodes.Count - 1; i >= 0; i--)
            {
                if (_graph.Nodes[i].Bounds.Contains(world)) return _graph.Nodes[i];
            }
            return null;
        }

        private GraphEdge EdgeAt(PointF world)
        {
            if (_graph == null) return null;
            var tolerance = Math.Max(4f, 6f / _scale);
            GraphEdge best = null;
            var bestDistance = tolerance;
            foreach (var edge in _graph.Edges)
            {
                var distance = GraphRenderer.Distance(edge, world);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = edge;
                }
            }
            return best;
        }

        #endregion

        #region Painting

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            DrawDotGrid(g);

            if (_graph == null || _graph.Nodes.Count == 0)
            {
                if (!string.IsNullOrEmpty(EmptyText))
                {
                    TextRenderer.DrawText(g, EmptyText, Theme.CardTitle, ClientRectangle, Theme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                }
                return;
            }

            var state = g.Save();
            g.TranslateTransform(_offset.X, _offset.Y);
            g.ScaleTransform(_scale, _scale);
            _state.Scale = _scale;
            _state.Viewport = new RectangleF(-_offset.X / _scale, -_offset.Y / _scale, Width / _scale, Height / _scale);
            GraphRenderer.Render(new GdiSurface(g), _graph, _state);
            g.Restore(state);

            DrawLegend(g);
        }

        private void DrawDotGrid(Graphics g)
        {
            var spacing = 26f * _scale;
            while (spacing < 12) spacing *= 2;
            var startX = _offset.X % spacing;
            var startY = _offset.Y % spacing;
            using (var brush = new SolidBrush(Theme.GraphDot))
            {
                for (var x = startX; x < Width; x += spacing)
                {
                    for (var y = startY; y < Height; y += spacing) g.FillRectangle(brush, x, y, 1.6f, 1.6f);
                }
            }
        }

        private void DrawLegend(Graphics g)
        {
            var items = Legend ?? new LegendItem[0];
            if (items.Length == 0) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            var widths = items.Select(i => TextRenderer.MeasureText(g, i.Text, Theme.Small, Size.Empty, TextFormatFlags.NoPadding).Width + 34).ToArray();
            var width = widths.Sum() + 16;
            if (width > Width - 20) return;
            var box = new Rectangle(10, Height - 36, width, 26);
            using (var path = Theme.RoundedRect(box, 8))
            using (var fill = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            using (var border = new Pen(Theme.Border))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            var x = box.X + 10;
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                var text = item.Text;
                var cy = box.Y + box.Height / 2f;
                switch (item.Style)
                {
                    case LegendStyle.Dot:
                        using (var brush = new SolidBrush(item.Color)) g.FillEllipse(brush, x + 4, cy - 5, 10, 10);
                        break;
                    case LegendStyle.Square:
                        using (var brush = new SolidBrush(item.Color))
                        using (var path = Theme.RoundedRect(new RectangleF(x + 4, cy - 5, 10, 10), 2.5f))
                        {
                            g.FillPath(brush, path);
                        }
                        break;
                    default:
                        using (var pen = new Pen(item.Color, 1.8f))
                        {
                            if (item.Style == LegendStyle.Dashed) pen.DashPattern = new[] { 3f, 2f };
                            g.DrawLine(pen, x, cy, x + 18, cy);
                        }
                        break;
                }
                TextRenderer.DrawText(g, text, Theme.Small, new Point(x + 24, (int)cy - 7), Theme.Muted, TextFormatFlags.NoPadding);
                x += widths[i];
            }
        }

        #endregion

        #region Mouse

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            _mouseIsDown = true;
            _dragging = false;
            _mouseDown = e.Location;
            _dragNode = NodeAt(ToWorld(e.Location));
            if (_dragNode != null) _dragNodeStart = new PointF(_dragNode.X, _dragNode.Y);
            _panStart = _offset;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_mouseIsDown)
            {
                var dx = e.X - _mouseDown.X;
                var dy = e.Y - _mouseDown.Y;
                if (!_dragging && Math.Abs(dx) + Math.Abs(dy) >= DragThreshold)
                {
                    _dragging = true;
                    HideToolTip();
                }
                if (!_dragging) return;

                if (_dragNode != null)
                {
                    _dragNode.X = _dragNodeStart.X + dx / _scale;
                    _dragNode.Y = _dragNodeStart.Y + dy / _scale;
                    Cursor = Cursors.SizeAll;
                }
                else
                {
                    _offset = new PointF(_panStart.X + dx, _panStart.Y + dy);
                    Cursor = Cursors.Hand;
                }
                Invalidate();
                return;
            }

            UpdateHover(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var world = ToWorld(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                var node = NodeAt(world);
                if (node != null)
                {
                    if (node != _state.SelectedNode) SelectNode(node);
                    NodeMenuRequested?.Invoke(this, new NodeMenuEventArgs { Node = node, Location = e.Location });
                }
                return;
            }
            if (e.Button != MouseButtons.Left || !_mouseIsDown) return;

            _mouseIsDown = false;
            Cursor = Cursors.Default;
            if (_dragging)
            {
                if (_dragNode != null && _graph != null)
                {
                    Services.GraphLayout.RouteEdges(_graph);
                    Invalidate();
                }
                _dragging = false;
                _dragNode = null;
                return;
            }

            var clicked = NodeAt(world);
            if (clicked != null)
            {
                SelectNode(clicked);
                return;
            }
            var edge = EdgeAt(world);
            if (edge != null)
            {
                SelectEdge(edge);
                return;
            }
            if (_state.SelectedNode != null || _state.SelectedEdge != null) SelectNode(null);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left) return;
            var node = NodeAt(ToWorld(e.Location));
            if (node != null) NodeDoubleClicked?.Invoke(this, node);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            HideToolTip();
            ZoomAt(e.Location, e.Delta > 0 ? 1.15f : 1 / 1.15f);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_state.HoverNode != null || _state.HoverEdge != null)
            {
                _state.HoverNode = null;
                _state.HoverEdge = null;
                Invalidate();
            }
            _hoverTarget = null;
            _hoverDelay.Stop();
        }

        private void UpdateHover(Point location)
        {
            var world = ToWorld(location);
            var node = NodeAt(world);
            var edge = node == null ? EdgeAt(world) : null;
            if (node != _state.HoverNode || edge != _state.HoverEdge)
            {
                _state.HoverNode = node;
                _state.HoverEdge = edge;
                Cursor = node != null || edge != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            object target = (object)node ?? edge;
            if (target == _hoverTarget) return;
            _hoverTarget = target;
            _hoverDelay.Stop();
            if (target == null) return; // the watch timer closes the card unless the mouse goes onto it
            if (_card.Visible) ShowCard(); // already reading a card: switch at once
            else _hoverDelay.Start();
        }

        private void HideToolTip()
        {
            _hoverDelay.Stop();
            _hoverWatch.Stop();
            _card.HideCard();
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            const float step = 60;
            switch (e.KeyCode)
            {
                case Keys.Add:
                case Keys.Oemplus:
                    ZoomBy(1.2f);
                    break;
                case Keys.Subtract:
                case Keys.OemMinus:
                    ZoomBy(1 / 1.2f);
                    break;
                case Keys.Left:
                    _offset.X += step;
                    Invalidate();
                    break;
                case Keys.Right:
                    _offset.X -= step;
                    Invalidate();
                    break;
                case Keys.Up:
                    _offset.Y += step;
                    Invalidate();
                    break;
                case Keys.Down:
                    _offset.Y -= step;
                    Invalidate();
                    break;
                case Keys.Escape:
                    SelectNode(null);
                    break;
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hoverDelay.Dispose();
                _hoverWatch.Dispose();
                _card.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
