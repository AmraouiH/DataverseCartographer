using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DataverseCartographer.Models;

namespace DataverseCartographer.UI.Rendering
{
    /// <summary>What is highlighted when the graph is drawn (nothing for the exports).</summary>
    internal sealed class RenderState
    {
        public GraphNode SelectedNode;
        public GraphEdge SelectedEdge;
        public GraphNode HoverNode;
        public GraphEdge HoverEdge;
        public HashSet<GraphNode> SearchHits;
        public bool ShowLabels;

        /// <summary>Zoom factor, used to drop details when zoomed out.</summary>
        public float Scale = 1f;

        /// <summary>Visible area in graph coordinates (null = draw everything).</summary>
        public RectangleF? Viewport;
    }

    /// <summary>Cubic Bézier of an edge, from the source border to the target border.</summary>
    internal struct EdgePath
    {
        public PointF Start, C1, C2, End;

        public PointF PointAt(float t)
        {
            var u = 1 - t;
            return new PointF(
                u * u * u * Start.X + 3 * u * u * t * C1.X + 3 * u * t * t * C2.X + t * t * t * End.X,
                u * u * u * Start.Y + 3 * u * u * t * C1.Y + 3 * u * t * t * C2.Y + t * t * t * End.Y);
        }

        public RectangleF Bounds => RectangleF.FromLTRB(
            Math.Min(Math.Min(Start.X, End.X), Math.Min(C1.X, C2.X)), Math.Min(Math.Min(Start.Y, End.Y), Math.Min(C1.Y, C2.Y)),
            Math.Max(Math.Max(Start.X, End.X), Math.Max(C1.X, C2.X)), Math.Max(Math.Max(Start.Y, End.Y), Math.Max(C1.Y, C2.Y)));
    }

    /// <summary>
    /// Draws the graph on any <see cref="IGraphSurface"/>: tables as cards, relationships with crow's-foot ends
    /// (bar = one, three prongs = many), N:N dashed, cascade-delete relationships in red.
    /// </summary>
    internal static class GraphRenderer
    {
        public static readonly Color CascadeDeleteEdge = Color.FromArgb(220, 98, 98);
        public static readonly Color PluginNode = Color.FromArgb(109, 40, 217);
        public static readonly Color EndpointNode = Color.FromArgb(13, 148, 136);
        public static readonly Color MessageNode = Color.FromArgb(190, 24, 93);
        public static readonly Color ScriptNode = Color.FromArgb(202, 138, 4);
        public static readonly Color SyncTrigger = Color.FromArgb(8, 132, 168);
        public static readonly Color AsyncTrigger = Color.FromArgb(217, 119, 6);
        public static readonly Color WriteAction = Color.FromArgb(220, 38, 38);
        public static readonly Color ReadAction = Color.FromArgb(148, 163, 184);
        private const float LowDetailScale = 0.32f;
        private const int FadedAlpha = 45;

        public static void Render(IGraphSurface surface, Graph graph, RenderState state)
        {
            var active = state.HoverNode ?? state.SelectedNode;
            HashSet<GraphNode> related = null;
            if (active != null)
            {
                related = new HashSet<GraphNode> { active };
                foreach (var edge in graph.Edges)
                {
                    if (edge.Source == active) related.Add(edge.Target);
                    else if (edge.Target == active) related.Add(edge.Source);
                }
            }

            var lowDetail = state.Scale < LowDetailScale;
            var emphasized = new List<GraphEdge>();
            foreach (var edge in graph.Edges)
            {
                var highlighted = edge == state.SelectedEdge || edge == state.HoverEdge || (active != null && (edge.Source == active || edge.Target == active));
                if (highlighted)
                {
                    emphasized.Add(edge);
                    continue;
                }
                DrawEdge(surface, edge, state, active != null || state.SelectedEdge != null ? EdgeLook.Faded : EdgeLook.Normal, lowDetail);
            }
            foreach (var edge in emphasized) DrawEdge(surface, edge, state, EdgeLook.Highlighted, lowDetail);

            foreach (var node in graph.Nodes)
            {
                if (state.Viewport.HasValue && !state.Viewport.Value.IntersectsWith(RectangleF.Inflate(node.Bounds, 8, 8))) continue;
                var faded = related != null && !related.Contains(node)
                            || state.SelectedEdge != null && active == null && node != state.SelectedEdge.Source && node != state.SelectedEdge.Target;
                DrawNode(surface, node, state, faded, lowDetail);
            }

            if (lowDetail) return;
            foreach (var edge in emphasized) DrawEdgeLabel(surface, edge, state);
            if (state.ShowLabels)
            {
                foreach (var edge in graph.Edges.Except(emphasized))
                {
                    if (active == null && state.SelectedEdge == null) DrawEdgeLabel(surface, edge, state);
                }
            }
        }

