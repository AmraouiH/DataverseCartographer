using System;
using System.Collections.Generic;
using System.Linq;
using DataverseCartographer.Localization;
using DataverseCartographer.Models;
using DataverseCartographer.UI.Rendering;

namespace DataverseCartographer.Services
{
    /// <summary>What the plugin map shows.</summary>
    public sealed class PluginFilter
    {
        /// <summary>Plugin assemblies, types and steps of the selected solution, null = everything.</summary>
        public HashSet<Guid> SolutionComponents { get; set; }

        /// <summary>Only this assembly (DLL), null = every assembly.</summary>
        public Guid? AssemblyId { get; set; }

        /// <summary>Hides Microsoft / platform assemblies and the system steps.</summary>
        public bool HideMicrosoft { get; set; } = true;

        public bool IncludeDisabled { get; set; }

        /// <summary>Also draws the tables the plugins only read (Retrieve / RetrieveMultiple).</summary>
        public bool ShowReads { get; set; }

        /// <summary>Key of the node to explore around, null = whole map.</summary>
        public string Focus { get; set; }

        public int Depth { get; set; } = 1;
    }

    /// <summary>
    /// Builds the plugin map: table (or global message) → trigger (the steps) → plugin → action (what its code does) → table.
    /// Example: contact —[Create · Post]→ ContactPlugin —[Update]→ account.
    /// </summary>
    public static class PluginGraphBuilder
    {
        public const string TypePrefix = "type:";
        public const string EndpointPrefix = "endpoint:";
        public const string MessagePrefix = "message:";

        public static Graph Build(PluginModel plugins, OrgModel org, PluginFilter filter)
        {
            var assemblies = plugins.Assemblies.ToDictionary(a => a.Id);
            var types = plugins.Types.ToDictionary(t => t.Id);

            bool TypeAllowed(PluginTypeInfo type)
            {
                if (filter.AssemblyId.HasValue && type.AssemblyId != filter.AssemblyId.Value) return false;
                if (filter.HideMicrosoft && assemblies.TryGetValue(type.AssemblyId, out var assembly) && assembly.IsMicrosoft) return false;
                return true;
            }

            bool InSolution(Guid id, PluginTypeInfo type) =>
                filter.SolutionComponents == null || filter.SolutionComponents.Contains(id)
                || type != null && (filter.SolutionComponents.Contains(type.Id) || filter.SolutionComponents.Contains(type.AssemblyId));

            var graph = new Graph { Kind = GraphKind.Plugins };
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
            GraphNode TypeNode(PluginTypeInfo type) => Node(TypePrefix + type.Id, () => new GraphNode(NodeKind.Plugin, TypePrefix + type.Id, type.ShortName, type.AssemblyName, type));

            // 1. Triggers: steps grouped by (table or message, handler)
            var triggers = new Dictionary<(GraphNode, GraphNode), List<StepInfo>>();
            foreach (var step in plugins.Steps)
            {
                if (!filter.IncludeDisabled && !step.Enabled) continue;
                if (filter.HideMicrosoft && step.CustomizationLevel == 0) continue;

                GraphNode handler;
                if (step.PluginTypeId.HasValue)
                {
                    if (!types.TryGetValue(step.PluginTypeId.Value, out var type) || !TypeAllowed(type) || !InSolution(step.Id, type)) continue;
                    handler = TypeNode(type);
                }
                else if (step.EndpointId.HasValue)
                {
                    if (filter.AssemblyId.HasValue || !InSolution(step.Id, null)) continue;
                    var key = EndpointPrefix + step.EndpointId.Value;
                    handler = Node(key, () => new GraphNode(NodeKind.Endpoint, key, step.HandlerName ?? Strings.ServiceEndpoint, Strings.ServiceEndpoint, step.EndpointId.Value));
                }
                else
                {
                    continue;
                }

                var source = step.Entity != null
                    ? TableNode(step.Entity)
                    : Node(MessagePrefix + step.Message, () => new GraphNode(NodeKind.Message, MessagePrefix + step.Message, step.Message, Strings.GlobalMessage, step.Message));
                if (!triggers.TryGetValue((source, handler), out var list)) triggers[(source, handler)] = list = new List<StepInfo>();
                list.Add(step);
            }

            foreach (var pair in triggers)
            {
                var steps = pair.Value.OrderBy(s => s.Stage).ThenBy(s => s.Message).ToList();
                graph.Edges.Add(new GraphEdge(EdgeKind.Trigger, pair.Key.Item1, pair.Key.Item2, TriggerLabel(steps), steps)
                {
                    Async = steps.All(s => s.IsAsync),
                    Muted = steps.All(s => !s.Enabled)
                });
            }

            // Plugins of the selected DLL / solution that have no step (custom API, workflow activity…) but whose code touches tables
            if (filter.AssemblyId.HasValue || filter.SolutionComponents != null)
            {
                foreach (var type in plugins.Types.Where(t => TypeAllowed(t) && InSolution(t.Id, t) && t.Operations != null && t.Operations.Any(o => o.Entity != null)))
                {
                    TypeNode(type);
                }
            }

            // 2. Actions: what the code of each displayed plugin does, grouped by table
            foreach (var node in graph.Nodes.Where(n => n.Kind == NodeKind.Plugin).ToList())
            {
                var type = (PluginTypeInfo)node.Tag;
                if (type.Operations == null) continue;
                foreach (var group in type.Operations.Where(o => o.Entity != null && (filter.ShowReads || !o.IsRead)).GroupBy(o => o.Entity, StringComparer.OrdinalIgnoreCase))
                {
                    var operations = group.ToList();
                    var label = string.Join(", ", operations.Where(o => !o.IsRead).Select(o => o.Operation).Concat(operations.Any(o => o.IsRead) ? new[] { "Retrieve" } : new string[0]).Distinct());
                    graph.Edges.Add(new GraphEdge(EdgeKind.Action, node, TableNode(group.Key), label, operations) { Muted = operations.All(o => o.IsRead) });
                }
            }
            graph.CandidateCount = graph.Nodes.Count;

            // 3. Focus: neighbors of one node, N hops (whatever the direction)
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
                            if (frontier.Contains(from) && !distances.ContainsKey(to))
                            {
                                distances[to] = level;
                                next.Add(to);
                            }
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

        /// <summary>« Create · Post, Update · Pre » (messages by stage), shortened beyond three entries.</summary>
        public static string TriggerLabel(IList<StepInfo> steps)
        {
            var parts = steps.Select(s => $"{s.Message} · {Strings.StageShort(s.Stage)}{(s.IsAsync ? " (async)" : string.Empty)}").Distinct().ToList();
            return parts.Count <= 3 ? string.Join(", ", parts) : string.Join(", ", parts.Take(3)) + $" +{parts.Count - 3}";
        }
    }
}
