namespace DataverseCartographer
{
    /// <summary>
    /// Préférences du plugin, persistées par le SettingsManager de XrmToolBox (sérialisation XML).
    /// </summary>
    public class PluginSettings
    {
        /// <summary>Langue de l'interface : « en » (défaut) ou « fr ».</summary>
        public string Language { get; set; } = "en";

        /// <summary>Disposition automatique (index de <see cref="Models.LayoutKind"/>).</summary>
        public int Layout { get; set; }

        /// <summary>Tables custom / standard (index de <see cref="Models.TableKindFilter"/>).</summary>
        public int TableKind { get; set; }

        public bool HideSystemTables { get; set; } = true;

        public bool HideIsolated { get; set; } = true;

        public bool ShowManyToMany { get; set; } = true;

        public bool ShowSelfReferences { get; set; } = true;

        /// <summary>Profondeur d'exploration autour de la table centrale.</summary>
        public int Depth { get; set; } = 1;

        public bool ShowBoundary { get; set; }

        public bool ShowLabels { get; set; }

        // Onglet Plugins
        public int PluginLayout { get; set; }

        public bool HideMicrosoftPlugins { get; set; } = true;

        public bool IncludeDisabledSteps { get; set; }

        public bool ShowPluginReads { get; set; }

        /// <summary>Analyse statique du code des assemblies (tables créées / mises à jour…).</summary>
        public bool AnalyzePluginCode { get; set; } = true;

        // Onglet JavaScript
        public int ScriptLayout { get; set; }

        public bool HideMicrosoftScripts { get; set; } = true;

        public bool IncludeInactiveForms { get; set; }

        public bool ShowScriptReads { get; set; }

        /// <summary>Analyse du code des ressources web (appels Xrm.WebApi / Web API).</summary>
        public bool AnalyzeScripts { get; set; } = true;
    }
}