        #region Nodes

        public static Color KindColor(GraphNode node)
        {
            if (node.IsFocus) return Theme.FocusTable;
            if (node.IsGhost) return Theme.GhostTable;
            switch (node.Kind)
            {
                case NodeKind.Plugin: return PluginNode;
                case NodeKind.Endpoint: return EndpointNode;
                case NodeKind.Message: return MessageNode;
                case NodeKind.Script: return ScriptNode;
                default: return node.Table.IsCustom ? Theme.CustomTable : Theme.StandardTable;
            }
        }

        private static void DrawNode(IGraphSurface s, GraphNode node, RenderState state, bool faded, bool lowDetail)
        {
            var bounds = node.Bounds;
            var kind = KindColor(node);
            Color A(Color c) => faded ? Color.FromArgb(Math.Min((int)c.A, (int)(FadedAlpha * 1.6)), c) : c;

            if (state.SearchHits != null && state.SearchHits.Contains(node))
            {
                s.FillRoundRect(RectangleF.Inflate(bounds, 6, 6), GraphStyle.NodeRadius + 5, Color.FromArgb(faded ? 60 : 110, Theme.SearchHit));
            }
            if (node == state.SelectedNode)
            {
                s.FillRoundRect(RectangleF.Inflate(bounds, 5, 5), GraphStyle.NodeRadius + 4, Color.FromArgb(170, Theme.Selection));
            }

            if (!lowDetail && !faded)
            {
                s.FillRoundRect(new RectangleF(bounds.X, bounds.Y + 2, bounds.Width, bounds.Height), GraphStyle.NodeRadius, Color.FromArgb(22, 15, 23, 42));
            }
            s.FillRoundRect(bounds, GraphStyle.NodeRadius, A(node.IsGhost ? Color.FromArgb(246, 248, 251) : Color.White));

            var borderWidth = node.IsFocus ? 2.4f : node == state.HoverNode ? 1.8f : 1.1f;
            var border = node.IsFocus || node == state.HoverNode ? kind : Theme.Blend(kind, Color.White, node.IsGhost ? 0.1f : 0.55f);
            s.StrokeRoundRect(bounds, GraphStyle.NodeRadius, A(border), borderWidth, node.IsGhost);

            // Kind badge: left accent dot (custom / standard / focus / outside the filters)
            var badge = new PointF(bounds.X + 15, bounds.Y + bounds.Height / 2);
            if (node.Kind == NodeKind.Table)
            {
                s.FillCircle(badge, 6f, A(kind));
                if (node.Table.IsCustom && !node.IsGhost) s.FillCircle(badge, 2.2f, A(Color.White));
            }
            else
            {
                // Code (plugin, endpoint, message): a small rounded square with a white core
                s.FillRoundRect(new RectangleF(badge.X - 6.5f, badge.Y - 6.5f, 13, 13), 3.5f, A(kind));
                s.FillRoundRect(new RectangleF(badge.X - 2.5f, badge.Y - 2.5f, 5, 5), 1.2f, A(Color.White));
            }

            if (lowDetail) return;
            var textColor = node.IsGhost ? Theme.Muted : Theme.Text;
            s.Text(node.Title, GraphStyle.TitleFont, A(textColor), new RectangleF(bounds.X + 30, bounds.Y + 6, bounds.Width - 40, 20), false);
            s.Text(node.Caption, GraphStyle.CaptionFont, A(node.IsGhost ? Theme.Subtle : Theme.Muted), new RectangleF(bounds.X + 30, bounds.Y + 25, bounds.Width - 40, 18), false);
        }

