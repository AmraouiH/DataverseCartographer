using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DataverseCartographer.Localization;
using DataverseCartographer.Models;
using DataverseCartographer.UI.Controls;
using DataverseCartographer.UI.Rendering;

namespace DataverseCartographer.UI
{
    /// <summary>Contents of the hover cards of the maps: one precise, structured card per kind of node and edge.</summary>
    internal static class HoverContentBuilder
    {
        private const int MaxListed = 40;

        public static HoverContent Build(object target)
        {
            switch (target)
            {
                case GraphNode node: return ForNode(node);
                case GraphEdge edge: return ForEdge(edge);
                default: return null;
            }
        }

        private static string TableText(string logicalName, GraphEdge edge)
        {
            var node = edge.Source.Key == logicalName ? edge.Source : edge.Target.Key == logicalName ? edge.Target : null;
            return node == null || node.Label == logicalName ? logicalName : $"{node.Label} ({logicalName})";
        }

        #region Nodes

        private static HoverContent ForNode(GraphNode node)
        {
            switch (node.Kind)
            {
                case NodeKind.Plugin: return ForPlugin(node, (PluginTypeInfo)node.Tag);
                case NodeKind.Script: return ForScript(node, (ScriptLibraryInfo)node.Tag);
                case NodeKind.Endpoint:
                    return new HoverContent { Title = node.Label, Subtitle = Strings.ServiceEndpoint, Accent = GraphRenderer.EndpointNode, Badge = "Endpoint" };
                case NodeKind.Message:
                    return new HoverContent { Title = node.Label, Subtitle = Strings.GlobalMessage, Accent = GraphRenderer.MessageNode, Badge = "Message" };
            }

            var table = node.Table;
            var content = new HoverContent
            {
                Title = table.Label,
                Subtitle = table.LogicalName,
                Accent = GraphRenderer.KindColor(node),
                Badge = table.IsCustom ? "Custom" : "Standard"
            };
            content.Row(Strings.PropSchemaName, table.SchemaName)
                .Row(Strings.PropOwnership, Strings.OwnershipLabel(table.Ownership))
                .Row(Strings.PropPrimaryKey, table.PrimaryIdAttribute)
                .Row(Strings.PropPrimaryName, table.PrimaryNameAttribute);
            if (table.Relations.Count > 0)
            {
                content.Row(Strings.PropRelations, Strings.PropRelationsValue(
                    table.Relations.Count(r => r.Kind == RelationKind.OneToMany && r.From == table.LogicalName),
                    table.Relations.Count(r => r.Kind == RelationKind.OneToMany && !r.IsSelf && r.To == table.LogicalName),
                    table.Relations.Count(r => r.Kind == RelationKind.ManyToMany)));
            }
            if (table.RecordCount.HasValue || table.HasActiveRecords.HasValue)
            {
                content.Row(Strings.PropRecords, Strings.RecordsValue(table.RecordCount, table.HasActiveRecords), table.HasActiveRecords == false ? Theme.Muted : (Color?)null);
            }
            if (table.IsManaged) content.Row("Solution", "Managed");
            if (node.IsGhost) content.Row(Strings.HoverNote, Strings.OutsideFiltersNote, Theme.Muted);
            if (!string.IsNullOrEmpty(table.Description)) content.Row(Strings.PropDescription, table.Description, Theme.Muted);
            return content;
        }

        private static HoverContent ForPlugin(GraphNode node, PluginTypeInfo type)
        {
            var content = new HoverContent { Title = type.ShortName, Subtitle = type.TypeName, Accent = GraphRenderer.PluginNode, Badge = "Plugin" };
            content.Row(Strings.PropAssembly, type.AssemblyName);
            if (type.Operations == null)
            {
                content.Row(Strings.PropCodeAnalysis, Strings.NotAnalyzed, Theme.Muted);
                return content;
            }
            var operations = type.Operations.Where(o => o.Entity != null).ToList();
            if (operations.Count == 0)
            {
                content.Row(Strings.PropCodeAnalysis, Strings.NoOperations, Theme.Muted);
                return content;
            }
            content.Section(Strings.TabCodeOperations);
            foreach (var group in operations.GroupBy(o => o.Entity).Take(MaxListed))
            {
                var writes = group.Where(o => !o.IsRead).Select(o => o.Operation).Distinct().ToList();
                var text = string.Join(", ", writes.Concat(group.Any(o => o.IsRead) ? new[] { "Retrieve" } : new string[0]));
                content.Row(group.Key, text + (group.All(o => o.Inferred) ? "  (?)" : string.Empty), writes.Count > 0 ? GraphRenderer.WriteAction : Theme.Muted);
            }
            return content;
        }

        private static HoverContent ForScript(GraphNode node, ScriptLibraryInfo library)
        {
            var content = new HoverContent { Title = library.Name, Subtitle = library.DisplayName ?? Strings.WebResourceJs, Accent = GraphRenderer.ScriptNode, Badge = "JS" };
            content.Row(Strings.PropForms, library.Forms.Count.ToString())
                .Row(Strings.PropHandlers, library.Handlers.Count.ToString());
            if (library.Calls == null)
            {
                content.Row(Strings.PropCodeAnalysis, library.AnalysisNote ?? Strings.NotAnalyzed, Theme.Muted);
                return content;
            }
            var calls = library.Calls.Where(c => c.Entity != null).ToList();
            if (calls.Count == 0)
            {
                content.Row(Strings.PropCodeAnalysis, Strings.NoScriptCalls, Theme.Muted);
                return content;
            }
            content.Section(Strings.TabScriptCalls);
            foreach (var group in calls.GroupBy(c => c.Entity).Take(MaxListed))
            {
                content.Row(group.Key, string.Join(", ", group.Select(c => c.Operation).Distinct()), group.Any(c => !c.IsRead) ? GraphRenderer.WriteAction : Theme.Muted);
            }
            return content;
        }

