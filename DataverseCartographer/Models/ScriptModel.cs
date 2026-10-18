using System;
using System.Collections.Generic;

namespace DataverseCartographer.Models
{
    /// <summary>A JavaScript web resource used by forms: where it is loaded, which handlers call it, what its code does.</summary>
    public sealed class ScriptLibraryInfo
    {
        public string Name { get; set; }
        public Guid? Id { get; set; }
        public string DisplayName { get; set; }
        public bool IsManaged { get; set; }

        /// <summary>Microsoft / first-party library (msdyn_, mscrm…), hidden by default.</summary>
        public bool IsMicrosoft { get; set; }

        /// <summary>Forms loading the library.</summary>
        public List<FormInfo> Forms { get; } = new List<FormInfo>();

        /// <summary>Form event handlers calling a function of the library.</summary>
        public List<FormHandler> Handlers { get; } = new List<FormHandler>();

        /// <summary>Dataverse calls found in the code (null = not analyzed).</summary>
        public List<ScriptCall> Calls { get; set; }

        public bool Analyzed { get; set; }
        public string AnalysisNote { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>A Dataverse call found in a script: « Xrm.WebApi.updateRecord("account", …) » in onSave(), line 42.</summary>
    public sealed class ScriptCall
    {
        public string Operation { get; set; }

        /// <summary>Table, null when it cannot be read from the code (custom API, variable…).</summary>
        public string Entity { get; set; }

        public string Function { get; set; }
        public int Line { get; set; }

        /// <summary>Read or navigation only (retrieve, open a form…): drawn faded.</summary>
        public bool IsRead { get; set; }
    }

    /// <summary>Forms and their JavaScript libraries for the whole organization.</summary>
    public sealed class ScriptModel
    {
        public List<FormInfo> Forms { get; set; } = new List<FormInfo>();
        public Dictionary<string, ScriptLibraryInfo> Libraries { get; } = new Dictionary<string, ScriptLibraryInfo>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Forms and web resources of each solution (component ids), cached once read.</summary>
        public Dictionary<Guid, HashSet<Guid>> SolutionComponents { get; } = new Dictionary<Guid, HashSet<Guid>>();
    }
}
