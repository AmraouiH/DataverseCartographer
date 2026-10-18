using System;
using System.Collections.Generic;
using System.Linq;
using DataverseCartographer.Models;

namespace DataverseCartographer.Services
{
    /// <summary>
    /// Automatic layouts. All of them are deterministic (same graph → same picture) and place nodes around the origin.
    /// </summary>
    public static class GraphLayout
    {
        private const float HorizontalGap = 36;
        private const float LayerGap = 110;

        public static void Apply(Graph graph, LayoutKind kind)
        {
            if (graph.Nodes.Count == 0) return;
            foreach (var edge in graph.Edges) edge.Bend = 0;
            switch (kind)
            {
                case LayoutKind.ForceDirected:
                    ForceDirected(graph);
                    break;
                case LayoutKind.Circular:
                    Circular(graph);
                    break;
                case LayoutKind.Grid:
                    Grid(graph.Nodes.OrderBy(n => n.Label, StringComparer.CurrentCultureIgnoreCase).ToList(), 0, 0);
                    break;
                default:
                    Hierarchical(graph);
                    break;
            }
            RouteEdges(graph);
        }

        /// <summary>
        /// Bends the straight edges that would cross another table: each one gets a sideways offset, on the side crossing fewer tables.
        /// Call again after tables were moved by hand.
        /// </summary>
        public static void RouteEdges(Graph graph)
        {
            if (graph.Edges.Count * (long)graph.Nodes.Count > 1_500_000) return; // huge maps: keep it instantaneous
            foreach (var edge in graph.Edges)
            {
                edge.Bend = 0;
                if (edge.IsSelf || edge.ParallelCount > 1) continue;
                if (Crossings(graph, edge) == 0) continue;
                var a = edge.Source.Center;
                var b = edge.Target.Center;
                var length = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                var best = 0f;
                var bestCount = int.MaxValue;
                // The curve only moves by half the offset at its middle: offsets must exceed the width of a table.
                var unit = Math.Max(110, Math.Min(260, length * 0.3f));
                foreach (var factor in new[] { 1.2f, -1.2f, 2f, -2f, 3f, -3f })
                {
                    edge.Bend = factor * unit;
                    var count = Crossings(graph, edge);
                    if (count < bestCount)
                    {
                        bestCount = count;
                        best = edge.Bend;
                    }
                    if (count == 0) break;
                }
                edge.Bend = best;
            }
        }

        private static int Crossings(Graph graph, GraphEdge edge)
        {
            var path = UI.Rendering.GraphRenderer.GetPath(edge);
            var area = path.Bounds;
            var points = new System.Drawing.PointF[17];
            for (var i = 0; i <= 16; i++) points[i] = path.PointAt(i / 16f);
            var count = 0;
            foreach (var node in graph.Nodes)
            {
                if (node == edge.Source || node == edge.Target) continue;
                var box = System.Drawing.RectangleF.Inflate(node.Bounds, 6, 6);
                if (!box.IntersectsWith(area)) continue;
                for (var i = 1; i < 16; i++)
                {
                    if (box.Contains(points[i])) { count++; break; }
                }
            }
            return count;
        }

        #region Neighbors

        /// <summary>Edges that order the layers: 1:N (one side above), triggers and actions (origin above).</summary>
        private static bool IsLayered(GraphEdge edge) => edge.Kind != EdgeKind.Relationship || edge.Relation.Kind == RelationKind.OneToMany;

        private static Dictionary<GraphNode, List<GraphNode>> Neighbors(Graph graph)
        {
            var result = graph.Nodes.ToDictionary(n => n, n => new List<GraphNode>());
            foreach (var edge in graph.Edges.Where(e => !e.IsSelf))
            {
                result[edge.Source].Add(edge.Target);
                result[edge.Target].Add(edge.Source);
            }
            return result;
        }

        #endregion

        #region Hierarchical

