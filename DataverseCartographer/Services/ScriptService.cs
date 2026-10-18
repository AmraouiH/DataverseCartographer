using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using DataverseCartographer.Localization;
using DataverseCartographer.Models;
using DataverseCartographer.Services.Analysis;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseCartographer.Services
{
    /// <summary>
    /// Reads the JavaScript side of the organization: forms with their libraries and event handlers (every table),
    /// the web resources they load, and the content of the scripts to analyze.
    /// </summary>
    public sealed class ScriptService
    {
        private const int SystemFormComponent = 60;
        private const int WebResourceComponent = 61;

        /// <summary>Main, quick view, quick create, card and main interactive forms.</summary>
        private static readonly object[] FormTypes = { 2, 6, 7, 11, 12 };

        private static readonly string[] MicrosoftPrefixes =
        {
            "msdyn", "mscrm", "msdynce", "adx_", "mspp_", "msfp_", "msevtmgt", "Main_system_library", "Activities/", "CRM/", "ClientGlobalContext", "cc_", "msdyusd"
        };

        private readonly IOrganizationService _service;

        public ScriptService(IOrganizationService service)
        {
            _service = service;
        }

        public ScriptModel LoadModel(Action<string> progress)
        {
            progress(Strings.ReadingForms);
            var model = new ScriptModel();
            foreach (var row in RetrieveForms())
            {
                var form = new FormInfo
                {
                    Id = row.Id,
                    Name = row.GetAttributeValue<string>("name"),
                    Entity = row.GetAttributeValue<string>("objecttypecode"),
                    Type = row.GetAttributeValue<OptionSetValue>("type")?.Value ?? 0,
                    Active = (row.GetAttributeValue<OptionSetValue>("formactivationstate")?.Value ?? 1) == 1,
                    IsDefault = row.GetAttributeValue<bool>("isdefault")
                };
                PluginService.ParseFormXml(form, row.GetAttributeValue<string>("formxml"));
                if (form.Libraries.Count == 0 && form.Handlers.Count == 0) continue;
                model.Forms.Add(form);

                foreach (var name in form.Libraries.Concat(form.Handlers.Select(h => h.Library)).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!model.Libraries.TryGetValue(name, out var library))
                    {
                        model.Libraries[name] = library = new ScriptLibraryInfo { Name = name, IsMicrosoft = IsMicrosoftName(name) };
                    }
                    library.Forms.Add(form);
                    library.Handlers.AddRange(form.Handlers.Where(h => string.Equals(h.Library, name, StringComparison.OrdinalIgnoreCase)));
                }
            }

            progress(Strings.ReadingWebResources);
            var names = model.Libraries.Keys.ToList();
            for (var i = 0; i < names.Count; i += 200)
            {
                var query = new QueryExpression("webresource")
                {
                    ColumnSet = new ColumnSet("name", "displayname", "ismanaged"),
                    Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.In, names.Skip(i).Take(200).Cast<object>().ToArray()) } }
                };
                foreach (var row in RetrieveAll(query))
                {
                    if (!model.Libraries.TryGetValue(row.GetAttributeValue<string>("name") ?? string.Empty, out var library)) continue;
                    library.Id = row.Id;
                    library.DisplayName = row.GetAttributeValue<string>("displayname");
                    library.IsManaged = row.GetAttributeValue<bool>("ismanaged");
                }
            }
            return model;
        }

        public static bool IsMicrosoftName(string name) =>
            MicrosoftPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) || name.StartsWith("/", StringComparison.Ordinal);

        /// <summary>Forms that load at least one library (filtered on the server when the organization allows it).</summary>
        private IEnumerable<Entity> RetrieveForms()
        {
            QueryExpression Query(bool filtered)
            {
                var query = new QueryExpression("systemform")
                {
                    ColumnSet = new ColumnSet("name", "objecttypecode", "type", "formactivationstate", "isdefault", "formxml"),
                    Criteria = { Conditions = { new ConditionExpression("type", ConditionOperator.In, FormTypes) } }
                };
                if (filtered) query.Criteria.AddCondition("formxml", ConditionOperator.Like, "%<Library %");
                return query;
            }

            List<Entity> rows;
            try
            {
                rows = RetrieveAll(Query(true), 250).ToList();
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                rows = RetrieveAll(Query(false), 250).ToList();
            }
            return rows;
        }

        /// <summary>Forms and web resources of a solution.</summary>
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
                        new ConditionExpression("componenttype", ConditionOperator.In, SystemFormComponent, WebResourceComponent)
                    }
                }
            };
            return new HashSet<Guid>(RetrieveAll(query).Select(e => e.GetAttributeValue<Guid>("objectid")));
        }

        /// <summary>Downloads and analyzes the given libraries (once each).</summary>
        public void AnalyzeLibraries(IList<ScriptLibraryInfo> libraries, ICollection<string> tables, IDictionary<string, string> entitySets, Action<string> progress)
        {
            for (var i = 0; i < libraries.Count; i++)
            {
                var library = libraries[i];
                if (library.Analyzed) continue;
                progress(Strings.AnalyzingScript(library.Name, i + 1, libraries.Count));
                try
                {
                    if (library.Id == null)
                    {
                        library.AnalysisNote = Strings.ScriptNotFound;
                    }
                    else
                    {
                        var row = _service.Retrieve("webresource", library.Id.Value, new ColumnSet("content"));
                        var content = row.GetAttributeValue<string>("content");
                        var script = string.IsNullOrEmpty(content) ? string.Empty : Decode(Convert.FromBase64String(content));
                        library.Calls = ScriptAnalyzer.Analyze(script, tables, entitySets);
                        library.AnalysisNote = Strings.ScriptAnalysisSummary(library.Calls.Count(c => c.Entity != null));
                    }
                }
                catch (Exception error)
                {
                    library.AnalysisNote = Strings.AnalysisError(error.Message);
                }
                library.Analyzed = true;
            }
        }

        private static string Decode(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            return Encoding.UTF8.GetString(bytes);
        }

        private IEnumerable<Entity> RetrieveAll(QueryExpression query, int pageSize = 5000)
        {
            query.PageInfo = new PagingInfo { Count = pageSize, PageNumber = 1 };
            while (true)
            {
                var page = _service.RetrieveMultiple(query);
                foreach (var entity in page.Entities) yield return entity;
                if (!page.MoreRecords) yield break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }
    }
}
