using System;
using System.Collections.Generic;
using System.Linq;
using DataverseCartographer.Localization;
using DataverseCartographer.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Metadata.Query;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseCartographer.Services
{
    /// <summary>
    /// Reads the data model of the organization: tables and relationships (one light query for the whole org),
    /// solutions and publishers, then columns and keys of a single table on demand.
    /// </summary>
    public sealed class MetadataService
    {
        private const int EntityComponentType = 1;

        private static readonly string[] EntityProperties =
        {
            "MetadataId", "LogicalName", "SchemaName", "DisplayName", "DisplayCollectionName", "Description", "ObjectTypeCode", "EntitySetName",
            "IsCustomEntity", "IsManaged", "IsIntersect", "IsActivity", "IsPrivate", "OwnershipType", "PrimaryIdAttribute",
            "PrimaryNameAttribute", "OneToManyRelationships", "ManyToManyRelationships"
        };

        private static readonly string[] RelationshipProperties =
        {
            "MetadataId", "SchemaName", "RelationshipType", "IsCustomRelationship", "IsManaged", "ReferencedEntity", "ReferencedAttribute",
            "ReferencingEntity", "ReferencingAttribute", "CascadeConfiguration", "Entity1LogicalName", "Entity2LogicalName", "IntersectEntityName"
        };

        private readonly IOrganizationService _service;

        public MetadataService(IOrganizationService service)
        {
            _service = service;
        }

        /// <summary>Warning raised when the light query failed and the full metadata had to be downloaded.</summary>
        public string Warning { get; private set; }

        #region Organization model

        public OrgModel LoadModel(Action<string> progress)
        {
            progress(Strings.ReadingTables);
            EntityMetadata[] entities;
            try
            {
                entities = RetrieveLight();
            }
            catch (Exception error)
            {
                // Some (old or locked down) environments reject RetrieveMetadataChanges: fall back to the full download.
                Warning = error.Message;
                progress(Strings.ReadingTablesFallback);
                entities = RetrieveFull();
            }

            var model = new OrgModel();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in entities)
            {
                if (string.IsNullOrEmpty(entity.LogicalName)) continue;
                model.Tables[entity.LogicalName] = ToTable(entity);

                foreach (var relation in entity.OneToManyRelationships ?? new OneToManyRelationshipMetadata[0])
                {
                    if (relation.SchemaName != null && seen.Add(relation.SchemaName)) model.Relations.Add(ToRelation(relation));
                }
                foreach (var relation in entity.ManyToManyRelationships ?? new ManyToManyRelationshipMetadata[0])
                {
                    if (relation.SchemaName != null && seen.Add(relation.SchemaName)) model.Relations.Add(ToRelation(relation));
                }
            }
            model.Index();

            // Solutions and publishers only feed the filters: a missing privilege must not prevent drawing the map.
            progress(Strings.ReadingSolutions);
            try
            {
                model.Solutions = LoadSolutions();
                model.Publishers = LoadPublishers();
            }
            catch (Exception error)
            {
                Warning = (Warning == null ? string.Empty : Warning + " | ") + "Solutions: " + error.Message;
            }
            return model;
        }

        private EntityMetadata[] RetrieveLight()
        {
            var query = new EntityQueryExpression
            {
                Properties = new MetadataPropertiesExpression(EntityProperties),
                RelationshipQuery = new RelationshipQueryExpression { Properties = new MetadataPropertiesExpression(RelationshipProperties) }
            };
            var response = (RetrieveMetadataChangesResponse)_service.Execute(new RetrieveMetadataChangesRequest { Query = query });
            return response.EntityMetadata.ToArray();
        }

        private EntityMetadata[] RetrieveFull()
        {
            var response = (RetrieveAllEntitiesResponse)_service.Execute(new RetrieveAllEntitiesRequest
            {
                EntityFilters = EntityFilters.Entity | EntityFilters.Relationships,
                RetrieveAsIfPublished = false
            });
            return response.EntityMetadata;
        }

        private static TableInfo ToTable(EntityMetadata entity)
        {
            var table = new TableInfo
            {
                MetadataId = entity.MetadataId ?? Guid.Empty,
                LogicalName = entity.LogicalName,
                SchemaName = entity.SchemaName ?? entity.LogicalName,
                DisplayName = Text(entity.DisplayName) ?? entity.LogicalName,
                CollectionName = Text(entity.DisplayCollectionName),
                EntitySetName = entity.EntitySetName,
                Description = Text(entity.Description),
                ObjectTypeCode = entity.ObjectTypeCode,
                IsCustom = entity.IsCustomEntity ?? false,
                IsManaged = entity.IsManaged ?? false,
                IsIntersect = entity.IsIntersect ?? false,
                IsActivity = entity.IsActivity ?? false,
                IsPrivate = entity.IsPrivate ?? false,
                Ownership = entity.OwnershipType?.ToString(),
                PrimaryIdAttribute = entity.PrimaryIdAttribute,
                PrimaryNameAttribute = entity.PrimaryNameAttribute
            };

            var separator = table.SchemaName.IndexOf('_');
            if (table.IsCustom && separator > 0) table.Prefix = table.SchemaName.Substring(0, separator).ToLowerInvariant();
            return table;
        }

        private static RelationInfo ToRelation(OneToManyRelationshipMetadata relation)
        {
            var cascade = relation.CascadeConfiguration;
            return new RelationInfo
            {
                MetadataId = relation.MetadataId ?? Guid.Empty,
                SchemaName = relation.SchemaName,
                Kind = RelationKind.OneToMany,
                From = relation.ReferencedEntity,
                To = relation.ReferencingEntity,
                FromAttribute = relation.ReferencedAttribute,
                ToAttribute = relation.ReferencingAttribute,
                IsCustom = relation.IsCustomRelationship ?? false,
                IsManaged = relation.IsManaged ?? false,
                Cascade = cascade == null ? null : new CascadeInfo
                {
                    Assign = cascade.Assign?.ToString(),
                    Delete = cascade.Delete?.ToString(),
                    Merge = cascade.Merge?.ToString(),
                    Reparent = cascade.Reparent?.ToString(),
                    Share = cascade.Share?.ToString(),
                    Unshare = cascade.Unshare?.ToString(),
                    RollupView = cascade.RollupView?.ToString()
                }
            };
        }

        private static RelationInfo ToRelation(ManyToManyRelationshipMetadata relation) => new RelationInfo
        {
            MetadataId = relation.MetadataId ?? Guid.Empty,
            SchemaName = relation.SchemaName,
            Kind = RelationKind.ManyToMany,
            From = relation.Entity1LogicalName,
            To = relation.Entity2LogicalName,
            Intersect = relation.IntersectEntityName,
            IsCustom = relation.IsCustomRelationship ?? false,
            IsManaged = relation.IsManaged ?? false
        };

        #endregion

        #region Solutions / publishers

        private List<SolutionItem> LoadSolutions()
        {
            var query = new QueryExpression("solution")
            {
                ColumnSet = new ColumnSet("friendlyname", "uniquename", "version", "ismanaged"),
                Criteria = { Conditions = { new ConditionExpression("isvisible", ConditionOperator.Equal, true) } },
                Orders = { new OrderExpression("friendlyname", OrderType.Ascending) }
            };
            var publisher = query.AddLink("publisher", "publisherid", "publisherid", JoinOperator.LeftOuter);
            publisher.EntityAlias = "pub";
            publisher.Columns = new ColumnSet("friendlyname", "customizationprefix");

            return RetrieveAll(query).Select(e => new SolutionItem
            {
                Id = e.Id,
                FriendlyName = e.GetAttributeValue<string>("friendlyname"),
                UniqueName = e.GetAttributeValue<string>("uniquename"),
                Version = e.GetAttributeValue<string>("version"),
                IsManaged = e.GetAttributeValue<bool>("ismanaged"),
                PublisherName = Aliased(e, "pub.friendlyname"),
                PublisherPrefix = Aliased(e, "pub.customizationprefix")
            }).ToList();
        }

        private Dictionary<string, string> LoadPublishers()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var query = new QueryExpression("publisher") { ColumnSet = new ColumnSet("friendlyname", "customizationprefix") };
            foreach (var publisher in RetrieveAll(query))
            {
                var prefix = publisher.GetAttributeValue<string>("customizationprefix");
                if (!string.IsNullOrEmpty(prefix) && !result.ContainsKey(prefix)) result[prefix] = publisher.GetAttributeValue<string>("friendlyname");
            }
            return result;
        }

        /// <summary>Metadata ids of the tables included in a solution (entity components).</summary>
        public HashSet<Guid> LoadSolutionTables(Guid solutionId)
        {
            var query = new QueryExpression("solutioncomponent")
            {
                ColumnSet = new ColumnSet("objectid"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("solutionid", ConditionOperator.Equal, solutionId),
                        new ConditionExpression("componenttype", ConditionOperator.Equal, EntityComponentType)
                    }
                }
            };
            return new HashSet<Guid>(RetrieveAll(query).Select(e => e.GetAttributeValue<Guid>("objectid")));
        }

        #endregion

        #region Usage

        private const int UsageBatchSize = 200;

        /// <summary>
        /// Reads which tables are really used: the approximate record count of each table (platform snapshot), then, for every table,
        /// whether it holds at least one active record (a 1-row query, sent by batches through ExecuteMultiple).
        /// Activities count any record (a completed email is still data); tables without status count any record too.
        /// </summary>
        public void LoadUsage(OrgModel model, Action<string> progress)
        {
            var tables = model.Tables.Values.Where(t => !t.IsPrivate && !t.IsIntersect).OrderBy(t => t.LogicalName).ToList();

            progress(Strings.ReadingRecordCounts);
            for (var i = 0; i < tables.Count; i += 100)
            {
                var chunk = tables.Skip(i).Take(100).ToList();
                try
                {
                    var request = new OrganizationRequest("RetrieveTotalRecordCount") { ["EntityNames"] = chunk.Select(t => t.LogicalName).ToArray() };
                    var response = _service.Execute(request);
                    if (response.Results.TryGetValue("EntityRecordCountCollection", out var counts) && counts is System.Collections.Generic.IEnumerable<KeyValuePair<string, long>> pairs)
                    {
                        foreach (var pair in pairs) model.Find(pair.Key).RecordCount = pair.Value;
                    }
                }
                catch (Exception)
                {
                    // Snapshot counts are informative only (unavailable on old versions or for some tables).
                }
            }

            var pending = tables;
            for (var attempt = 0; attempt < 2 && pending.Count > 0; attempt++)
            {
                // First attempt filters on statecode = 0; tables without that column fail and are checked again without filter.
                var withStatus = attempt == 0;
                var retry = new List<TableInfo>();
                for (var i = 0; i < pending.Count; i += UsageBatchSize)
                {
                    var batch = pending.Skip(i).Take(UsageBatchSize).ToList();
                    progress(Strings.CheckingUsage(Math.Min(i + batch.Count, pending.Count), pending.Count));
                    var execute = new ExecuteMultipleRequest
                    {
                        Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = true },
                        Requests = new OrganizationRequestCollection()
                    };
                    foreach (var table in batch)
                    {
                        var query = new QueryExpression(table.LogicalName) { ColumnSet = new ColumnSet(false), TopCount = 1, NoLock = true };
                        if (withStatus && !table.IsActivity) query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                        execute.Requests.Add(new RetrieveMultipleRequest { Query = query });
                    }

                    ExecuteMultipleResponse response;
                    try
                    {
                        response = (ExecuteMultipleResponse)_service.Execute(execute);
                    }
                    catch (Exception)
                    {
                        continue; // usage stays unknown for this batch: the tables are kept visible
                    }
                    foreach (var item in response.Responses)
                    {
                        var table = batch[item.RequestIndex];
                        if (item.Fault == null && item.Response is RetrieveMultipleResponse result)
                        {
                            table.HasActiveRecords = result.EntityCollection.Entities.Count > 0;
                        }
                        else if (withStatus && !table.IsActivity)
                        {
                            retry.Add(table);
                        }
                    }
                }
                pending = retry;
            }
            model.UsageLoaded = true;
        }

        #endregion

        #region Table details

        /// <summary>Columns and alternate keys of one table.</summary>
        public TableDetails LoadDetails(TableInfo table)
        {
            var response = (RetrieveEntityResponse)_service.Execute(new RetrieveEntityRequest
            {
                LogicalName = table.LogicalName,
                EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
                RetrieveAsIfPublished = false
            });
            var entity = response.EntityMetadata;

            var details = new TableDetails();
            foreach (var attribute in entity.Attributes ?? new AttributeMetadata[0])
            {
                // Child columns (owneridname, createdbyyominame…) duplicate their parent lookup.
                if (!string.IsNullOrEmpty(attribute.AttributeOf)) continue;

                details.Columns.Add(new ColumnInfo
                {
                    LogicalName = attribute.LogicalName,
                    SchemaName = attribute.SchemaName,
                    DisplayName = Text(attribute.DisplayName) ?? attribute.LogicalName,
                    Type = TypeName(attribute),
                    RequiredLevel = attribute.RequiredLevel?.Value.ToString(),
                    IsCustom = attribute.IsCustomAttribute ?? false,
                    IsPrimaryId = attribute.IsPrimaryId ?? false,
                    IsPrimaryName = attribute.IsPrimaryName ?? false,
                    Targets = attribute is LookupAttributeMetadata lookup && lookup.Targets != null ? string.Join(", ", lookup.Targets) : null,
                    Description = Text(attribute.Description),
                    MaxLength = (attribute as StringAttributeMetadata)?.MaxLength ?? (attribute as MemoAttributeMetadata)?.MaxLength
                });
            }
            details.Columns = details.Columns
                .OrderByDescending(c => c.IsPrimaryId).ThenByDescending(c => c.IsPrimaryName).ThenBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var key in entity.Keys ?? new EntityKeyMetadata[0])
            {
                details.Keys.Add(new KeyInfo
                {
                    SchemaName = key.SchemaName ?? key.LogicalName,
                    DisplayName = Text(key.DisplayName) ?? key.LogicalName,
                    Columns = key.KeyAttributes ?? new string[0],
                    Status = key.EntityKeyIndexStatus.ToString()
                });
            }
            return details;
        }

        private static string TypeName(AttributeMetadata attribute)
        {
            var name = attribute.AttributeTypeName?.Value;
            if (!string.IsNullOrEmpty(name)) return name.EndsWith("Type", StringComparison.Ordinal) ? name.Substring(0, name.Length - 4) : name;
            return attribute.AttributeType?.ToString() ?? "?";
        }

        #endregion

        #region Helpers

        private IEnumerable<Entity> RetrieveAll(QueryExpression query)
        {
            query.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
            while (true)
            {
                var page = _service.RetrieveMultiple(query);
                foreach (var entity in page.Entities) yield return entity;
                if (!page.MoreRecords) yield break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }

        private static string Aliased(Entity entity, string name) =>
            entity.GetAttributeValue<AliasedValue>(name)?.Value as string;

        private static string Text(Label label)
        {
            var text = label?.UserLocalizedLabel?.Label ?? label?.LocalizedLabels?.FirstOrDefault()?.Label;
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        #endregion
    }
}