        /// <summary>
        /// Layered layout: the « one » side of each 1:N above the « many » side (cycles broken by a DFS),
        /// crossings reduced with barycenter sweeps, very wide layers wrapped on several rows.
        /// Tables without any relationship are gathered in a grid below.
        /// </summary>
        private static void Hierarchical(Graph graph)
        {
            var neighbors = Neighbors(graph);
            var children = graph.Nodes.ToDictionary(n => n, n => new List<GraphNode>());
            foreach (var edge in graph.Edges.Where(e => !e.IsSelf && IsLayered(e)))
            {
                if (!children[edge.Source].Contains(edge.Target)) children[edge.Source].Add(edge.Target);
            }

            var connected = graph.Nodes.Where(n => neighbors[n].Count > 0).ToList();
            var isolated = graph.Nodes.Where(n => neighbors[n].Count == 0).OrderBy(n => n.Label, StringComparer.CurrentCultureIgnoreCase).ToList();

            // 1. Break cycles: DFS from the hubs (focus first), edges to a node on the current path are ignored.
            // Technical maps: start from the tables that trigger code, so that the chains read from top to bottom.
            var triggers = graph.Edges.Where(e => e.Kind == EdgeKind.Trigger).GroupBy(e => e.Source).ToDictionary(g => g.Key, g => g.Count());
            var order = connected
                .OrderByDescending(n => n.IsFocus)
                .ThenBy(n => n.IsGhost)
                .ThenByDescending(n => triggers.TryGetValue(n, out var count) ? count : 0)
                .ThenByDescending(n => children[n].Count)
                .ThenByDescending(n => neighbors[n].Count)
                .ThenBy(n => n.Key, StringComparer.Ordinal)
                .ToList();
            var dag = connected.ToDictionary(n => n, n => new List<GraphNode>());
            var state = new Dictionary<GraphNode, int>(); // 1 = on path, 2 = done
            var topological = new List<GraphNode>();
            foreach (var root in order)
            {
                if (state.ContainsKey(root)) continue;
                var stack = new Stack<(GraphNode Node, int Index)>();
                stack.Push((root, 0));
                state[root] = 1;
                while (stack.Count > 0)
                {
                    var (node, index) = stack.Pop();
                    var list = children[node];
                    if (index < list.Count)
                    {
                        stack.Push((node, index + 1));
                        var child = list[index];
                        if (!state.TryGetValue(child, out var s))
                        {
                            dag[node].Add(child);
                            state[child] = 1;
                            stack.Push((child, 0));
                        }
                        else if (s == 2)
                        {
                            dag[node].Add(child);
                        }
                        // s == 1: back edge (cycle), ignored for the layering.
                    }
                    else
                    {
                        state[node] = 2;
                        topological.Add(node);
                    }
                }
            }
            topological.Reverse();

            // 2. Longest path layering.
            var layer = connected.ToDictionary(n => n, n => 0);
            foreach (var node in topological)
            {
                foreach (var child in dag[node]) layer[child] = Math.Max(layer[child], layer[node] + 1);
            }

            // Nodes only linked through N:N (no 1:N parent) sit next to the first neighbor's layer.
            foreach (var node in topological.Where(n => layer[n] == 0 && dag[n].Count == 0 && !graph.Edges.Any(e => e.Target == n && IsLayered(e) && !e.IsSelf)))
            {
                var anchor = neighbors[node].Where(m => m != node).Select(m => layer[m]).DefaultIfEmpty(0).Max();
                layer[node] = anchor;
            }

            // 3. Rows: wrap the layers wider than the limit.
            var maxPerRow = Math.Max(8, (int)Math.Ceiling(Math.Sqrt(connected.Count) * 1.6));
            var rows = new List<List<GraphNode>>();
            foreach (var group in connected.GroupBy(n => layer[n]).OrderBy(g => g.Key))
            {
                var members = group.ToList();
                for (var i = 0; i < members.Count; i += maxPerRow) rows.Add(members.Skip(i).Take(maxPerRow).ToList());
            }

            // 4. Crossing reduction: barycenter of the neighbors in the adjacent rows.
            var rowOf = new Dictionary<GraphNode, int>();
            for (var r = 0; r < rows.Count; r++) foreach (var node in rows[r]) rowOf[node] = r;
            var position = new Dictionary<GraphNode, float>();
            void Index(List<GraphNode> row) { for (var i = 0; i < row.Count; i++) position[row[i]] = i - row.Count / 2f; }
            foreach (var row in rows) Index(row);

            for (var sweep = 0; sweep < 6; sweep++)
            {
                var down = sweep % 2 == 0;
                for (var step = 1; step < rows.Count; step++)
                {
                    var r = down ? step : rows.Count - 1 - step;
                    var reference = down ? r - 1 : r + 1;
                    var row = rows[r];
                    var keys = row.ToDictionary(n => n, n =>
                    {
                        var adjacent = neighbors[n].Where(m => rowOf[m] == reference).ToList();
                        return adjacent.Count == 0 ? position[n] : adjacent.Average(m => position[m]);
                    });
                    row.Sort((a, b) =>
                    {
                        var c = keys[a].CompareTo(keys[b]);
                        return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
                    });
                    Index(row);
                }
            }

            // 5. Coordinates: each row centered on x = 0.
            float y = 0;
            foreach (var row in rows)
            {
                var width = row.Sum(n => n.Width) + HorizontalGap * (row.Count - 1);
                var x = -width / 2;
                var height = row.Max(n => n.Height);
                foreach (var node in row)
                {
                    node.X = x;
                    node.Y = y + (height - node.Height) / 2;
                    x += node.Width + HorizontalGap;
                }
                y += height + LayerGap;
            }

            if (isolated.Count > 0)
            {
                Grid(isolated, 0, rows.Count == 0 ? 0 : y + LayerGap / 2);
            }
        }