        #endregion

        #region Edges

        private enum EdgeLook
        {
            Normal,
            Faded,
            Highlighted
        }

        public static Color EdgeColor(GraphEdge edge)
        {
            switch (edge.Kind)
            {
                case EdgeKind.Trigger:
                    return edge.Muted ? Theme.GhostTable : edge.Async ? AsyncTrigger : SyncTrigger;
                case EdgeKind.Action:
                    return edge.Muted ? ReadAction : WriteAction;
            }
            if (edge.Relation.Kind == RelationKind.ManyToMany) return Theme.EdgeManyToMany;
            return edge.Relation.Cascade?.CascadesDelete == true ? CascadeDeleteEdge : Theme.Edge;
        }

        private static void DrawEdge(IGraphSurface s, GraphEdge edge, RenderState state, EdgeLook look, bool lowDetail)
        {
            var path = GetPath(edge);
            if (state.Viewport.HasValue && !state.Viewport.Value.IntersectsWith(RectangleF.Inflate(path.Bounds, 20, 20))) return;

            var color = EdgeColor(edge);
            var width = 1.3f;
            if (look == EdgeLook.Highlighted)
            {
                if (edge.Kind == EdgeKind.Relationship && edge.Relation.Kind == RelationKind.OneToMany && edge.Relation.Cascade?.CascadesDelete != true) color = Theme.EdgeHighlight;
                width = edge == state.SelectedEdge ? 3.2f : 2.2f;
            }
            else if (look == EdgeLook.Faded)
            {
                color = Color.FromArgb(FadedAlpha, color);
            }
            else if (edge.Source.IsGhost || edge.Target.IsGhost)
            {
                color = Color.FromArgb(120, color);
            }

            if (edge == state.SelectedEdge)
            {
                s.Bezier(path.Start, path.C1, path.C2, path.End, Color.FromArgb(150, Theme.Selection), width + 5, false);
            }

            var dashed = edge.Kind == EdgeKind.Relationship ? edge.Relation.Kind == RelationKind.ManyToMany : edge.Muted;
            s.Bezier(path.Start, path.C1, path.C2, path.End, color, width, dashed);
            if (lowDetail && look != EdgeLook.Highlighted) return;

            var markerWidth = Math.Max(1.3f, width * 0.85f);
            var startDirection = Direction(path.Start, path.C1, path.End);
            var endDirection = Direction(path.End, path.C2, path.Start);
            if (edge.Kind != EdgeKind.Relationship)
            {
                ArrowHead(s, path.End, endDirection, color, markerWidth);
                return;
            }
            if (edge.Relation.Kind == RelationKind.ManyToMany)
            {
                CrowFoot(s, path.Start, startDirection, color, markerWidth);
            }
            else
            {
                OneBar(s, path.Start, startDirection, color, markerWidth);
            }
            CrowFoot(s, path.End, endDirection, color, markerWidth);
        }

        private static void DrawEdgeLabel(IGraphSurface s, GraphEdge edge, RenderState state)
        {
            var text = EdgeLabel(edge);
            if (string.IsNullOrEmpty(text)) return;
            var path = GetPath(edge);
            var point = path.PointAt(0.5f);
            if (state.Viewport.HasValue && !state.Viewport.Value.Contains(point)) return;

            var size = GraphStyle.Measure(text, GraphStyle.LabelFont);
            var box = new RectangleF(point.X - size.Width / 2 - 5, point.Y - 8, size.Width + 10, 16);
            s.FillRoundRect(box, 8, Color.FromArgb(235, 255, 255, 255));
            s.StrokeRoundRect(box, 8, Color.FromArgb(120, EdgeColor(edge)), 0.8f, false);
            s.Text(text, GraphStyle.LabelFont, Theme.Text, box, true);
        }

