using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DataverseCartographer.Models;

namespace DataverseCartographer.Export
{
    /// <summary>
    /// Text diagrams of the displayed graph for wikis and Git repositories: Mermaid erDiagram and PlantUML (IE notation).
    /// Each table lists its primary key and the lookup columns of the displayed relationships.
    /// </summary>
    public static class DiagramExporter
    {
        public static string ToMermaid(Graph graph, string title)
        {
            if (graph.Kind != GraphKind.DataModel) return PluginsToMermaid(graph, title);
            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
            {
                builder.AppendLine("---");
                builder.AppendLine("title: " + title.Replace(":", " -"));
                builder.AppendLine("---");
            }
            builder.AppendLine("erDiagram");

            foreach (var node in graph.Nodes)
            {
                var table = node.Table;
                builder.AppendLine($"    {Id(table.LogicalName)} {{");
                if (!string.IsNullOrEmpty(table.PrimaryIdAttribute))
                {
                    builder.AppendLine($"        uniqueidentifier {Id(table.PrimaryIdAttribute)} PK \"{Quote(table.Label)}\"");
                }
                foreach (var lookup in Lookups(graph, node))
                {
                    builder.AppendLine($"        lookup {Id(lookup.Column)} FK \"{Quote(lookup.Target)}\"");
                }
                builder.AppendLine("    }");
            }

            foreach (var edge in graph.Edges)
            {
                var relation = edge.Relation;
                var link = relation.Kind == RelationKind.ManyToMany ? "}o--o{" : "||--o{";
                var label = relation.Kind == RelationKind.ManyToMany ? relation.SchemaName : relation.ToAttribute;
                builder.AppendLine($"    {Id(edge.Source.Table.LogicalName)} {link} {Id(edge.Target.Table.LogicalName)} : \"{Quote(label)}\"");
            }
            return builder.ToString();
        }

        public static string ToPlantUml(Graph graph, string title)
        {
            if (graph.Kind != GraphKind.DataModel) return PluginsToPlantUml(graph, title);
            var builder = new StringBuilder();
            builder.AppendLine("@startuml");
            if (!string.IsNullOrEmpty(title)) builder.AppendLine("title " + title);
            builder.AppendLine("hide circle");
            builder.AppendLine("skinparam linetype ortho");
            builder.AppendLine("skinparam shadowing false");
            builder.AppendLine("skinparam class {");
            builder.AppendLine("  BackgroundColor White");
            builder.AppendLine("  BorderColor #94A3B8");
            builder.AppendLine("}");
            builder.AppendLine();

            foreach (var node in graph.Nodes)
            {
                var table = node.Table;
                var color = node.Table.IsCustom ? " #E3F4FA" : string.Empty;
                builder.AppendLine($"entity \"{Quote(table.Label)}\\n<size:10>{table.LogicalName}</size>\" as {Id(table.LogicalName)}{color} {{");
                if (!string.IsNullOrEmpty(table.PrimaryIdAttribute)) builder.AppendLine($"  * {table.PrimaryIdAttribute} : uniqueidentifier <<PK>>");
                var lookups = Lookups(graph, node).ToList();
                if (lookups.Count > 0)
                {
                    builder.AppendLine("  --");
                    foreach (var lookup in lookups) builder.AppendLine($"  {lookup.Column} : lookup <<FK>> {lookup.Target}");
                }
                builder.AppendLine("}");
            }
            builder.AppendLine();

            foreach (var edge in graph.Edges)
            {
                var relation = edge.Relation;
                var link = relation.Kind == RelationKind.ManyToMany ? "}o..o{" : "||--o{";
                var label = relation.Kind == RelationKind.ManyToMany ? relation.SchemaName : relation.ToAttribute;
                builder.AppendLine($"{Id(edge.Source.Table.LogicalName)} {link} {Id(edge.Target.Table.LogicalName)} : {label}");
            }
            builder.AppendLine("@enduml");
            return builder.ToString();
        }

        #region Plugin map