        #endregion

        #region Force-directed

        /// <summary>
        /// Fruchterman–Reingold spring embedder with a light gravity (keeps components together), then overlap removal.
        /// </summary>
        private static void ForceDirected(Graph graph)
        {
            var linked = new HashSet<GraphNode>(graph.Edges.Where(e => !e.IsSelf).SelectMany(e => new[] { e.Source, e.Target }));
            var nodes = graph.Nodes.Where(linked.Contains).ToList();
            var isolated = graph.Nodes.Where(n => !linked.Contains(n)).OrderBy(n => n.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (nodes.Count == 0)
            {
                Grid(isolated, 0, 0);
                return;
            }
            var n = nodes.Count;
            var index = new Dictionary<GraphNode, int>();
            for (var i = 0; i < n; i++) index[nodes[i]] = i;

            var averageWidth = nodes.Average(node => node.Width);
            var cutoff = averageWidth * 8;
            var cutoffSquared = cutoff * cutoff;
            var k = averageWidth * 1.15f;
            var px = new float[n];
            var py = new float[n];

            // Deterministic start: a spiral ordered by degree, focus at the center.
            var degree = new int[n];
            foreach (var edge in graph.Edges.Where(e => !e.IsSelf))
            {
                degree[index[edge.Source]]++;
                degree[index[edge.Target]]++;
            }
            var start = Enumerable.Range(0, n).OrderByDescending(i => nodes[i].IsFocus).ThenByDescending(i => degree[i]).ToList();
            for (var rank = 0; rank < n; rank++)
            {
                var angle = rank * 2.39996; // golden angle
                var radius = k * 0.6 * Math.Sqrt(rank);
                px[start[rank]] = (float)(Math.Cos(angle) * radius);
                py[start[rank]] = (float)(Math.Sin(angle) * radius);
            }

            var links = graph.Edges.Where(e => !e.IsSelf).Select(e => (index[e.Source], index[e.Target])).Distinct().ToArray();
            var iterations = n <= 150 ? 320 : Math.Max(60, Math.Min(320, (int)(30_000_000L / ((long)n * n))));
            var temperature = k * 2.5f;
            var cooling = temperature / (iterations + 1);
            var dx = new float[n];
            var dy = new float[n];

            for (var iteration = 0; iteration < iterations; iteration++)
            {
                Array.Clear(dx, 0, n);
                Array.Clear(dy, 0, n);

                for (var i = 0; i < n; i++)
                {
                    for (var j = i + 1; j < n; j++)
                    {
                        var x = px[i] - px[j];
                        var y = py[i] - py[j];
                        var d2 = x * x + y * y;
                        if (d2 < 0.01f)
                        {
                            x = 0.1f * ((i * 7 + j) % 5 - 2);
                            y = 0.1f * ((i * 3 + j) % 5 - 2);
                            d2 = x * x + y * y + 0.01f;
                        }
                        // Grid variant of Fruchterman-Reingold: far tables no longer push each other away.
                        if (d2 > cutoffSquared) continue;
                        var force = k * k / d2;
                        dx[i] += x * force;
                        dy[i] += y * force;
                        dx[j] -= x * force;
                        dy[j] -= y * force;
                    }
                }

                foreach (var (a, b) in links)
                {
                    var x = px[a] - px[b];
                    var y = py[a] - py[b];
                    var d = (float)Math.Sqrt(x * x + y * y) + 0.01f;
                    var force = d / k;
                    dx[a] -= x * force;
                    dy[a] -= y * force;
                    dx[b] += x * force;
                    dy[b] += y * force;
                }

                for (var i = 0; i < n; i++)
                {
                    // Gravity toward the origin so that disconnected tables do not drift away.
                    dx[i] -= px[i] * 0.05f;
                    dy[i] -= py[i] * 0.05f;

                    var length = (float)Math.Sqrt(dx[i] * dx[i] + dy[i] * dy[i]);
                    if (length < 0.0001f) continue;
                    var move = Math.Min(length, temperature);
                    px[i] += dx[i] / length * move;
                    py[i] += dy[i] / length * move;
                }
                temperature = Math.Max(1f, temperature - cooling);
            }

            for (var i = 0; i < n; i++) nodes[i].MoveCenterTo(px[i], py[i]);
            RemoveOverlaps(nodes, 24);

            if (isolated.Count > 0)
            {
                var bounds = new Graph();
                bounds.Nodes.AddRange(nodes);
                var area = bounds.GetBounds();
                Grid(isolated, area.X + area.Width / 2, area.Bottom + LayerGap);
            }
        }

        /// <summary>Pushes overlapping boxes apart along the axis of least penetration.</summary>
        private static void RemoveOverlaps(List<GraphNode> nodes, float margin)
        {
            for (var pass = 0; pass < 250; pass++)
            {
                var moved = false;
                var sorted = nodes.OrderBy(node => node.X).ToList();
                for (var i = 0; i < sorted.Count; i++)
                {
                    var a = sorted[i];
                    for (var j = i + 1; j < sorted.Count; j++)
                    {
                        var b = sorted[j];
                        if (b.X > a.X + a.Width + margin) break;
                        var overlapX = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X) + margin;
                        var overlapY = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y) + margin;
                        if (overlapX <= 0 || overlapY <= 0) continue;
                        moved = true;
                        if (overlapX < overlapY)
                        {
                            var shift = overlapX / 2;
                            if (a.Center.X <= b.Center.X) { a.X -= shift; b.X += shift; } else { a.X += shift; b.X -= shift; }
                        }
                        else
                        {
                            var shift = overlapY / 2;
                            if (a.Center.Y <= b.Center.Y) { a.Y -= shift; b.Y += shift; } else { a.Y += shift; b.Y -= shift; }
                        }
                    }
                }
                if (!moved) break;
            }
        }

