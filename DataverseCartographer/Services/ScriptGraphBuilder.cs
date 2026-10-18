using System;
using System.Collections.Generic;
using System.Linq;
using DataverseCartographer.Localization;
using DataverseCartographer.Models;
using DataverseCartographer.UI.Rendering;

namespace DataverseCartographer.Services
{
    public sealed class ScriptFilter
    {
        /// <summary>Forms and web resources of the selected solution, null = everything.</summary>
        public HashSet<Guid> SolutionComponents { get; set; }

        /// <summary>Only this library, null = every library.</summary>
        public string Library { get; set; }

        public bool HideMicrosoft { get; set; } = true;
        public bool IncludeInactiveForms { get; set; }

        /// <summary>Also draws reads and navigation (retrieve, open a form…).</summary>
        public bool ShowReads { get; set; }

        public string Focus { get; set; }
        public int Depth { get; set; } = 1;
    }

    /// <summary>
    /// Builds the JavaScript map: table —[form events]→ library —[what its code calls]→ table.
    /// Example: account —[Main form: OnLoad, OnChange (parentaccountid)]→ mso_/scripts/account.js —[Update]→ contact.
    /// </summary>
    public static class ScriptGraphBuilder
    {
        public const string ScriptPrefix = "script:";

        public static bool LibraryAllowed(ScriptLibraryInfo library, ScriptFilter filter) =>
            (!filter.HideMicrosoft || !library.IsMicrosoft)
            && (filter.Library == null || string.Equals(library.Name, filter.Library, StringComparison.OrdinalIgnoreCase))
            && (filter.SolutionComponents == null || library.Id.HasValue && filter.SolutionComponents.Contains(library.Id.Value)
                || library.Forms.Any(f => filter.SolutionComponents.Contains(f.Id)));

        public static Graph Build(ScriptModel scripts, OrgModel org, ScriptFilter filter)
        {
            var graph = new Graph { Kind = GraphKind.Scripts };
            GraphNode Node(string key, Func<GraphNode> create)
            {
                var node = graph.Find(key);
                if (node != null) return node;
                node = create();
                graph.Nodes.Add(node);
                graph.ByName[key] = node;
                return node;
            }
            GraphNode TableNode(string name) => Node(name, () => new GraphNode(org?.Find(name) ?? new TableInfo { LogicalName = name, DisplayName = name }));

            foreach (var library in scripts.Libraries.Values.Where(l => LibraryAllowed(l, filter)))
            {
                var forms = library.Forms.Where(f => (filter.IncludeInactiveForms || f.Active)
                                                     && (filter.SolutionComponents == null || filter.SolutionComponents.Contains(f.Id) || library.Id.HasValue && filter.SolutionComponents.Contains(library.Id.Value)))
                    .ToList();
                if (forms.Count == 0) continue;
                var key = ScriptPrefix + library.Name;
                var node = Node(key, () => new GraphNode(NodeKind.Script, key, ShortName(library.Name), library.Name, library));

                // Triggers: one edge per table, with the handlers of all its forms
                foreach (var group in forms.Where(f => !string.IsNullOrEmpty(f.Entity)).GroupBy(f => f.Entity, StringComparer.OrdinalIgnoreCase))
                {
                    var formNames = new HashSet<string>(group.Select(f => f.Name));
                    var handlers = library.Handlers.Where(h => formNames.Contains(h.Form) && group.Any(f => f.Handlers.Contains(h))).ToList();
                    var label = handlers.Count == 0 ? Strings.LoadedOnly : EventsLabel(handlers);
                    graph.Edges.Add(new GraphEdge(EdgeKind.Trigger, TableNode(group.Key), node, label, handlers) { Muted = handlers.Count == 0 || handlers.All(h => !h.Enabled) });
                }

                // Actions: what the code calls, by table
                if (library.Calls == null) continue;
                foreach (var group in library.Calls.Where(c => c.Entity != null && (filter.ShowReads || !c.IsRead)).GroupBy(c => c.Entity, StringComparer.OrdinalIgnoreCase))
                {
                    var calls = group.ToList();
                    var label = string.Join(", ", calls.Where(c => !c.IsRead).Select(c => c.Operation)
                        .Concat(calls.Where(c => c.IsRead).Select(c => c.Operation)).Distinct());
                    graph.Edges.Add(new GraphEdge(EdgeKind.Action, node, TableNode(group.Key), label, calls) { Muted = calls.All(c => c.IsRead) });
                }
            }
            graph.CandidateCount = graph.Nodes.Count;

            var focus = graph.Find(filter.Focus);
            if (focus != null)
            {
                var distances = new Dictionary<GraphNode, int> { [focus] = 0 };
                var frontier = new List<GraphNode> { focus };
                for (var level = 1; level <= Math.Max(1, filter.Depth) && frontier.Count > 0; level++)
                {
                    var next = new List<GraphNode>();
                    foreach (var edge in graph.Edges)
                    {
                        foreach (var (from, to) in new[] { (edge.Source, edge.Target), (edge.Target, edge.Source) })
                        {
                            if (!frontier.Contains(from) || distances.ContainsKey(to)) continue;
                            distances[to] = level;
                            next.Add(to);
                        }
                    }
                    frontier = next;
                }
                graph.Nodes.RemoveAll(n => !distances.ContainsKey(n));
                graph.Edges.RemoveAll(e => !distances.ContainsKey(e.Source) || !distances.ContainsKey(e.Target));
                graph.ByName.Clear();
                foreach (var node in graph.Nodes)
                {
                    graph.ByName[node.Key] = node;
                    node.Distance = distances[node];
                    node.IsFocus = node == focus;
                }
            }

            using (var measure = new NodeMeasurer())
            {
                foreach (var node in graph.Nodes) measure.Fit(node);
            }
            graph.Nodes.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            GraphBuilder.AssignParallelIndexes(graph);
            return graph;
        }

        /// <summary>« account.js » for « mso_/scripts/account.js ».</summary>
        public static string ShortName(string name)
        {
            var index = name.LastIndexOf('/');
            return index >= 0 && index < name.Length - 1 ? name.Substring(index + 1) : name;
        }

        /// <summary>« OnLoad, OnChange (parentaccountid) +2 ».</summary>
        public static string EventsLabel(IList<FormHandler> handlers)
        {
            var parts = handlers.Select(h => Strings.EventLabel(h.Event, h.Source)).Distinct().ToList();
            return parts.Count <= 3 ? string.Join(", ", parts) : string.Join(", ", parts.Take(3)) + $" +{parts.Count - 3}";
        }
    }
}
