using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DataverseCartographer.Models;
using DataverseCartographer.UI.Rendering;

namespace DataverseCartographer.Services
{
    /// <summary>
    /// Turns the organization model + filters into the graph to display.
    /// Universe = tables passing the system filter; candidates = universe passing the solution / publisher / kind filters;
    /// with a focus table, only the candidates within N hops of it are kept.
    /// </summary>
    public static class GraphBuilder
    {
        /// <summary>
        /// Technical tables linked to almost everything (owner, audit, sharing, processes…) that turn a schema into spaghetti.
        /// </summary>
        public static readonly HashSet<string> SystemTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "activitypointer", "activityparty", "systemuser", "team", "businessunit", "organization", "owner", "transactioncurrency",
            "principalobjectaccess", "asyncoperation", "processsession", "workflow", "workflowlog", "bulkdeletefailure", "bulkdeleteoperation",
            "annotation", "connection", "connectionrole", "queue", "queueitem", "sharepointdocumentlocation", "sharepointsite", "syncerror",
            "duplicaterecord", "duplicaterule", "userentityinstancedata", "userentityuisettings", "mailboxtrackingfolder", "mailbox",
            "post", "postfollow", "postregarding", "postrole", "postcomment", "postlike", "principalsyncattributemap", "traceassociation",
            "traceregarding", "tracelog", "calendar", "calendarrule", "externalparty", "externalpartyitem", "fileattachment", "imagedescriptor",
            "import", "importdata", "importfile", "importlog", "importmap", "lookupmapping", "ownermapping", "columnmapping", "bulkoperation",
            "bulkoperationlog", "emailserverprofile", "processstage", "processtrigger", "teamtemplate", "role", "roletemplate",
            "fieldsecurityprofile", "usersettings", "exchangesyncidmapping", "convertrule", "routingrule", "solution", "solutioncomponent",
            "publisher", "pluginassembly", "plugintype", "plugintracelog", "sdkmessage", "sdkmessagefilter", "sdkmessageprocessingstep",
            "savedquery", "userquery", "systemform", "webresource", "sitemap", "appmodule", "customcontrol", "documenttemplate",
            "personaldocumenttemplate", "userform", "msdyn_aiconfiguration", "mailmergetemplate", "templateattachment", "template",
            "stagesolutionupload", "msdyn_solutioncomponentsummary", "recurrencerule", "emailsignature", "untrackedemail", "reportcategory", "report",
            "advancedsimilarityrule", "knowledgesearchmodel", "mobileofflineprofile",
            "newprocess", "translationprocess", "expiredprocess", "callbackregistration", "flowsession", "flowrun", "flowmachine", "flowlog",
            "msdyn_flow_approval", "msdyn_flow_approvalrequest", "msdyn_flow_approvalresponse", "msdyn_flow_approvalstep", "attachment",
            "datalakefolder", "serviceplan", "catalog", "catalogassignment", "connectionreference", "environmentvariabledefinition",
            "environmentvariablevalue", "msdyn_richtextfile", "settingdefinition", "organizationsetting", "appsetting"
        };

        public static bool IsSystem(TableInfo table) => table.IsPrivate || SystemTables.Contains(table.LogicalName);

        /// <summary>Tables that can never be nodes: private tables and N:N intersect tables (the N:N edge links both sides directly).</summary>
        private static bool CanBeNode(TableInfo table) => !table.IsPrivate && !table.IsIntersect;

        public static bool Allows(GraphFilter filter, RelationInfo relation)
        {
            if (relation.Kind == RelationKind.ManyToMany && !filter.ShowManyToMany) return false;
            if (relation.IsSelf && !filter.ShowSelfReferences) return false;
            return true;
        }