        #endregion

        #region Circular

        /// <summary>
        /// With a focus table: radial layout, the focus at the center and one ring per hop.
        /// Without: tables on concentric circles, neighbors kept next to each other (BFS order from the hubs).
        /// </summary>
        private static void Circular(Graph graph)
        {
            var neighbors = Neighbors(graph);
            var focus = graph.Nodes.FirstOrDefault(n => n.IsFocus);
            var slot = graph.Nodes.Average(n => n.Width) + 40;

            if (focus != null)
            {
                focus.MoveCenterTo(0, 0);
                var rings = graph.Nodes.Where(n => n != focus)
                    .GroupBy(n => n.Distance < 0 ? int.MaxValue : n.Distance)
                    .OrderBy(g => g.Key)
                    .Select(g => BfsOrder(g.ToList(), neighbors))
                    .ToList();
                double radius = 0;
                foreach (var ring in rings) radius = PlaceRing(ring, Math.Max(radius + 230, ring.Count * slot / (2 * Math.PI)));
                return;
            }

            var ordered = BfsOrder(graph.Nodes, neighbors);
            if (ordered.Count == 1)
            {
                ordered[0].MoveCenterTo(0, 0);
                return;
            }

            var circle = Math.Max(240, ordered.Count * slot / (2 * Math.PI));
            if (ordered.Count <= 60)
            {
                PlaceRing(ordered, circle);
                return;
            }

            // Large graphs: concentric rings from the inside out, each filled to its circumference.
            var remaining = new Queue<GraphNode>(ordered);
            double r = 260;
            while (remaining.Count > 0)
            {
                var capacity = Math.Max(6, (int)(2 * Math.PI * r / slot));
                var ring = new List<GraphNode>();
                while (ring.Count < capacity && remaining.Count > 0) ring.Add(remaining.Dequeue());
                PlaceRing(ring, r);
                r += 160;
            }
        }

