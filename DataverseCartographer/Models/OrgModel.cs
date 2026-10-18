using System;
using System.Collections.Generic;
using System.Linq;

namespace DataverseCartographer.Models
{
    public enum RelationKind
    {
        OneToMany,
        ManyToMany
    }

    /// <summary>
    /// A Dataverse table, with the light metadata loaded for the whole organization.
    /// Columns and keys are loaded on demand (<see cref="Details"/>) when the table card is opened.
    /// </summary>
    public sealed class TableInfo
    {
        public Guid MetadataId { get; set; }
        public string LogicalName { get; set; }
        public string SchemaName { get; set; }
        public string DisplayName { get; set; }
        public string CollectionName { get; set; }

        /// <summary>Web API name (« accounts »), used to recognize the tables called by the scripts.</summary>
        public string EntitySetName { get; set; }
        public string Description { get; set; }
        public int? ObjectTypeCode { get; set; }
        public bool IsCustom { get; set; }
        public bool IsManaged { get; set; }
        public bool IsIntersect { get; set; }
        public bool IsActivity { get; set; }
        public bool IsPrivate { get; set; }
        public string Ownership { get; set; }
        public string PrimaryIdAttribute { get; set; }
        public string PrimaryNameAttribute { get; set; }

        /// <summary>Publisher prefix taken from the schema name of custom tables (« mso » for mso_project), empty otherwise.</summary>
        public string Prefix { get; set; } = string.Empty;

        /// <summary>Relationships of the table (both directions, self-references once).</summary>
        public List<RelationInfo> Relations { get; } = new List<RelationInfo>();

        /// <summary>At least one active record (statecode = 0, or any record for activities and tables without status); null = not checked or not readable.</summary>
        public bool? HasActiveRecords { get; set; }

        /// <summary>Approximate number of records (platform snapshot, refreshed about once a day), null = unknown.</summary>
        public long? RecordCount { get; set; }

        /// <summary>Columns and keys, null until loaded.</summary>
        public TableDetails Details { get; set; }

        /// <summary>Forms, scripts and plugin steps, null until loaded.</summary>
        public TableLogic Logic { get; set; }

        public string Label => string.IsNullOrEmpty(DisplayName) ? LogicalName : DisplayName;

        public override string ToString() => string.IsNullOrEmpty(DisplayName) || DisplayName == LogicalName ? LogicalName : $"{DisplayName} ({LogicalName})";
    }

    public sealed class CascadeInfo
    {
        public string Assign { get; set; }
        public string Delete { get; set; }
        public string Merge { get; set; }
        public string Reparent { get; set; }
        public string Share { get; set; }
        public string Unshare { get; set; }
        public string RollupView { get; set; }

        /// <summary>True when deleting the parent deletes the children (the risky case worth highlighting).</summary>
        public bool CascadesDelete => string.Equals(Delete, "Cascade", StringComparison.OrdinalIgnoreCase);