        public static Graph Build(OrgModel model, GraphFilter filter)
        {
            var universe = new HashSet<string>(
                model.Tables.Values
                    .Where(t => CanBeNode(t) && !filter.Excluded.Contains(t.LogicalName) && (!filter.HideSystemTables || !IsSystem(t)))
                    .Select(t => t.LogicalName),
                StringComparer.OrdinalIgnoreCase);

            var candidates = new HashSet<string>(
                universe.Select(model.Find).Where(t => Matches(t, filter)).Select(t => t.LogicalName),
                StringComparer.OrdinalIgnoreCase);

            var focus = model.Find(filter.Focus);
            var distances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> selected;

            if (focus != null && CanBeNode(focus))
            {
                // The focus table is always shown, even when the filters would exclude it.
                candidates.Add(focus.LogicalName);
                universe.Add(focus.LogicalName);
                selected = Neighborhood(model, filter, focus.LogicalName, Math.Max(1, filter.Depth), candidates, distances);
            }
            else
            {
                selected = new HashSet<string>(candidates, StringComparer.OrdinalIgnoreCase);
            }

            if (filter.HideIsolated)
            {
                selected.RemoveWhere(name =>
                    !string.Equals(name, focus?.LogicalName, StringComparison.OrdinalIgnoreCase) &&
                    !model.Find(name).Relations.Any(r => !r.IsSelf && Allows(filter, r) && selected.Contains(r.Other(name))));
            }

            var ghosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (filter.ShowBoundary)
            {
                foreach (var name in selected)
                {
                    foreach (var relation in model.Find(name).Relations)
                    {
                        var other = relation.Other(name);
                        if (Allows(filter, relation) && universe.Contains(other) && !selected.Contains(other)) ghosts.Add(other);
                    }
                }
            }

            var graph = new Graph { CandidateCount = candidates.Count };
            using (var measure = new NodeMeasurer())
            {
                foreach (var name in selected.Concat(ghosts).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    var node = new GraphNode(model.Find(name))
                    {
                        IsGhost = ghosts.Contains(name),
                        IsFocus = focus != null && string.Equals(name, focus.LogicalName, StringComparison.OrdinalIgnoreCase),
                        Distance = distances.TryGetValue(name, out var d) ? d : -1
                    };
                    measure.Fit(node);
                    graph.Nodes.Add(node);
                    graph.ByName[name] = node;
                }
            }

            foreach (var relation in model.Relations)
            {
                if (!Allows(filter, relation)) continue;
                var source = graph.Find(relation.From);
                var target = graph.Find(relation.To);
                if (source == null || target == null || (source.IsGhost && target.IsGhost)) continue;
                graph.Edges.Add(new GraphEdge(relation, source, target));
            }
            AssignParallelIndexes(graph);
            return graph;
        }

        private static bool Matches(TableInfo table, GraphFilter filter)
        {
            if (filter.SolutionTables != null && !filter.SolutionTables.Contains(table.MetadataId)) return false;
            if (filter.AppTables != null && !filter.AppTables.Contains(table.MetadataId)) return false;
            if (filter.OnlyUsedTables && table.HasActiveRecords == false) return false;
            if (!string.IsNullOrEmpty(filter.Prefix) && !string.Equals(table.Prefix, filter.Prefix, StringComparison.OrdinalIgnoreCase)) return false;
            switch (filter.Kind)
            {
                case TableKindFilter.CustomOnly: return table.IsCustom;
                case TableKindFilter.StandardOnly: return !table.IsCustom;
                default: return true;
            }
        }

        /// <summary>Breadth-first walk from the focus table, through the allowed relationships, staying inside <paramref name="allowed"/>.</summary>
        private static HashSet<string> Neighborhood(OrgModel model, GraphFilter filter, string start, int depth, HashSet<string> allowed, Dictionary<string, int> distances)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start };
            distances[start] = 0;
            var frontier = new List<string> { start };
            for (var level = 1; level <= depth && frontier.Count > 0; level++)
            {
                var next = new List<string>();
                foreach (var name in frontier)
                {
                    foreach (var relation in model.Find(name).Relations)
                    {
                        if (!Allows(filter, relation)) continue;
                        var other = relation.Other(name);
                        if (!allowed.Contains(other) || !result.Add(other)) continue;
                        distances[other] = level;
                        next.Add(other);
                    }
                }
                frontier = next;
            }
            return result;
        }

        /// <summary>Ranks the edges linking the same two nodes so that they fan out.</summary>
        public static void AssignParallelIndexes(Graph graph)
        {
            var groups = graph.Edges.GroupBy(e =>
            {
                var a = e.Source.Key;
                var b = e.Target.Key;
                return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
            });
            foreach (var group in groups)
            {
                var index = 0;
                var edges = group.OrderBy(e => e.Kind).ThenBy(e => e.Relation?.Kind).ThenBy(e => e.Relation?.SchemaName ?? e.Label, StringComparer.Ordinal).ToList();
                foreach (var edge in edges)
                {
                    edge.ParallelIndex = index++;
                    edge.ParallelCount = edges.Count;
                }
            }
        }
    }
}
