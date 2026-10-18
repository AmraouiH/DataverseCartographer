using System;
using System.Collections.Generic;
using System.Drawing;

namespace DataverseCartographer.Models
{
    public enum TableKindFilter
    {
        All,
        CustomOnly,
        StandardOnly
    }

    public enum GraphKind
    {
        DataModel,
        Plugins,
        Scripts
    }

    public enum NodeKind
    {
        Table,
        Plugin,
        Endpoint,
        Message,
        Script
    }

    public enum EdgeKind
    {
        /// <summary>Table relationship (1:N or N:N), data model map.</summary>
        Relationship,

        /// <summary>Table (or global message) → plugin: the registered steps that run the plugin.</summary>
        Trigger,

        /// <summary>Plugin → table: what the plugin code does on that table (create, update…).</summary>
        Action
    }

    public enum LayoutKind
    {
        Hierarchical,
        ForceDirected,
        Circular,
        Grid
    }

    /// <summary>
    /// What to display: the filters define the universe of tables, the focus (optional) keeps only the neighbors
    /// of one table within <see cref="Depth"/> relationship hops.
    /// </summary>
    public sealed class GraphFilter
    {
        /// <summary>Tables of the selected solution (metadata ids), null = every table.</summary>
        public HashSet<Guid> SolutionTables { get; set; }

        /// <summary>Tables of the selected model-driven app (metadata ids), null = every table.</summary>
        public HashSet<Guid> AppTables { get; set; }
        public string Prefix { get; set; }
        public TableKindFilter Kind { get; set; }
        public bool HideSystemTables { get; set; } = true;
        public bool HideIsolated { get; set; }

        /// <summary>Only the tables that hold at least one active record (tables whose usage is unknown stay visible).</summary>
        public bool OnlyUsedTables { get; set; }
        public bool ShowManyToMany { get; set; } = true;
        public bool ShowSelfReferences { get; set; } = true;
        public string Focus { get; set; }
        public int Depth { get; set; } = 1;

        /// <summary>Also shows (faded) the tables outside the filters directly linked to a displayed table.</summary>
        public bool ShowBoundary { get; set; }

        /// <summary>Tables hidden one by one from the graph context menu.</summary>
        public HashSet<string> Excluded { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class GraphNode
    {
        public GraphNode(TableInfo table)
        {
            Table = table;
            Kind = NodeKind.Table;
            Key = table.LogicalName;
            Label = table.Label;
            SubLabel = table.LogicalName;
        }

        public GraphNode(NodeKind kind, string key, string label, string subLabel, object tag)
        {
            Kind = kind;
            Key = key;
            Label = label;
            SubLabel = subLabel;
            Tag = tag;
        }

        public NodeKind Kind { get; }

        /// <summary>Unique key in the graph (logical name for a table).</summary>
        public string Key { get; }

        public string Label { get; }
        public string SubLabel { get; }

        /// <summary>The table (null for plugins, endpoints and messages).</summary>
        public TableInfo Table { get; }

        /// <summary>Plugin type, service endpoint… depending on <see cref="Kind"/>.</summary>
        public object Tag { get; }

        /// <summary>Top-left corner in graph coordinates.</summary>
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; } = 190;
        public float Height { get; set; } = 50;

        /// <summary>Outside the filters, shown only because it is linked to a displayed table.</summary>
        public bool IsGhost { get; set; }
        public bool IsFocus { get; set; }

        /// <summary>Hops from the focus table (0 = focus), -1 without focus.</summary>
        public int Distance { get; set; } = -1;

        /// <summary>Texts fitted to the node width when the graph is built.</summary>
        public string Title { get; set; }
        public string Caption { get; set; }

        public RectangleF Bounds => new RectangleF(X, Y, Width, Height);
        public PointF Center => new PointF(X + Width / 2, Y + Height / 2);

        public void MoveCenterTo(float x, float y)
        {
            X = x - Width / 2;
            Y = y - Height / 2;
        }
    }

    public sealed class GraphEdge
    {
        public GraphEdge(RelationInfo relation, GraphNode source, GraphNode target)
        {
            Kind = EdgeKind.Relationship;
            Relation = relation;
            Source = source;
            Target = target;
        }

        public GraphEdge(EdgeKind kind, GraphNode source, GraphNode target, string label, object tag)
        {
            Kind = kind;
            Source = source;
            Target = target;
            Label = label;
            Tag = tag;
        }

        public EdgeKind Kind { get; }

        /// <summary>The relationship (data model map only).</summary>
        public RelationInfo Relation { get; }

        /// <summary>Text drawn on trigger / action edges.</summary>
        public string Label { get; }

        /// <summary>Steps (trigger) or code operations (action).</summary>
        public object Tag { get; }

        /// <summary>Drawn faded: disabled steps, read-only operations.</summary>
        public bool Muted { get; set; }

        /// <summary>Trigger edge whose steps are all asynchronous.</summary>
        public bool Async { get; set; }

        /// <summary>One side (referenced table, or Entity1 for N:N); the origin of a trigger or action.</summary>
        public GraphNode Source { get; }

        /// <summary>Many side (referencing table holding the lookup, or Entity2 for N:N).</summary>
        public GraphNode Target { get; }

        /// <summary>Rank among the edges linking the same two tables, used to fan them out.</summary>
        public int ParallelIndex { get; set; }
        public int ParallelCount { get; set; } = 1;

        /// <summary>Sideways offset given to an edge whose straight line would cross another table (see GraphLayout.RouteEdges).</summary>
        public float Bend { get; set; }

        public bool IsSelf => ReferenceEquals(Source, Target);
    }

    public sealed class Graph
    {
        public GraphKind Kind { get; set; }

        public List<GraphNode> Nodes { get; } = new List<GraphNode>();
        public List<GraphEdge> Edges { get; } = new List<GraphEdge>();
        public Dictionary<string, GraphNode> ByName { get; } = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Tables in the universe before the focus was applied (for the status line).</summary>
        public int CandidateCount { get; set; }

        public GraphNode Find(string logicalName) =>
            logicalName != null && ByName.TryGetValue(logicalName, out var node) ? node : null;

        public RectangleF GetBounds()
        {
            if (Nodes.Count == 0) return RectangleF.Empty;
            float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
            foreach (var node in Nodes)
            {
                left = Math.Min(left, node.X);
                top = Math.Min(top, node.Y);
                right = Math.Max(right, node.X + node.Width);
                bottom = Math.Max(bottom, node.Y + node.Height);
            }
            return RectangleF.FromLTRB(left, top, right, bottom);
        }
    }
}