        public static string EdgeLabel(GraphEdge edge) =>
            edge.Kind != EdgeKind.Relationship ? edge.Label :
            edge.Relation.Kind == RelationKind.ManyToMany ? edge.Relation.Intersect ?? edge.Relation.SchemaName : edge.Relation.ToAttribute;

        private static PointF Direction(PointF from, PointF control, PointF fallback)
        {
            var dx = control.X - from.X;
            var dy = control.Y - from.Y;
            var length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.5f)
            {
                dx = fallback.X - from.X;
                dy = fallback.Y - from.Y;
                length = (float)Math.Sqrt(dx * dx + dy * dy);
            }
            return length < 0.001f ? new PointF(1, 0) : new PointF(dx / length, dy / length);
        }

        /// <summary>« Many » end: three prongs opening onto the table border.</summary>
        private static void CrowFoot(IGraphSurface s, PointF tip, PointF u, Color color, float width)
        {
            var n = new PointF(-u.Y, u.X);
            var root = new PointF(tip.X + u.X * 12, tip.Y + u.Y * 12);
            s.Line(root, new PointF(tip.X + n.X * 6.5f, tip.Y + n.Y * 6.5f), color, width);
            s.Line(root, tip, color, width);
            s.Line(root, new PointF(tip.X - n.X * 6.5f, tip.Y - n.Y * 6.5f), color, width);
        }

        /// <summary>Direction end of a trigger / action: an open arrow head.</summary>
        private static void ArrowHead(IGraphSurface s, PointF tip, PointF u, Color color, float width)
        {
            var n = new PointF(-u.Y, u.X);
            var back = new PointF(tip.X + u.X * 11, tip.Y + u.Y * 11);
            s.Line(tip, new PointF(back.X + n.X * 6, back.Y + n.Y * 6), color, width + 0.4f);
            s.Line(tip, new PointF(back.X - n.X * 6, back.Y - n.Y * 6), color, width + 0.4f);
        }

        /// <summary>« One » end: a bar across the line.</summary>
        private static void OneBar(IGraphSurface s, PointF tip, PointF u, Color color, float width)
        {
            var n = new PointF(-u.Y, u.X);
            var center = new PointF(tip.X + u.X * 9, tip.Y + u.Y * 9);
            s.Line(new PointF(center.X + n.X * 6, center.Y + n.Y * 6), new PointF(center.X - n.X * 6, center.Y - n.Y * 6), color, width);
        }

        public static EdgePath GetPath(GraphEdge edge)
        {
            var source = edge.Source.Bounds;
            var target = edge.Target.Bounds;

            if (edge.IsSelf)
            {
                var grow = 16 * edge.ParallelIndex;
                var start = new PointF(source.Right - 34 - grow * 0.5f, source.Top);
                var end = new PointF(source.Right, source.Top + Math.Min(18, source.Height / 2));
                return new EdgePath
                {
                    Start = start,
                    C1 = new PointF(start.X, source.Top - 40 - grow),
                    C2 = new PointF(source.Right + 46 + grow, end.Y),
                    End = end
                };
            }

            var cs = edge.Source.Center;
            var ct = edge.Target.Center;
            var dx = ct.X - cs.X;
            var dy = ct.Y - cs.Y;
            var length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.01f) length = 0.01f;