        public bool RestrictsDelete => string.Equals(Delete, "Restrict", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A relationship. For 1:N, <see cref="From"/> is the referenced (one) side and <see cref="To"/> the referencing (many) side
    /// holding the lookup <see cref="ToAttribute"/>. For N:N, From/To are Entity1/Entity2 and <see cref="Intersect"/> is the hidden table.
    /// </summary>
    public sealed class RelationInfo
    {
        public Guid MetadataId { get; set; }
        public string SchemaName { get; set; }
        public RelationKind Kind { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string FromAttribute { get; set; }
        public string ToAttribute { get; set; }
        public string Intersect { get; set; }
        public bool IsCustom { get; set; }
        public bool IsManaged { get; set; }
        public CascadeInfo Cascade { get; set; }

        public bool IsSelf => string.Equals(From, To, StringComparison.OrdinalIgnoreCase);

        public string Other(string table) => string.Equals(table, From, StringComparison.OrdinalIgnoreCase) ? To : From;

        /// <summary>Cardinality seen from <paramref name="table"/>: 1:N when it is the referenced side, N:1 when it holds the lookup.</summary>
        public string KindFrom(string table)
        {
            if (Kind == RelationKind.ManyToMany) return "N:N";
            if (IsSelf) return "1:N";
            return string.Equals(table, From, StringComparison.OrdinalIgnoreCase) ? "1:N" : "N:1";
        }

        public string KindLabel => Kind == RelationKind.ManyToMany ? "N:N" : "1:N";
    }

    public sealed class ColumnInfo
    {
        public string LogicalName { get; set; }
        public string SchemaName { get; set; }
        public string DisplayName { get; set; }
        public string Type { get; set; }
        public string RequiredLevel { get; set; }
        public bool IsCustom { get; set; }
        public bool IsPrimaryId { get; set; }
        public bool IsPrimaryName { get; set; }
        public string Targets { get; set; }
        public string Description { get; set; }
        public int? MaxLength { get; set; }

        public bool IsRequired => RequiredLevel == "ApplicationRequired" || RequiredLevel == "SystemRequired";
    }

    public sealed class KeyInfo
    {
        public string SchemaName { get; set; }
        public string DisplayName { get; set; }
        public string[] Columns { get; set; } = new string[0];
        public string Status { get; set; }
    }

    public sealed class TableDetails
    {
        public List<ColumnInfo> Columns { get; set; } = new List<ColumnInfo>();
        public List<KeyInfo> Keys { get; set; } = new List<KeyInfo>();
    }

    public sealed class SolutionItem
    {
        public Guid Id { get; set; }
        public string UniqueName { get; set; }
        public string FriendlyName { get; set; }
        public string Version { get; set; }
        public bool IsManaged { get; set; }
        public string PublisherName { get; set; }
        public string PublisherPrefix { get; set; }

        public override string ToString() => $"{FriendlyName} ({Version}){(IsManaged ? " · managed" : string.Empty)}";
    }

    /// <summary>A model-driven app (appmodule).</summary>
    public sealed class AppItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string UniqueName { get; set; }
        public bool IsManaged { get; set; }

        public override string ToString() => IsManaged ? $"{Name} · managed" : Name;
    }

    /// <summary>
    /// Everything loaded for one organization: tables, relationships, solutions and publishers.
    /// </summary>
    public sealed class OrgModel
    {
        public Dictionary<string, TableInfo> Tables { get; } = new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
        public List<RelationInfo> Relations { get; } = new List<RelationInfo>();
        public List<SolutionItem> Solutions { get; set; } = new List<SolutionItem>();
        public List<AppItem> Apps { get; set; } = new List<AppItem>();

        /// <summary>Tables of each model-driven app (metadata ids), cached once read.</summary>
        public Dictionary<Guid, HashSet<Guid>> AppTables { get; } = new Dictionary<Guid, HashSet<Guid>>();

        /// <summary>Publisher friendly name by customization prefix.</summary>
        public Dictionary<string, string> Publishers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Tables of each solution (metadata ids of the entity components), cached once read.</summary>
        public Dictionary<Guid, HashSet<Guid>> SolutionTables { get; } = new Dictionary<Guid, HashSet<Guid>>();

        /// <summary>The usage of the tables (active records) has been read.</summary>
        public bool UsageLoaded { get; set; }

        public TableInfo Find(string logicalName) =>
            logicalName != null && Tables.TryGetValue(logicalName, out var table) ? table : null;

        /// <summary>Prefixes actually used by custom tables, most used first.</summary>
        public List<string> UsedPrefixes() => Tables.Values
            .Where(t => t.IsCustom && !string.IsNullOrEmpty(t.Prefix))
            .GroupBy(t => t.Prefix, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => g.Key)
            .ToList();

        /// <summary>Table logical name by Web API entity set name.</summary>
        public Dictionary<string, string> EntitySets() => Tables.Values
            .Where(t => !string.IsNullOrEmpty(t.EntitySetName))
            .GroupBy(t => t.EntitySetName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().LogicalName, StringComparer.OrdinalIgnoreCase);

        /// <summary>Links every relationship to its tables (call once after loading).</summary>
        public void Index()
        {
            foreach (var table in Tables.Values) table.Relations.Clear();
            foreach (var relation in Relations)
            {
                Find(relation.From)?.Relations.Add(relation);
                if (!relation.IsSelf) Find(relation.To)?.Relations.Add(relation);
            }
        }
    }
}
