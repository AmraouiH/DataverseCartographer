using System;
using System.Collections.Generic;
using System.Linq;
using DataverseCartographer.Helpers;
using DataverseCartographer.Localization;
using DataverseCartographer.Models;

namespace DataverseCartographer.Export
{
    /// <summary>
    /// Tabular inventory of the displayed graph: tables, relationships (with cascade behaviors) and the columns of every table
    /// whose details were loaded.
    /// </summary>
    public static class ExcelExporter
    {
        public static void Save(Graph graph, string path)
        {
            var excel = new ExcelWriter();
            var s = Strings.Excel;

            excel.AddSheet(s.TablesSheet,
                new[] { s.DisplayName, s.LogicalName, s.SchemaName, s.Custom, s.Managed, s.Ownership, s.PrimaryKey, s.PrimaryName, s.Prefix, s.Relationships, s.OutsideFilters, s.Description },
                graph.Nodes.OrderBy(n => n.Table.Label).Select(n => new object[]
                {
                    n.Table.Label, n.Table.LogicalName, n.Table.SchemaName, YesNo(n.Table.IsCustom), YesNo(n.Table.IsManaged),
                    Strings.OwnershipLabel(n.Table.Ownership), n.Table.PrimaryIdAttribute, n.Table.PrimaryNameAttribute, n.Table.Prefix,
                    graph.Edges.Count(e => e.Source == n || e.Target == n), YesNo(n.IsGhost), n.Table.Description
                }),
                new double[] { 30, 28, 28, 9, 10, 18, 26, 22, 10, 14, 14, 60 });

            excel.AddSheet(s.RelationshipsSheet,
                new[] { s.SchemaName, s.Type, s.OneTable, s.ManyTable, s.Lookup, s.ReferencedColumn, s.Intersect, "Assign", "Delete", "Merge", "Reparent", "Share", "Unshare", "RollupView", s.Custom, s.Managed },
                graph.Edges.OrderBy(e => e.Relation.SchemaName).Select(e =>
                {
                    var r = e.Relation;
                    var c = r.Cascade;
                    return new object[]
                    {
                        r.SchemaName, r.KindLabel, r.From, r.To, r.ToAttribute, r.FromAttribute, r.Intersect,
                        c?.Assign, c?.Delete, c?.Merge, c?.Reparent, c?.Share, c?.Unshare, c?.RollupView, YesNo(r.IsCustom), YesNo(r.IsManaged)
                    };
                }),
                new double[] { 40, 8, 26, 26, 26, 22, 30, 12, 12, 12, 12, 12, 12, 12, 9, 10 });

            var columns = graph.Nodes
                .Where(n => n.Table.Details != null)
                .OrderBy(n => n.Table.LogicalName)
                .SelectMany(n => n.Table.Details.Columns.Select(c => new object[]
                {
                    n.Table.LogicalName, c.DisplayName, c.LogicalName, c.Type, Strings.RequiredLabel(c.RequiredLevel), YesNo(c.IsCustom),
                    c.IsPrimaryId ? "PK" : c.IsPrimaryName ? s.PrimaryName : null, c.Targets, c.MaxLength, c.Description
                }))
                .ToList();
            if (columns.Count > 0)
            {
                excel.AddSheet(s.ColumnsSheet,
                    new[] { s.Table, s.DisplayName, s.LogicalName, s.Type, s.Required, s.Custom, s.Role, s.Targets, s.MaxLength, s.Description },
                    columns,
                    new double[] { 26, 30, 30, 18, 14, 9, 14, 26, 10, 60 });
            }

            excel.Save(path);
        }