        /// <summary>Mermaid flowchart: tables as boxes, plugins as subroutines, triggers as arrows, code actions as thick arrows.</summary>
        private static string PluginsToMermaid(Graph graph, string title)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
            {
                builder.AppendLine("---");
                builder.AppendLine("title: " + title.Replace(":", " -"));
                builder.AppendLine("---");
            }
            builder.AppendLine("flowchart LR");
            foreach (var node in graph.Nodes)
            {
                var label = $"\"{Flow(node.Label)}<br/><small>{Flow(node.SubLabel)}</small>\"";
                switch (node.Kind)
                {
                    case NodeKind.Plugin: builder.AppendLine($"    {NodeId(node)}[[{label}]]"); break;
                    case NodeKind.Script: builder.AppendLine($"    {NodeId(node)}[({label})]"); break;
                    case NodeKind.Endpoint: builder.AppendLine($"    {NodeId(node)}[/{label}/]"); break;
                    case NodeKind.Message: builder.AppendLine($"    {NodeId(node)}{{{{{label}}}}}"); break;
                    default: builder.AppendLine($"    {NodeId(node)}[{label}]"); break;
                }
            }
            foreach (var edge in graph.Edges)
            {
                var arrow = edge.Kind == EdgeKind.Action ? (edge.Muted ? "-.->" : "==>") : "-->";
                builder.AppendLine($"    {NodeId(edge.Source)} {arrow}|\"{Flow(edge.Label)}\"| {NodeId(edge.Target)}");
            }
            builder.AppendLine("    classDef plugin fill:#EDE9FE,stroke:#6D28D9,color:#1E1B4B");
            builder.AppendLine("    classDef script fill:#FEF9C3,stroke:#CA8A04,color:#422006");
            var scripts = graph.Nodes.Where(n => n.Kind == NodeKind.Script).Select(NodeId).ToList();
            if (scripts.Count > 0) builder.AppendLine("    class " + string.Join(",", scripts) + " script");
            var plugins = graph.Nodes.Where(n => n.Kind == NodeKind.Plugin).Select(NodeId).ToList();
            if (plugins.Count > 0) builder.AppendLine("    class " + string.Join(",", plugins) + " plugin");
            return builder.ToString();
        }

        private static string PluginsToPlantUml(Graph graph, string title)
        {
            var builder = new StringBuilder();
            builder.AppendLine("@startuml");
            if (!string.IsNullOrEmpty(title)) builder.AppendLine("title " + title);
            builder.AppendLine("left to right direction");
            builder.AppendLine("skinparam shadowing false");
            foreach (var node in graph.Nodes)
            {
                var label = $"\"{Quote(node.Label)}\n<size:10>{Quote(node.SubLabel)}</size>\"";
                switch (node.Kind)
                {
                    case NodeKind.Plugin: builder.AppendLine($"component {label} as {NodeId(node)} #EDE9FE"); break;
                    case NodeKind.Script: builder.AppendLine($"artifact {label} as {NodeId(node)} #FEF9C3"); break;
                    case NodeKind.Endpoint: builder.AppendLine($"cloud {label} as {NodeId(node)}"); break;
                    case NodeKind.Message: builder.AppendLine($"hexagon {label} as {NodeId(node)}"); break;
                    default: builder.AppendLine($"rectangle {label} as {NodeId(node)}"); break;
                }
            }
            foreach (var edge in graph.Edges)
            {
                var arrow = edge.Kind == EdgeKind.Action ? (edge.Muted ? "..>" : "-[#DC2626,bold]->") : "-->";
                builder.AppendLine($"{NodeId(edge.Source)} {arrow} {NodeId(edge.Target)} : {edge.Label}");
            }
            builder.AppendLine("@enduml");
            return builder.ToString();
        }

        private static string NodeId(GraphNode node) => node.Kind == NodeKind.Table ? Id(node.Key) : "n_" + Id(node.Key);

        private static string Flow(string text) => (text ?? string.Empty).Replace("\"", "#quot;").Replace("|", "#124;");

        #endregion

        /// <summary>Lookup columns held by the table for the displayed 1:N relationships (polymorphic lookups grouped).</summary>
        private static IEnumerable<(string Column, string Target)> Lookups(Graph graph, GraphNode node) => graph.Edges
            .Where(e => e.Target == node && e.Relation.Kind == RelationKind.OneToMany && !string.IsNullOrEmpty(e.Relation.ToAttribute))
            .GroupBy(e => e.Relation.ToAttribute, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, string.Join(", ", g.Select(e => e.Source.Table.LogicalName).Distinct())));

        /// <summary>Mermaid / PlantUML identifiers: letters, digits and underscores only.</summary>
        private static string Id(string name)
        {
            if (string.IsNullOrEmpty(name)) return "_";
            var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
            var id = new string(chars);
            return char.IsDigit(id[0]) ? "_" + id : id;
        }

        private static string Quote(string text) => (text ?? string.Empty).Replace("\"", "'");
    }
}