            // Parallel edges fan out on both sides; the perpendicular is oriented by name so both directions agree.
            var sign = string.CompareOrdinal(edge.Source.Key, edge.Target.Key) <= 0 ? 1 : -1;
            var nx = -dy / length * sign;
            var ny = dx / length * sign;
            var offset = (edge.ParallelIndex - (edge.ParallelCount - 1) / 2f) * 34f + edge.Bend;
            var control = new PointF((cs.X + ct.X) / 2 + nx * offset, (cs.Y + ct.Y) / 2 + ny * offset);

            var p0 = Border(source, offset == 0 ? ct : control);
            var p3 = Border(target, offset == 0 ? cs : control);
            if (offset == 0)
            {
                return new EdgePath
                {
                    Start = p0,
                    C1 = Lerp(p0, p3, 1 / 3f),
                    C2 = Lerp(p0, p3, 2 / 3f),
                    End = p3
                };
            }
            return new EdgePath
            {
                Start = p0,
                C1 = Lerp(p0, control, 2 / 3f),
                C2 = Lerp(p3, control, 2 / 3f),
                End = p3
            };
        }

        private static PointF Lerp(PointF a, PointF b, float t) => new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        /// <summary>Point where the line from the box center toward <paramref name="toward"/> leaves the box.</summary>
        private static PointF Border(RectangleF box, PointF toward)
        {
            var cx = box.X + box.Width / 2;
            var cy = box.Y + box.Height / 2;
            var dx = toward.X - cx;
            var dy = toward.Y - cy;
            if (Math.Abs(dx) < 0.001f && Math.Abs(dy) < 0.001f) return new PointF(cx, box.Y);
            var tx = Math.Abs(dx) < 0.001f ? float.MaxValue : (box.Width / 2 + 1) / Math.Abs(dx);
            var ty = Math.Abs(dy) < 0.001f ? float.MaxValue : (box.Height / 2 + 1) / Math.Abs(dy);
            var t = Math.Min(tx, ty);
            return new PointF(cx + dx * t, cy + dy * t);
        }

        /// <summary>Distance (graph units) between a point and the edge curve.</summary>
        public static float Distance(GraphEdge edge, PointF point)
        {
            var path = GetPath(edge);
            var best = float.MaxValue;
            var previous = path.Start;
            for (var i = 1; i <= 24; i++)
            {
                var current = path.PointAt(i / 24f);
                best = Math.Min(best, SegmentDistance(point, previous, current));
                previous = current;
            }
            return best;
        }

        private static float SegmentDistance(PointF p, PointF a, PointF b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared < 0.0001f ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared));
            var x = a.X + t * dx - p.X;
            var y = a.Y + t * dy - p.Y;
            return (float)Math.Sqrt(x * x + y * y);
        }

        #endregion

        #region Export

        /// <summary>Area covered by the graph, including self-reference loops and a margin for the title.</summary>
        public static RectangleF ExportBounds(Graph graph, bool withTitle)
        {
            var bounds = graph.GetBounds();
            foreach (var edge in graph.Edges.Where(e => e.IsSelf)) bounds = RectangleF.Union(bounds, GetPath(edge).Bounds);
            bounds.Inflate(40, 40);
            if (withTitle)
            {
                bounds.Y -= 56;
                bounds.Height += 56;
            }
            return bounds;
        }

        /// <summary>Exported picture: title line, graph, nothing highlighted.</summary>
        public static void RenderExport(IGraphSurface surface, Graph graph, RectangleF bounds, string title, string subtitle)
        {
            if (!string.IsNullOrEmpty(title))
            {
                surface.Text(title, GraphStyle.HeadingFont, Theme.Text, new RectangleF(bounds.X + 40, bounds.Y + 22, bounds.Width - 80, 26), false);
                surface.Text(subtitle, GraphStyle.CaptionFont, Theme.Muted, new RectangleF(bounds.X + 40, bounds.Y + 48, bounds.Width - 80, 18), false);
            }
            Render(surface, graph, new RenderState { ShowLabels = graph.Kind != GraphKind.DataModel });
        }

        #endregion
    }
}