        /// <summary>Inventory of the plugin map: steps (with images) and the operations found in the code.</summary>
        public static void SavePlugins(Graph graph, string path)
        {
            var excel = new ExcelWriter();
            var s = Strings.Excel;
            var steps = graph.Edges.Where(e => e.Kind == EdgeKind.Trigger).SelectMany(e => ((IEnumerable<StepInfo>)e.Tag).Select(step => (Edge: e, Step: step))).ToList();
            excel.AddSheet(s.StepsSheet,
                new[] { s.Message, s.Table, s.Stage, s.Mode, s.Rank, s.Handler, s.Assembly, s.FilteringAttributes, s.Images, s.State, s.StepName },
                steps.OrderBy(x => x.Step.Entity).ThenBy(x => x.Step.Message).ThenBy(x => x.Step.Stage).ThenBy(x => x.Step.Rank).Select(x => new object[]
                {
                    x.Step.Message, x.Step.Entity ?? Strings.GlobalMessage, Strings.StageLabel(x.Step.Stage), Strings.ModeLabel(x.Step.Mode), x.Step.Rank,
                    (x.Edge.Target.Tag as PluginTypeInfo)?.TypeName ?? x.Step.HandlerName, (x.Edge.Target.Tag as PluginTypeInfo)?.AssemblyName,
                    x.Step.FilteringAttributes, Strings.ImagesLabel(x.Step.Images), x.Step.Enabled ? Strings.Enabled : Strings.Disabled, x.Step.Name
                }),
                new double[] { 16, 24, 18, 12, 7, 46, 30, 40, 40, 10, 50 });

            var operations = graph.Nodes.Where(n => n.Kind == NodeKind.Plugin)
                .SelectMany(n => (((PluginTypeInfo)n.Tag).Operations ?? new List<CodeOperation>()).Select(o => (Type: (PluginTypeInfo)n.Tag, Operation: o)))
                .ToList();
            excel.AddSheet(s.OperationsSheet,
                new[] { s.Handler, s.Assembly, s.Operation, s.Table, s.Method, s.Inferred },
                operations.OrderBy(x => x.Type.TypeName).ThenBy(x => x.Operation.Entity).Select(x => new object[]
                {
                    x.Type.TypeName, x.Type.AssemblyName, x.Operation.Operation, x.Operation.Entity ?? Strings.TableNotDetected, x.Operation.Method, YesNo(x.Operation.Inferred)
                }),
                new double[] { 46, 30, 16, 26, 60, 10 });
            excel.Save(path);
        }

        /// <summary>Inventory of the JavaScript map: form event handlers and the Dataverse calls found in the scripts.</summary>
        public static void SaveScripts(Graph graph, string path)
        {
            var excel = new ExcelWriter();
            var s = Strings.Excel;
            var libraries = graph.Nodes.Where(n => n.Kind == NodeKind.Script).Select(n => (ScriptLibraryInfo)n.Tag).ToList();
            var tables = new HashSet<string>(graph.Nodes.Where(n => n.Kind == NodeKind.Table).Select(n => n.Key), StringComparer.OrdinalIgnoreCase);

            excel.AddSheet(s.HandlersSheet,
                new[] { s.Table, s.Form, s.Event, s.Source, s.Function, s.Library, s.State },
                libraries.SelectMany(l => l.Forms.Where(f => tables.Contains(f.Entity ?? string.Empty))
                        .SelectMany(f => f.Handlers.Where(h => string.Equals(h.Library, l.Name, StringComparison.OrdinalIgnoreCase)).Select(h => (Form: f, Handler: h))))
                    .OrderBy(x => x.Form.Entity).ThenBy(x => x.Form.Name).ThenBy(x => x.Handler.Event)
                    .Select(x => new object[]
                    {
                        x.Form.Entity, x.Form.Name, x.Handler.Event, x.Handler.Source, x.Handler.Function, x.Handler.Library, x.Handler.Enabled ? Strings.Enabled : Strings.Disabled
                    }),
                new double[] { 22, 30, 16, 22, 36, 40, 10 });

            excel.AddSheet(s.CallsSheet,
                new[] { s.Library, s.Operation, s.Table, s.Function, s.Line },
                libraries.SelectMany(l => (l.Calls ?? new List<ScriptCall>()).Select(c => (Library: l, Call: c)))
                    .OrderBy(x => x.Library.Name).ThenBy(x => x.Call.Line)
                    .Select(x => new object[] { x.Library.Name, x.Call.Operation, x.Call.Entity ?? Strings.TableNotDetected, x.Call.Function, x.Call.Line }),
                new double[] { 40, 22, 24, 30, 8 });
            excel.Save(path);
        }

        private static string YesNo(bool value) => value ? Strings.Yes : Strings.No;
    }
}