        #endregion

        #region Edges

        private static HoverContent ForEdge(GraphEdge edge)
        {
            switch (edge.Kind)
            {
                case EdgeKind.Trigger: return ForTrigger(edge);
                case EdgeKind.Action: return ForAction(edge);
            }

            var relation = edge.Relation;
            var content = new HoverContent
            {
                Title = relation.SchemaName,
                Subtitle = relation.Kind == RelationKind.ManyToMany
                    ? $"{edge.Source.Label} ⇄ {edge.Target.Label}"
                    : $"{edge.Source.Label} (1) → {edge.Target.Label} (N)",
                Badge = relation.KindLabel,
                Accent = GraphRenderer.EdgeColor(edge)
            };

            if (relation.Kind == RelationKind.ManyToMany)
            {
                content.Row(Strings.PropEntity1, TableText(relation.From, edge))
                    .Row(Strings.PropEntity2, TableText(relation.To, edge))
                    .Row(Strings.PropIntersect, relation.Intersect);
            }
            else
            {
                content.Row(Strings.PropOneSide, TableText(relation.From, edge))
                    .Row(Strings.PropManySide, TableText(relation.To, edge))
                    .Row(Strings.PropLookup, $"{relation.To}.{relation.ToAttribute}")
                    .Row(Strings.PropReferenced, $"{relation.From}.{relation.FromAttribute}");
                var cascade = relation.Cascade;
                if (cascade != null)
                {
                    content.Section(Strings.CascadeHeader);
                    content.Row("Delete", cascade.Delete, cascade.CascadesDelete ? Theme.Danger : cascade.RestrictsDelete ? Theme.WarningText : (Color?)null)
                        .Row("Assign", cascade.Assign)
                        .Row("Share", cascade.Share)
                        .Row("Unshare", cascade.Unshare)
                        .Row("Reparent", cascade.Reparent)
                        .Row("Merge", cascade.Merge);
                    if (cascade.CascadesDelete) content.Row(Strings.HoverNote, Strings.CascadeDeleteWarning, Theme.Danger);
                }
            }
            content.Section("Solution");
            content.Row(Strings.PropKind, relation.IsCustom ? Strings.CustomRelationship : Strings.SystemRelationship)
                .Row("Managed", relation.IsManaged ? Strings.Yes : Strings.No);
            return content;
        }

        private static HoverContent ForTrigger(GraphEdge edge)
        {
            var content = new HoverContent
            {
                Title = $"{edge.Source.Label} → {edge.Target.Label}",
                Subtitle = edge.Target.Kind == NodeKind.Script ? Strings.ScriptTriggerSubtitle : Strings.TriggerSubtitle,
                Accent = GraphRenderer.EdgeColor(edge)
            };

            if (edge.Tag is IEnumerable<FormHandler> handlers)
            {
                var list = handlers.ToList();
                content.Badge = list.Count.ToString();
                foreach (var form in list.GroupBy(h => h.Form).Take(MaxListed))
                {
                    content.Section(form.Key);
                    foreach (var handler in form)
                    {
                        content.Row(Strings.EventLabel(handler.Event, handler.Source), handler.Function, handler.Enabled ? (Color?)null : Theme.Subtle);
                    }
                }
                return content;
            }

            var steps = ((IEnumerable<StepInfo>)edge.Tag).ToList();
            content.Badge = steps.Count == 1 ? "1 step" : $"{steps.Count} steps";
            foreach (var step in steps.Take(MaxListed))
            {
                var details = new List<string> { Strings.ModeLabel(step.Mode), $"rank {step.Rank}" };
                if (!string.IsNullOrEmpty(step.FilteringAttributes)) details.Add(Strings.FiltersLabel(step.FilteringAttributes));
                if (step.Images.Count > 0) details.Add(Strings.ImagesLabel(step.Images));
                if (!step.Enabled) details.Add(Strings.Disabled);
                content.Row($"{step.Message} · {Strings.StageLabel(step.Stage)}", string.Join(" · ", details), step.Enabled ? (Color?)null : Theme.Subtle);
            }
            return content;
        }

        private static HoverContent ForAction(GraphEdge edge)
        {
            var content = new HoverContent
            {
                Title = $"{edge.Source.Label} → {edge.Target.Label}",
                Subtitle = edge.Source.Kind == NodeKind.Script ? Strings.ScriptActionSubtitle : Strings.ActionSubtitle,
                Accent = GraphRenderer.EdgeColor(edge),
                Badge = edge.Label
            };
            if (edge.Tag is IEnumerable<ScriptCall> calls)
            {
                foreach (var call in calls.Take(MaxListed))
                {
                    content.Row(call.Operation, string.IsNullOrEmpty(call.Function) ? $"{Strings.LineLabel} {call.Line}" : $"{call.Function}()  ·  {Strings.LineLabel} {call.Line}",
                        call.IsRead ? Theme.Muted : (Color?)null);
                }
                return content;
            }
            foreach (var operation in ((IEnumerable<CodeOperation>)edge.Tag).Take(MaxListed))
            {
                content.Row(operation.Operation, operation.Method + (operation.Inferred ? "  (?)" : string.Empty), operation.IsRead ? Theme.Muted : (Color?)null);
            }
            return content;
        }

        #endregion
    }
}
