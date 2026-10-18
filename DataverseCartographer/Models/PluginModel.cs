using System;
using System.Collections.Generic;
using System.Linq;

namespace DataverseCartographer.Models
{
    public sealed class PluginAssemblyInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }

        /// <summary>1 = none, 2 = sandbox.</summary>
        public int IsolationMode { get; set; }

        /// <summary>0 = database, 1 = disk, 2 = GAC, 3 = Azure web app, 4 = file store (plugin package).</summary>
        public int SourceType { get; set; }

        public Guid? PackageId { get; set; }
        public bool IsManaged { get; set; }

        /// <summary>Microsoft / platform assembly (hidden by default).</summary>
        public bool IsMicrosoft { get; set; }

        public bool Analyzed { get; set; }

        /// <summary>Why the code could not be analyzed (or a summary of the analysis).</summary>
        public string AnalysisNote { get; set; }

        public override string ToString() => $"{Name} ({Version})";
    }

    public sealed class PluginTypeInfo
    {
        public Guid Id { get; set; }
        public string TypeName { get; set; }
        public string FriendlyName { get; set; }
        public Guid AssemblyId { get; set; }
        public string AssemblyName { get; set; }
        public bool IsWorkflowActivity { get; set; }

        /// <summary>What the code does on the tables, found by the static analysis of the assembly (null = not analyzed).</summary>
        public List<CodeOperation> Operations { get; set; }

        public string ShortName
        {
            get
            {
                var name = TypeName ?? FriendlyName ?? "?";
                var index = Math.Max(name.LastIndexOf('.'), name.LastIndexOf('+'));
                return index >= 0 && index < name.Length - 1 ? name.Substring(index + 1) : name;
            }
        }
    }

    public sealed class StepImageInfo
    {
        public string Name { get; set; }
        public string Alias { get; set; }

        /// <summary>0 = pre, 1 = post, 2 = both.</summary>
        public int ImageType { get; set; }

        public string Attributes { get; set; }
    }

    /// <summary>A registered step (sdkmessageprocessingstep): message + table + stage + mode → plugin type or service endpoint.</summary>
    public sealed class StepInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Message { get; set; }

        /// <summary>Table logical name, null for a step on any table (global message).</summary>
        public string Entity { get; set; }

        /// <summary>10 = pre-validation, 20 = pre-operation, 40 = post-operation.</summary>
        public int Stage { get; set; }

        /// <summary>0 = synchronous, 1 = asynchronous.</summary>
        public int Mode { get; set; }

        public int Rank { get; set; }
        public bool Enabled { get; set; }
        public string FilteringAttributes { get; set; }
        public string Description { get; set; }

        /// <summary>0 = system (Microsoft internal), 1 = custom.</summary>
        public int CustomizationLevel { get; set; }

        public Guid? PluginTypeId { get; set; }
        public Guid? EndpointId { get; set; }
        public string HandlerName { get; set; }

        public List<StepImageInfo> Images { get; } = new List<StepImageInfo>();

        public bool IsAsync => Mode == 1;
    }

    /// <summary>An operation found in the plugin code: « Update account » in MyPlugin.Execute.</summary>
    public sealed class CodeOperation
    {
        public string Operation { get; set; }

        /// <summary>Table, null when the code does not tell which one (e.g. update of an entity received as a parameter).</summary>
        public string Entity { get; set; }

        /// <summary>Method where the call was found (Type.Method).</summary>
        public string Method { get; set; }

        /// <summary>The table was deduced from another method of the same plugin (less certain).</summary>
        public bool Inferred { get; set; }

        public bool IsRead => Operation == "Retrieve" || Operation == "RetrieveMultiple";
    }

    /// <summary>Plugin registrations of the organization: assemblies, types, steps (and their images).</summary>
    public sealed class PluginModel
    {
        public List<PluginAssemblyInfo> Assemblies { get; set; } = new List<PluginAssemblyInfo>();
        public List<PluginTypeInfo> Types { get; set; } = new List<PluginTypeInfo>();
        public List<StepInfo> Steps { get; set; } = new List<StepInfo>();

        /// <summary>Plugin assemblies, types and steps of each solution (component ids), cached once read.</summary>
        public Dictionary<Guid, HashSet<Guid>> SolutionComponents { get; } = new Dictionary<Guid, HashSet<Guid>>();

        public PluginAssemblyInfo FindAssembly(Guid id) => Assemblies.FirstOrDefault(a => a.Id == id);

        public PluginTypeInfo FindType(Guid? id) => id == null ? null : Types.FirstOrDefault(t => t.Id == id.Value);
    }

    public sealed class FormInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        /// <summary>Table of the form (logical name).</summary>
        public string Entity { get; set; }

        public int Type { get; set; }
        public bool Active { get; set; }
        public bool IsDefault { get; set; }
        public string Description { get; set; }
        public List<string> Libraries { get; } = new List<string>();
        public List<FormHandler> Handlers { get; } = new List<FormHandler>();
    }

    /// <summary>A JavaScript function called by a form event (OnLoad, OnSave, OnChange of a column, tab state change…).</summary>
    public sealed class FormHandler
    {
        public string Form { get; set; }
        public string Event { get; set; }

        /// <summary>Column, tab or control the event belongs to (empty for form events).</summary>
        public string Source { get; set; }

        public string Library { get; set; }
        public string Function { get; set; }
        public bool Enabled { get; set; }
        public bool PassContext { get; set; }
    }

    /// <summary>The « logic » attached to a table: forms, their scripts and the registered plugin steps.</summary>
    public sealed class TableLogic
    {
        public List<FormInfo> Forms { get; set; } = new List<FormInfo>();
        public List<StepInfo> Steps { get; set; } = new List<StepInfo>();

        /// <summary>Plugin type and assembly name of each step handler.</summary>
        public Dictionary<Guid, PluginTypeInfo> Types { get; set; } = new Dictionary<Guid, PluginTypeInfo>();
    }
}
