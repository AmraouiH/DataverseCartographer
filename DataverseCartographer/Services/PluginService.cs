using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.ServiceModel;
using System.Xml.Linq;
using DataverseCartographer.Localization;
using DataverseCartographer.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseCartographer.Services
{
    /// <summary>
    /// Reads the « logic » of the organization: plugin assemblies, types, steps and images, the binaries to analyze,
    /// and the forms (with their JavaScript libraries and event handlers) of a table.
    /// </summary>
    public sealed class PluginService
    {
        private const int PluginTypeComponent = 90;
        private const int PluginAssemblyComponent = 91;
        private const int StepComponent = 92;

        private readonly IOrganizationService _service;

        public PluginService(IOrganizationService service)
        {
            _service = service;
        }

        #region Registrations

        public PluginModel LoadModel(Action<string> progress)
        {
            var model = new PluginModel();

            progress(Strings.ReadingAssemblies);
            model.Assemblies = LoadAssemblies();
            var assemblies = model.Assemblies.ToDictionary(a => a.Id);

            progress(Strings.ReadingPluginTypes);
            var typeQuery = new QueryExpression("plugintype")
            {
                ColumnSet = new ColumnSet("typename", "friendlyname", "name", "pluginassemblyid", "assemblyname", "isworkflowactivity")
            };
            model.Types = RetrieveAll(typeQuery).Select(e =>
            {
                var assemblyId = e.GetAttributeValue<EntityReference>("pluginassemblyid")?.Id ?? Guid.Empty;
                return new PluginTypeInfo
                {
                    Id = e.Id,
                    TypeName = e.GetAttributeValue<string>("typename") ?? e.GetAttributeValue<string>("name"),
                    FriendlyName = e.GetAttributeValue<string>("friendlyname"),
                    AssemblyId = assemblyId,
                    AssemblyName = e.GetAttributeValue<string>("assemblyname") ?? (assemblies.TryGetValue(assemblyId, out var a) ? a.Name : null),
                    IsWorkflowActivity = e.GetAttributeValue<bool>("isworkflowactivity")
                };
            }).ToList();

            progress(Strings.ReadingSteps);
            model.Steps = LoadSteps(null);
            LoadImages(model.Steps);

            // An assembly whose steps are all « system » is a platform assembly, whatever its name.
            var types = model.Types.ToDictionary(t => t.Id);
            foreach (var group in model.Steps.Where(s => s.PluginTypeId.HasValue && types.ContainsKey(s.PluginTypeId.Value)).GroupBy(s => types[s.PluginTypeId.Value].AssemblyId))
            {
                if (assemblies.TryGetValue(group.Key, out var assembly) && group.All(s => s.CustomizationLevel == 0)) assembly.IsMicrosoft = true;
            }
            return model;
        }

        private List<PluginAssemblyInfo> LoadAssemblies()
        {
            List<Entity> rows;
            try
            {
                rows = RetrieveAll(new QueryExpression("pluginassembly")
                {
                    ColumnSet = new ColumnSet("name", "version", "isolationmode", "sourcetype", "ismanaged", "packageid")
                }).ToList();
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                // Organizations without plugin packages have no packageid column.
                rows = RetrieveAll(new QueryExpression("pluginassembly")
                {
                    ColumnSet = new ColumnSet("name", "version", "isolationmode", "sourcetype", "ismanaged")
                }).ToList();
            }

            return rows.Select(e =>
            {
                var name = e.GetAttributeValue<string>("name") ?? "?";
                return new PluginAssemblyInfo
                {
                    Id = e.Id,
                    Name = name,
                    Version = e.GetAttributeValue<string>("version"),
                    IsolationMode = e.GetAttributeValue<OptionSetValue>("isolationmode")?.Value ?? 0,
                    SourceType = e.GetAttributeValue<OptionSetValue>("sourcetype")?.Value ?? 0,
                    IsManaged = e.GetAttributeValue<bool>("ismanaged"),
                    PackageId = e.GetAttributeValue<EntityReference>("packageid")?.Id,
                    IsMicrosoft = IsPlatformName(name)
                };
            }).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static bool IsPlatformName(string name) =>
            name != null && (name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) || name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                             || name.StartsWith("Microsoft", StringComparison.Ordinal) || name.StartsWith("ActivityFeeds", StringComparison.OrdinalIgnoreCase));

        /// <summary>Steps of the organization, or of one table.</summary>
        public List<StepInfo> LoadSteps(string entity)
        {
            var query = new QueryExpression("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet("name", "stage", "mode", "rank", "statecode", "filteringattributes", "description", "customizationlevel", "eventhandler", "plugintypeid")
            };
            var message = query.AddLink("sdkmessage", "sdkmessageid", "sdkmessageid", JoinOperator.Inner);
            message.EntityAlias = "m";
            message.Columns = new ColumnSet("name");
            var filter = query.AddLink("sdkmessagefilter", "sdkmessagefilterid", "sdkmessagefilterid", entity == null ? JoinOperator.LeftOuter : JoinOperator.Inner);
            filter.EntityAlias = "f";
            filter.Columns = new ColumnSet("primaryobjecttypecode");
            if (entity != null) filter.LinkCriteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, entity);

            return RetrieveAll(query).Select(e =>
            {
                var handler = e.GetAttributeValue<EntityReference>("eventhandler");
                var pluginType = e.GetAttributeValue<EntityReference>("plugintypeid");
                var table = Aliased(e, "f.primaryobjecttypecode");
                var step = new StepInfo
                {
                    Id = e.Id,
                    Name = e.GetAttributeValue<string>("name"),
                    Message = Aliased(e, "m.name") ?? "?",
                    Entity = string.IsNullOrEmpty(table) || table == "none" ? null : table,
                    Stage = e.GetAttributeValue<OptionSetValue>("stage")?.Value ?? 0,
                    Mode = e.GetAttributeValue<OptionSetValue>("mode")?.Value ?? 0,
                    Rank = e.GetAttributeValue<int>("rank"),
                    Enabled = (e.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0) == 0,
                    FilteringAttributes = e.GetAttributeValue<string>("filteringattributes"),
                    Description = e.GetAttributeValue<string>("description"),
                    CustomizationLevel = e.GetAttributeValue<int>("customizationlevel"),
                    HandlerName = handler?.Name ?? pluginType?.Name
                };
                if (handler != null && handler.LogicalName == "serviceendpoint") step.EndpointId = handler.Id;
                else step.PluginTypeId = handler?.Id ?? pluginType?.Id;
                return step;
            }).ToList();
        }

        private void LoadImages(List<StepInfo> steps)
        {
            var byId = steps.ToDictionary(s => s.Id);
            var query = new QueryExpression("sdkmessageprocessingstepimage")
            {
                ColumnSet = new ColumnSet("name", "entityalias", "imagetype", "attributes", "sdkmessageprocessingstepid")
            };
            foreach (var image in RetrieveAll(query))
            {
                var stepId = image.GetAttributeValue<EntityReference>("sdkmessageprocessingstepid")?.Id;
                if (stepId == null || !byId.TryGetValue(stepId.Value, out var step)) continue;
                step.Images.Add(new StepImageInfo
                {
                    Name = image.GetAttributeValue<string>("name"),
                    Alias = image.GetAttributeValue<string>("entityalias"),
                    ImageType = image.GetAttributeValue<OptionSetValue>("imagetype")?.Value ?? 0,
                    Attributes = image.GetAttributeValue<string>("attributes")
                });
            }
        }

        /// <summary>Ids of the plugin assemblies, types and steps of a solution.</summary>
        public HashSet<Guid> LoadSolutionComponents(Guid solutionId)
        {
            var query = new QueryExpression("solutioncomponent")
            {
                ColumnSet = new ColumnSet("objectid"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("solutionid", ConditionOperator.Equal, solutionId),
                        new ConditionExpression("componenttype", ConditionOperator.In, PluginTypeComponent, PluginAssemblyComponent, StepComponent)
                    }
                }
            };
            return new HashSet<Guid>(RetrieveAll(query).Select(e => e.GetAttributeValue<Guid>("objectid")));
        }

        #endregion

        #region Assembly binaries

        /// <summary>
        /// The DLL stored in the organization (database assemblies, or the .nupkg of a plugin package),
        /// plus the other DLLs of the package. Null when the assembly is on disk / GAC (on-premises).
        /// </summary>
        public byte[] LoadAssemblyContent(PluginAssemblyInfo assembly, out List<byte[]> dependencies, out string note)
        {
            dependencies = new List<byte[]>();
            note = null;

            var row = _service.Retrieve("pluginassembly", assembly.Id, new ColumnSet("content"));
            var content = row.GetAttributeValue<string>("content");
            if (!string.IsNullOrEmpty(content)) return Convert.FromBase64String(content);

            if (assembly.PackageId.HasValue)
            {
                var package = _service.Retrieve("pluginpackage", assembly.PackageId.Value, new ColumnSet("content"));
                var nupkg = package.GetAttributeValue<string>("content");
                if (string.IsNullOrEmpty(nupkg))
                {
                    note = Strings.AnalysisNoPackageContent;
                    return null;
                }
                using (var zip = new ZipArchive(new MemoryStream(Convert.FromBase64String(nupkg)), ZipArchiveMode.Read))
                {
                    byte[] main = null;
                    foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                    {
                        byte[] bytes;
                        using (var stream = entry.Open())
                        using (var memory = new MemoryStream())
                        {
                            stream.CopyTo(memory);
                            bytes = memory.ToArray();
                        }
                        if (main == null && string.Equals(Path.GetFileNameWithoutExtension(entry.Name), assembly.Name, StringComparison.OrdinalIgnoreCase)) main = bytes;
                        else dependencies.Add(bytes);
                    }
                    if (main == null) note = Strings.AnalysisNoPackageContent;
                    return main;
                }
            }

            note = assembly.SourceType == 1 || assembly.SourceType == 2 ? Strings.AnalysisOnDisk : Strings.AnalysisNoContent;
            return null;
        }

        /// <summary>
        /// Downloads and analyzes the code of the given assemblies (once each): fills the <see cref="PluginTypeInfo.Operations"/>
        /// of their plugin types and the <see cref="PluginAssemblyInfo.AnalysisNote"/>.
        /// </summary>
        public void AnalyzeAssemblies(PluginModel model, IList<PluginAssemblyInfo> assemblies, ICollection<string> entityNames, Action<string> progress)
        {
            for (var i = 0; i < assemblies.Count; i++)
            {
                var assembly = assemblies[i];
                if (assembly.Analyzed) continue;
                progress(Strings.AnalyzingAssembly(assembly.Name, i + 1, assemblies.Count));
                var types = model.Types.Where(t => t.AssemblyId == assembly.Id).ToList();
                try
                {
                    var content = LoadAssemblyContent(assembly, out _, out var note);
                    if (content == null)
                    {
                        assembly.AnalysisNote = note;
                    }
                    else
                    {
                        var analyzer = new Analysis.PluginCodeAnalyzer(content, entityNames);
                        var result = analyzer.Analyze(types.Select(t => t.TypeName));
                        foreach (var type in types) type.Operations = result.TryGetValue(type.TypeName ?? string.Empty, out var operations) ? operations : new List<CodeOperation>();
                        var count = types.Sum(t => t.Operations.Count(o => o.Entity != null));
                        assembly.AnalysisNote = Strings.AnalysisSummary(count, types.Count(t => t.Operations.Count > 0));
                    }
                }
                catch (Exception error)
                {
                    assembly.AnalysisNote = Strings.AnalysisError(error.Message);
                }
                assembly.Analyzed = true;
            }
        }

        #endregion

        #region Forms

        /// <summary>Forms of a table with their JavaScript libraries and event handlers, and the steps registered on it.</summary>
        public TableLogic LoadLogic(TableInfo table)
        {
            var logic = new TableLogic();
            var query = new QueryExpression("systemform")
            {
                ColumnSet = new ColumnSet("name", "type", "formactivationstate", "isdefault", "description", "formxml"),
                Criteria = { Conditions = { new ConditionExpression("objecttypecode", ConditionOperator.Equal, table.LogicalName) } },
                Orders = { new OrderExpression("type", OrderType.Ascending), new OrderExpression("name", OrderType.Ascending) }
            };
            foreach (var row in RetrieveAll(query))
            {
                var form = new FormInfo
                {
                    Id = row.Id,
                    Name = row.GetAttributeValue<string>("name"),
                    Type = row.GetAttributeValue<OptionSetValue>("type")?.Value ?? 0,
                    Active = (row.GetAttributeValue<OptionSetValue>("formactivationstate")?.Value ?? 1) == 1,
                    IsDefault = row.GetAttributeValue<bool>("isdefault"),
                    Description = row.GetAttributeValue<string>("description")
                };
                ParseFormXml(form, row.GetAttributeValue<string>("formxml"));
                logic.Forms.Add(form);
            }

            logic.Steps = LoadSteps(table.LogicalName)
                .OrderBy(s => s.Message).ThenBy(s => s.Stage).ThenBy(s => s.Rank)
                .ToList();
            var typeIds = logic.Steps.Where(s => s.PluginTypeId.HasValue).Select(s => s.PluginTypeId.Value).Distinct().ToList();
            if (typeIds.Count > 0)
            {
                var types = new QueryExpression("plugintype")
                {
                    ColumnSet = new ColumnSet("typename", "name", "assemblyname", "pluginassemblyid"),
                    Criteria = { Conditions = { new ConditionExpression("plugintypeid", ConditionOperator.In, typeIds.Cast<object>().ToArray()) } }
                };
                foreach (var row in RetrieveAll(types))
                {
                    logic.Types[row.Id] = new PluginTypeInfo
                    {
                        Id = row.Id,
                        TypeName = row.GetAttributeValue<string>("typename") ?? row.GetAttributeValue<string>("name"),
                        AssemblyName = row.GetAttributeValue<string>("assemblyname"),
                        AssemblyId = row.GetAttributeValue<EntityReference>("pluginassemblyid")?.Id ?? Guid.Empty
                    };
                }
            }
            return logic;
        }

        /// <summary>
        /// Reads the form libraries and every event handler of the form XML: form OnLoad / OnSave, column OnChange,
        /// tab state change, control events (sub-grids, iframes…).
        /// </summary>
        public static void ParseFormXml(FormInfo form, string formXml)
        {
            if (string.IsNullOrWhiteSpace(formXml)) return;
            XDocument document;
            try
            {
                document = XDocument.Parse(formXml);
            }
            catch (System.Xml.XmlException)
            {
                return;
            }

            foreach (var library in document.Descendants("Library"))
            {
                var name = (string)library.Attribute("name");
                if (!string.IsNullOrEmpty(name) && !form.Libraries.Contains(name)) form.Libraries.Add(name);
            }

            foreach (var evt in document.Descendants("event"))
            {
                var eventName = (string)evt.Attribute("name") ?? "?";
                var source = (string)evt.Attribute("attribute");
                if (string.IsNullOrEmpty(source))
                {
                    var tab = evt.Ancestors("tab").FirstOrDefault();
                    var control = evt.Ancestors("control").FirstOrDefault();
                    if (control != null) source = (string)control.Attribute("id") ?? (string)control.Attribute("datafieldname");
                    else if (tab != null) source = (string)tab.Attribute("name");
                }

                foreach (var handler in evt.Descendants("Handler"))
                {
                    form.Handlers.Add(new FormHandler
                    {
                        Form = form.Name,
                        Event = eventName,
                        Source = source ?? string.Empty,
                        Library = (string)handler.Attribute("libraryName"),
                        Function = (string)handler.Attribute("functionName"),
                        Enabled = !string.Equals((string)handler.Attribute("enabled"), "false", StringComparison.OrdinalIgnoreCase),
                        PassContext = string.Equals((string)handler.Attribute("passExecutionContext"), "true", StringComparison.OrdinalIgnoreCase)
                    });
                }
            }
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

        #endregion
    }
}