        private static double PlaceRing(List<GraphNode> ring, double radius)
        {
            for (var i = 0; i < ring.Count; i++)
            {
                var angle = -Math.PI / 2 + 2 * Math.PI * i / ring.Count;
                ring[i].MoveCenterTo((float)(Math.Cos(angle) * radius), (float)(Math.Sin(angle) * radius));
            }
            return radius;
        }

        private static List<GraphNode> BfsOrder(List<GraphNode> nodes, Dictionary<GraphNode, List<GraphNode>> neighbors)
        {
            var members = new HashSet<GraphNode>(nodes);
            var result = new List<GraphNode>();
            var seen = new HashSet<GraphNode>();
            foreach (var root in nodes.OrderByDescending(n => neighbors[n].Count).ThenBy(n => n.Key, StringComparer.Ordinal))
            {
                if (!seen.Add(root)) continue;
                var queue = new Queue<GraphNode>();
                queue.Enqueue(root);
                while (queue.Count > 0)
                {
                    var node = queue.Dequeue();
                    result.Add(node);
                    foreach (var next in neighbors[node].Where(members.Contains).OrderByDescending(m => neighbors[m].Count))
                    {
                        if (seen.Add(next)) queue.Enqueue(next);
                    }
                }
            }
            return result;
        }

        #endregion

        #region Grid

        private static void Grid(List<GraphNode> nodes, float centerX, float top)
        {
            if (nodes.Count == 0) return;
            var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(nodes.Count * 1.6)));
            var cell = nodes.Max(n => n.Width) + HorizontalGap;
            var rowHeight = nodes.Max(n => n.Height) + 34;
            var width = Math.Min(columns, nodes.Count) * cell - HorizontalGap;
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                node.X = centerX - width / 2 + i % columns * cell + (cell - HorizontalGap - node.Width) / 2;
                node.Y = top + i / columns * rowHeight;
            }
        }

        #endregion
    }
}
