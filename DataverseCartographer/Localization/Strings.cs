using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DataverseCartographer.Models;

namespace DataverseCartographer.Localization
{
    public enum Language
    {
        English,
        French
    }

    /// <summary>
    /// Every user-facing text of the tool, in English and French.
    /// Changing <see cref="Current"/> raises <see cref="LanguageChanged"/> so open screens can refresh.
    /// </summary>
    public static class Strings
    {
        private static Language _current = Language.English;

        public static event EventHandler LanguageChanged;

        public static Language Current
        {
            get => _current;
            set
            {
                if (_current == value) return;
                _current = value;
                LanguageChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static bool IsFrench => _current == Language.French;

        public static string ToCode(Language language) => language == Language.French ? "fr" : "en";

        public static Language FromCode(string code) =>
            string.Equals(code, "fr", StringComparison.OrdinalIgnoreCase) ? Language.French : Language.English;

        private static string T(string english, string french) => IsFrench ? french : english;

        public static string Yes => T("Yes", "Oui");
        public static string No => T("No", "Non");

        #region Header / toolbar

        public const string AppTitle = "Dataverse Cartographer";
        public static string AppSubtitle => T("Map your org's tables and relationships", "Cartographiez les tables et relations de votre organisation");
        public static string Close => T("Close", "Fermer");
        public static string CloseTip => T("Close the tool", "Fermer l'outil");
        public static string LoadMetadata => T("Load metadata", "Charger les métadonnées");
        public static string LoadMetadataTip => T("Read the data of the current tab again (data model, or plugin registrations)",
                                                  "Relit les données de l'onglet courant (modèle de données, ou enregistrements de plugins)");
        public static string Export => T("Export", "Exporter");
        public static string ExportTip => T("Export the displayed map (whole graph, not only the visible area)", "Exporte la carte affichée (tout le graphe, pas seulement la zone visible)");
        public static string ExportPng => T("PNG image…", "Image PNG…");
        public static string ExportSvg => T("SVG image (vector)…", "Image SVG (vectorielle)…");
        public static string ExportMermaid => T("Mermaid diagram (.mmd)…", "Diagramme Mermaid (.mmd)…");
        public static string ExportPlantUml => T("PlantUML diagram (.puml)…", "Diagramme PlantUML (.puml)…");
        public static string ExportExcel => T("Excel inventory (.xlsx)…", "Inventaire Excel (.xlsx)…");
        public static string CopyMermaid => T("Copy Mermaid to clipboard", "Copier le Mermaid dans le presse-papiers");
        public static string CopyPlantUml => T("Copy PlantUML to clipboard", "Copier le PlantUML dans le presse-papiers");
        public static string LanguageMenu => T("Language", "Langue");
        public const string EnglishName = "English";
        public const string FrenchName = "Français";
        public static string Donate => T("Donate", "Faire un don");
        public static string DonateTip => T("Support the tool (PayPal)", "Soutenir l'outil (PayPal)");
        public const string AuthorName = "Hamza AMRAOUI";
        public static string AuthorTip => T("Developed by Hamza AMRAOUI (LinkedIn)", "Développé par Hamza AMRAOUI (LinkedIn)");
        public static string NotConnected => T("Not connected", "Non connecté");
        public static string ConnectedTo(string name) => T($"Connected to {name}", $"Connecté à {name}");

        #endregion

        #region Filters

        public static string FiltersTitle => T("Filters", "Filtres");
        public static string FiltersSubtitle => T("Which tables make up the map", "Quelles tables composent la carte");
        public static string SolutionLabel => T("Solution", "Solution");
        public static string AllSolutions => T("All tables (no solution filter)", "Toutes les tables (sans filtre solution)");
        public static string PublisherLabel => T("Publisher prefix", "Préfixe d'éditeur");
        public static string AllPublishers => T("All prefixes", "Tous les préfixes");
        public static string PublisherItem(string prefix, string name, int count) =>
            string.IsNullOrEmpty(name) ? $"{prefix}_  ({count})" : $"{prefix}_  ·  {name}  ({count})";
        public static string TablesLabel => T("Tables", "Tables");
        public static string[] TableKinds => IsFrench
            ? new[] { "Custom et standard", "Custom uniquement", "Standard uniquement" }
            : new[] { "Custom and standard", "Custom only", "Standard only" };
        public static string HideSystem => T("Hide system tables", "Masquer les tables système");
        public static string HideSystemTip => T("Hides the technical tables linked to almost everything (systemuser, team, businessunit, activitypointer, annotation…)",
                                                "Masque les tables techniques liées à presque tout (systemuser, team, businessunit, activitypointer, annotation…)");
        public static string HideIsolated => T("Hide tables without relationships", "Masquer les tables sans relation");
        public static string HideIsolatedTip => T("Hides the tables that have no relationship with another displayed table",
                                                  "Masque les tables qui n'ont aucune relation avec une autre table affichée");
        public static string OnlyUsedTables => T("Only tables with data (≥ 1 active record)", "Seulement les tables utilisées (≥ 1 enregistrement actif)");
        public static string OnlyUsedTablesTip => T(
            "Hides the tables without any active record (statecode = 0; any record for activities and tables without status). Checked once per connection on the server; tables that cannot be read stay visible.",
            "Masque les tables sans aucun enregistrement actif (statecode = 0 ; tout enregistrement pour les activités et les tables sans statut). Vérifié une fois par connexion sur le serveur ; les tables illisibles restent visibles.");
        public static string CheckingUsageTitle => T("Checking which tables hold data…", "Vérification des tables qui contiennent des données…");
        public static string ReadingRecordCounts => T("Reading the record counts…", "Lecture du nombre d'enregistrements…");
        public static string CheckingUsage(int done, int total) => T($"Looking for active records ({done}/{total} tables)…", $"Recherche d'enregistrements actifs ({done}/{total} tables)…");
        public static string UsageFailed => T("The usage of the tables could not be checked", "L'utilisation des tables n'a pas pu être vérifiée");
        public static string EmptyTablesHidden(int count) => T($"  ·  {count:N0} empty tables hidden", $"  ·  {count:N0} tables vides masquées");
        public static string PropRecords => T("Records", "Enregistrements");
        public static string RecordsValue(long? count, bool? active)
        {
            var text = count.HasValue ? T($"≈ {count.Value:N0} (daily snapshot)", $"≈ {count.Value:N0} (instantané quotidien)") : string.Empty;
            if (active == false) text += (text.Length > 0 ? "  ·  " : string.Empty) + T("no active record", "aucun enregistrement actif");
            else if (active == true && !count.HasValue) text = T("has active records", "contient des enregistrements actifs");
            return text;
        }
        public static string ShowManyToMany => T("Show N:N relationships", "Afficher les relations N:N");
        public static string ShowSelf => T("Show self-references", "Afficher les auto-références");
        public static string ShowSelfTip => T("Relationships from a table to itself (parent account, manager…)", "Relations d'une table vers elle-même (compte parent, responsable…)");

        #endregion

        #region Explore

        public static string ExploreTitle => T("Explore", "Explorer");
        public static string ExploreSubtitle => T("Neighbors of one table, N levels deep", "Voisins d'une table, sur N niveaux");
        public static string FocusLabel => T("Focus table", "Table centrale");
        public static string FocusCue => T("Type a table name…", "Tapez un nom de table…");
        public static string DepthLabel => T("Levels", "Niveaux");
        public static string ShowBoundary => T("Show linked tables outside the filters", "Afficher les tables liées hors filtres");
        public static string ShowBoundaryTip => T("Adds, faded, the tables excluded by the filters that are directly linked to a displayed table",
                                                  "Ajoute, en grisé, les tables exclues par les filtres directement liées à une table affichée");
        public static string ShowFocus => T("Explore", "Explorer");
        public static string ClearFocus => T("Whole map", "Carte complète");
        public static string PreviousFocus => T("Back", "Retour");
        public static string PreviousFocusTip(string name) => T($"Back to {name}", $"Revenir à {name}");
        public static string HiddenTables(int count) => T($"{count} table(s) hidden manually.", $"{count} table(s) masquée(s) manuellement.");
        public static string ShowHiddenTables => T("Show them again", "Les réafficher");
        public static string ExploreHint => T("Tip: double-click a table on the map to explore around it, right-click for more.",
                                              "Astuce : double-cliquez sur une table de la carte pour explorer autour, clic droit pour plus d'options.");

        #endregion

        #region Map toolbar / status

        public static string LayoutLabel => T("Layout", "Disposition");
        public static string[] Layouts => IsFrench
            ? new[] { "Hiérarchique", "Force-directed", "Circulaire / radiale", "Grille" }
            : new[] { "Hierarchical", "Force-directed", "Circular / radial", "Grid" };
        public static string Relayout => T("Re-arrange", "Réorganiser");
        public static string RelayoutTip => T("Apply the layout again (tables moved by hand go back to their place)", "Réapplique la disposition (les tables déplacées à la main reprennent leur place)");
        public static string Fit => T("Fit", "Ajuster");
        public static string FitTip => T("Fit the whole map in the window", "Affiche toute la carte dans la fenêtre");
        public static string ZoomInTip => T("Zoom in (mouse wheel)", "Zoom avant (molette)");
        public static string ZoomOutTip => T("Zoom out (mouse wheel)", "Zoom arrière (molette)");
        public static string Labels => T("Labels", "Libellés");
        public static string LabelsTip => T("Show the lookup column of every relationship (always shown for the selected table)",
                                            "Affiche la colonne de recherche de chaque relation (toujours affichée pour la table sélectionnée)");
        public static string SearchCue => T("Find a table or a column…", "Rechercher une table ou une colonne…");
        public static string SearchHits(int count) => T($"{count} match(es) · Enter = next", $"{count} résultat(s) · Entrée = suivant");
        public static string SearchNotDisplayed(string name) => T($"{name} is not on the map · Enter = explore it", $"{name} n'est pas sur la carte · Entrée = l'explorer");
        public static string SearchNoMatch => T("No match", "Aucun résultat");

        public static string GraphStatus(int nodes, int edges, int total) =>
            T($"{nodes:N0} tables · {edges:N0} relationships  (of {total:N0} tables in the org)",
              $"{nodes:N0} tables · {edges:N0} relations  (sur {total:N0} tables dans l'org)");
        public static string FocusStatus(string name, int depth) => T($"  ·  around {name}, {depth} level(s)", $"  ·  autour de {name}, {depth} niveau(x)");
        public static string LargeGraphWarning => T("  ·  large map: filter by solution or explore one table for readability",
                                                    "  ·  carte volumineuse : filtrez par solution ou explorez une table pour plus de lisibilité");
        public static string EmptyNotLoaded => T("Connect to an organization: its tables and relationships will be drawn here.",
                                                 "Connectez-vous à une organisation : ses tables et relations seront dessinées ici.");
        public static string EmptyNoTables => T("No table matches the filters.", "Aucune table ne correspond aux filtres.");

        public static string LegendCustom => T("Custom", "Custom");
        public static string LegendStandard => T("Standard", "Standard");
        public static string LegendFocus => T("Focus", "Centre");
        public static string LegendOneToMany => "1:N";
        public static string LegendManyToMany => "N:N";
        public static string LegendCascadeDelete => T("Cascade delete", "Suppression en cascade");

        #endregion

        #region Details

        public static string DetailsTitle => T("Details", "Détails");
        public static string DetailsSubtitle => T("Select a table or a relationship on the map", "Sélectionnez une table ou une relation sur la carte");
        public static string TabColumns => T("Columns", "Colonnes");
        public static string TabRelationships => T("Relationships", "Relations");
        public static string TabKeys => T("Keys", "Clés");
        public static string LoadingColumns => T("Loading columns…", "Chargement des colonnes…");
        public static string ColumnsError(string message) => T($"Columns unavailable: {message}", $"Colonnes indisponibles : {message}");
        public static string NoKeys => T("No alternate key", "Aucune clé alternative");
        public static string FocusHere => T("Explore", "Explorer");
        public static string FocusHereTip => T("Show this table and its neighbors", "Affiche cette table et ses voisines");
        public static string HideTable => T("Hide", "Masquer");
        public static string HideTableTip => T("Remove this table from the map", "Retire cette table de la carte");
        public static string ShowTable => T("Go to table", "Aller à la table");
        public static string CustomTable => T("Custom table", "Table custom");
        public static string StandardTable => T("Standard table", "Table standard");
        public static string CustomRelationship => T("Custom relationship", "Relation custom");
        public static string SystemRelationship => T("System relationship", "Relation système");
        public static string ManagedSuffix => T(" · managed", " · managée");
        public static string UnmanagedSuffix => T(" · unmanaged", " · non managée");
        public static string RelationshipSubtitle(RelationInfo r) =>
            r.Kind == RelationKind.ManyToMany ? $"N:N  ·  {r.From} ⇄ {r.To}" : $"1:N  ·  {r.From} → {r.To}";

        public static string PropLogicalName => T("Logical name", "Nom logique");
        public static string PropSchemaName => T("Schema name", "Nom de schéma");
        public static string PropType => T("Type", "Type");
        public static string PropOwnership => T("Ownership", "Propriété");
        public static string PropPrimaryKey => T("Primary key", "Clé primaire");
        public static string PropPrimaryName => T("Primary name", "Nom principal");
        public static string PropObjectTypeCode => T("Object type code", "Code de type");
        public static string PropPrefix => T("Publisher prefix", "Préfixe d'éditeur");
        public static string PropActivity => T("Activity", "Activité");
        public static string PropRelations => T("Relationships", "Relations");
        public static string PropRelationsValue(int oneToMany, int manyToOne, int manyToMany) => $"{oneToMany} × 1:N  ·  {manyToOne} × N:1  ·  {manyToMany} × N:N";
        public static string PropDescription => "Description";
        public static string PropKind => T("Type", "Type");
        public static string PropOneSide => T("Primary table (1)", "Table principale (1)");
        public static string PropManySide => T("Related table (N)", "Table associée (N)");
        public static string PropLookup => T("Lookup column", "Colonne de recherche");
        public static string PropReferenced => T("Referenced column", "Colonne référencée");
        public static string PropEntity1 => T("Table 1", "Table 1");
        public static string PropEntity2 => T("Table 2", "Table 2");
        public static string PropIntersect => T("Intersect table", "Table intersect");
        public static string CascadeHeader => T("Cascade behavior", "Comportement en cascade");
        public static string CascadeDeleteWarning => T("Deleting a parent record deletes its children", "Supprimer un parent supprime ses enfants");
        public static string CascadeRestrictInfo => T("A parent record with children cannot be deleted", "Un parent ayant des enfants ne peut pas être supprimé");

        public static string ColDisplayName => T("Display name", "Nom complet");
        public static string ColLogicalName => T("Logical name", "Nom logique");
        public static string ColType => T("Type", "Type");
        public static string ColRequired => T("Required", "Requis");
        public static string ColCustom => "Custom";
        public static string ColRelKind => T("Kind", "Type");
        public static string ColRelated => T("Related table", "Table liée");
        public static string ColVia => T("Lookup / intersect", "Recherche / intersect");
        public static string ColDelete => T("Delete", "Suppression");
        public static string ColSchema => T("Schema name", "Nom de schéma");
        public static string ColKeyName => T("Key", "Clé");
        public static string ColKeyColumns => T("Columns", "Colonnes");
        public static string ColKeyStatus => T("Index status", "Statut de l'index");

        public static string OwnershipLabel(string ownership)
        {
            switch (ownership)
            {
                case "UserOwned": return T("User or team", "Utilisateur ou équipe");
                case "OrganizationOwned": return T("Organization", "Organisation");
                case "BusinessOwned": return T("Business unit", "Division");
                case "TeamOwned": return T("Team", "Équipe");
                case "None": return T("None", "Aucune");
                default: return ownership;
            }
        }

        public static string RequiredLabel(string level)
        {
            switch (level)
            {
                case "SystemRequired": return T("System required", "Requis (système)");
                case "ApplicationRequired": return T("Required", "Requis");
                case "Recommended": return T("Recommended", "Recommandé");
                case "None": return T("Optional", "Facultatif");
                default: return level;
            }
        }

        #endregion

        #region Forms / scripts / steps of a table

        public static string TabForms => T("Forms", "Formulaires");
        public static string TabScripts => "JavaScript";
        public static string TabPlugins => "Plugins";
        public static string ColFormName => T("Form", "Formulaire");
        public static string ColFormType => T("Type", "Type");
        public static string ColLibraries => T("Libraries", "Bibliothèques");
        public static string ColHandlers => T("Handlers", "Gestionnaires");
        public static string ColLibrary => T("Library (web resource)", "Bibliothèque (ressource web)");
        public static string ColEvent => T("Event", "Événement");
        public static string ColFunction => T("Function", "Fonction");
        public static string ColForm => T("Form", "Formulaire");
        public static string NoForms => T("No form", "Aucun formulaire");
        public static string NoScripts => T("No JavaScript on the forms of this table", "Aucun JavaScript sur les formulaires de cette table");
        public static string NoSteps => T("No plugin step", "Aucune étape de plugin");
        public static string Active => T("Active", "Actif");
        public static string Inactive => T("Inactive", "Inactif");
        public static string LibraryOnly => T("(loaded, no handler)", "(chargée, sans gestionnaire)");
        public static string PassContextTip => T("Receives the execution context", "Reçoit le contexte d'exécution");
        public static string OpenInPluginMapTip => T("Double-click: open in the plugin map", "Double-clic : ouvrir dans la carte des plugins");
        public static string LoadingLogic => T("Loading forms, scripts and plugin steps…", "Chargement des formulaires, scripts et étapes de plugins…");
        public static string LogicError(string message) => T($"Forms / steps unavailable: {message}", $"Formulaires / étapes indisponibles : {message}");

        public static string FormTypeLabel(int type)
        {
            switch (type)
            {
                case 0: return T("Dashboard", "Tableau de bord");
                case 2: return T("Main", "Principal");
                case 5: return "Mobile Express";
                case 6: return T("Quick view", "Affichage rapide");
                case 7: return T("Quick create", "Création rapide");
                case 8: return T("Dialog", "Boîte de dialogue");
                case 9: return T("Task flow", "Flux de tâches");
                case 11: return T("Card", "Carte");
                case 12: return T("Main (interactive)", "Principal (interactif)");
                case 101: return T("Main (backup)", "Principal (sauvegarde)");
                default: return type.ToString();
            }
        }

        public static string EventLabel(string evt, string source)
        {
            string name;
            switch ((evt ?? string.Empty).ToLowerInvariant())
            {
                case "onload": name = "OnLoad"; break;
                case "onsave": name = "OnSave"; break;
                case "onchange": name = "OnChange"; break;
                case "tabstatechange": name = "TabStateChange"; break;
                case "onreadystatecomplete": name = "OnReadyStateComplete"; break;
                default: name = evt; break;
            }
            return string.IsNullOrEmpty(source) ? name : $"{name} ({source})";
        }

        #endregion

        #region Plugins page

        public static string TabDataModel => T("Data model", "Modèle de données");
        public static string PluginFiltersTitle => T("Plugin filters", "Filtres des plugins");
        public static string PluginFiltersSubtitle => T("Which plugins make up the map", "Quels plugins composent la carte");
        public static string AllPluginSolutions => T("All solutions", "Toutes les solutions");
        public static string AssemblyLabel => T("Assembly (DLL)", "Assembly (DLL)");
        public static string AllAssemblies => T("All assemblies", "Toutes les assemblies");
        public static string AssemblyItem(string name, string version, int types) => T($"{name} ({version}) · {types} type(s)", $"{name} ({version}) · {types} type(s)");
        public static string HideMicrosoft => T("Hide Microsoft / system plugins", "Masquer les plugins Microsoft / système");
        public static string HideMicrosoftTip => T("Hides the platform assemblies (Microsoft.*) and the internal steps", "Masque les assemblies de la plateforme (Microsoft.*) et les étapes internes");
        public static string IncludeDisabled => T("Include disabled steps", "Inclure les étapes désactivées");
        public static string ShowReads => T("Show reads (Retrieve)", "Afficher les lectures (Retrieve)");
        public static string ShowReadsTip => T("Also draws the tables the plugins only read", "Dessine aussi les tables que les plugins ne font que lire");
        public static string AnalyzeCode => T("Analyze the code (tables touched)", "Analyser le code (tables touchées)");
        public static string AnalyzeCodeTip => T("Reads the IL of the assemblies stored in the organization to find what each plugin creates, updates or deletes (heuristic)",
                                                 "Lit l'IL des assemblies stockées dans l'organisation pour trouver ce que chaque plugin crée, met à jour ou supprime (heuristique)");
        public static string LoadPlugins => T("Reload plugins", "Recharger les plugins");
        public static string PluginExploreSubtitle => T("Chain around a table or a plugin", "Chaîne autour d'une table ou d'un plugin");
        public static string PluginFocusLabel => T("Table or plugin", "Table ou plugin");
        public static string PluginFocusCue => T("Type a table or plugin…", "Tapez une table ou un plugin…");
        public static string PluginHint => T("Follow the arrows: table → (step) → plugin → (what its code does) → table. Double-click a node to explore around it.",
                                             "Suivez les flèches : table → (étape) → plugin → (ce que fait son code) → table. Double-cliquez sur un nœud pour explorer autour.");
        public static string PluginSearchCue => T("Find a table or a plugin…", "Rechercher une table ou un plugin…");
        public static string TabSteps => T("Steps", "Étapes");
        public static string TabCodeOperations => T("Code operations", "Opérations du code");
        public static string ColMessage => T("Message", "Message");
        public static string ColTable => T("Table", "Table");
        public static string ColStage => T("Stage", "Phase");
        public static string ColMode => T("Mode", "Mode");
        public static string ColRank => T("Rank", "Rang");
        public static string ColHandler => T("Plugin", "Plugin");
        public static string ColFilteringAttributes => T("Filtering attributes", "Attributs filtrés");
        public static string ColImages => T("Images", "Images");
        public static string ColState => T("State", "État");
        public static string ColPlugin => T("Plugin", "Plugin");
        public static string ColOperation => T("Operation", "Opération");
        public static string ColMethod => T("Found in", "Trouvé dans");
        public static string LegendTable => T("Table", "Table");
        public static string LegendPlugin => T("Plugin", "Plugin");
        public static string LegendSyncStep => T("Sync step", "Étape synchrone");
        public static string LegendAsyncStep => T("Async step", "Étape asynchrone");
        public static string LegendWrite => T("Writes", "Écrit");
        public static string LegendRead => T("Reads", "Lit");
        public static string PluginsEmptyNotLoaded => T("Plugin registrations will be drawn here.", "Les enregistrements de plugins seront dessinés ici.");
        public static string PluginsEmptyNoMatch => T("No plugin step matches the filters.", "Aucune étape de plugin ne correspond aux filtres.");
        public static string LoadingPlugins => T("Loading plugin registrations…", "Chargement des enregistrements de plugins…");
        public static string ReadingAssemblies => T("Reading plugin assemblies…", "Lecture des assemblies de plugins…");
        public static string ReadingPluginTypes => T("Reading plugin types…", "Lecture des types de plugins…");
        public static string ReadingSteps => T("Reading steps and images…", "Lecture des étapes et images…");
        public static string PluginsLoadFailed => T("The plugin registrations could not be loaded", "Les enregistrements de plugins n'ont pas pu être chargés");
        public static string AnalyzingCode => T("Analyzing the plugin code…", "Analyse du code des plugins…");
        public static string AnalyzingAssembly(string name, int index, int count) => T($"Analyzing {name} ({index}/{count})…", $"Analyse de {name} ({index}/{count})…");
        public static string AnalysisFailed => T("The code analysis failed", "L'analyse du code a échoué");
        public static string AnalysisSummary(int operations, int types) => T($"{operations} table operation(s) in {types} plugin(s)", $"{operations} opération(s) sur des tables dans {types} plugin(s)");
        public static string AnalysisError(string message) => "!" + T($"Analysis failed: {message}", $"Analyse impossible : {message}");
        public static string AnalysisNoContent => "!" + T("Code not stored in the organization", "Code non stocké dans l'organisation");
        public static string AnalysisOnDisk => "!" + T("Assembly on disk / GAC: code not available", "Assembly sur disque / GAC : code indisponible");
        public static string AnalysisNoPackageContent => "!" + T("Plugin package: DLL not found in the package", "Package de plugins : DLL introuvable dans le package");
        public static string AnalysisStatus(int analyzed, int failed) => failed == 0
            ? T($"Code analyzed for {analyzed} assembly(ies).", $"Code analysé pour {analyzed} assembly(ies).")
            : T($"Code analyzed for {analyzed} assembly(ies), {failed} without readable code.", $"Code analysé pour {analyzed} assembly(ies), dont {failed} sans code lisible.");
        public static string AnalysisOff => T("Code analysis off: only the steps are drawn.", "Analyse du code désactivée : seules les étapes sont dessinées.");
        public static string NotAnalyzed => T("Not analyzed", "Non analysé");
        public static string PluginStatus(int plugins, int steps, int actions, int tables) =>
            T($"{plugins:N0} plugins · {steps:N0} steps · {actions:N0} table operations · {tables:N0} tables",
              $"{plugins:N0} plugins · {steps:N0} étapes · {actions:N0} opérations sur des tables · {tables:N0} tables");
        public static string PluginDetailsSubtitle => T("Select a table, a plugin or an arrow on the map", "Sélectionnez une table, un plugin ou une flèche sur la carte");
        public static string TriggerSubtitle => T("Registered steps that run the plugin", "Étapes enregistrées qui déclenchent le plugin");
        public static string ActionSubtitle => T("What the plugin code does on the table (static analysis)", "Ce que fait le code du plugin sur la table (analyse statique)");
        public static string PropTriggers => T("Steps", "Étapes");
        public static string PropOperations => T("Operations", "Opérations");
        public static string PropAssembly => "Assembly";
        public static string PropIsolation => T("Isolation", "Isolation");
        public static string Sandbox => "Sandbox";
        public static string NoIsolation => T("None", "Aucune");
        public static string PropSource => T("Stored in", "Stockée dans");
        public static string PropCodeAnalysis => T("Code analysis", "Analyse du code");
        public static string PropWorkflowActivity => T("Workflow activity", "Activité de workflow");
        public static string PropStepsOnTable => T("Steps on the table", "Étapes sur la table");
        public static string PropPluginsWriting => T("Plugins writing to it", "Plugins qui y écrivent");
        public static string GlobalMessage => T("Any table (global message)", "Toute table (message global)");
        public static string ServiceEndpoint => T("Service endpoint", "Point de terminaison de service");
        public static string Enabled => T("Enabled", "Activée");
        public static string Disabled => T("Disabled", "Désactivée");
        public static string TableNotDetected => T("(table not detected)", "(table non détectée)");
        public static string InferredTip => T("Table deduced from another method of the plugin: to be checked", "Table déduite d'une autre méthode du plugin : à vérifier");
        public static string NoOperations => T("No table operation found in the code", "Aucune opération sur une table trouvée dans le code");
        public static string ScopeAllPlugins => T("all plugins", "tous les plugins");
        public static string PluginExportTitle(string org) => string.IsNullOrEmpty(org)
            ? T("Dataverse plugins", "Plugins Dataverse")
            : T($"Dataverse plugins — {org}", $"Plugins Dataverse — {org}");

        public static string StageLabel(int stage)
        {
            switch (stage)
            {
                case 10: return T("Pre-validation", "Pré-validation");
                case 20: return T("Pre-operation", "Pré-opération");
                case 30: return T("Main operation", "Opération principale");
                case 40: return T("Post-operation", "Post-opération");
                case 50: return T("Post-operation (deprecated)", "Post-opération (obsolète)");
                default: return stage.ToString();
            }
        }

        public static string StageShort(int stage)
        {
            switch (stage)
            {
                case 10: return "PreVal";
                case 20: return "Pre";
                case 40: return "Post";
                default: return stage.ToString();
            }
        }

        public static string ModeLabel(int mode) => mode == 1 ? T("Async", "Asynchrone") : T("Sync", "Synchrone");

        public static string ImagesLabel(System.Collections.Generic.IEnumerable<StepImageInfo> images) => string.Join(", ", images.Select(i =>
            $"{(i.ImageType == 0 ? "Pre" : i.ImageType == 1 ? "Post" : "Pre+Post")}:{i.Alias}" + (string.IsNullOrEmpty(i.Attributes) ? " (*)" : string.Empty)));

        public static string SourceLabel(PluginAssemblyInfo assembly)
        {
            if (assembly.PackageId.HasValue) return T("Plugin package (NuGet)", "Package de plugins (NuGet)");
            switch (assembly.SourceType)
            {
                case 0: return T("Database", "Base de données");
                case 1: return T("Disk", "Disque");
                case 2: return "GAC";
                default: return assembly.SourceType.ToString();
            }
        }

        #endregion

        #region Node menu

        public static string MenuExplore(int depth) => T($"Explore around ({depth} level(s))", $"Explorer autour ({depth} niveau(x))");
        public static string MenuHide => T("Hide this table", "Masquer cette table");
        public static string MenuCenter => T("Center the view", "Centrer la vue");
        public static string MenuCopyLogicalName => T("Copy logical name", "Copier le nom logique");
        public static string MenuCopySchemaName => T("Copy schema name", "Copier le nom de schéma");

        #endregion

        #region Progress / messages

        public static string LoadingMetadata => T("Loading the data model…", "Chargement du modèle de données…");
        public static string ReadingTables => T("Reading tables and relationships…", "Lecture des tables et relations…");
        public static string ReadingTablesFallback => T("Reading the full metadata (slower)…", "Lecture complète des métadonnées (plus lent)…");
        public static string ReadingSolutions => T("Reading solutions and publishers…", "Lecture des solutions et éditeurs…");
        public static string ReadingSolutionTables => T("Reading the tables of the solution…", "Lecture des tables de la solution…");
        public static string LoadingColumnsProgress(int index, int count) => T($"Loading columns ({index}/{count})…", $"Chargement des colonnes ({index}/{count})…");
        public static string Exporting => T("Exporting…", "Export en cours…");

        public static string LoadFailed => T("The data model could not be loaded", "Le modèle de données n'a pas pu être chargé");
        public static string SolutionFailed => T("The tables of the solution could not be read", "Les tables de la solution n'ont pas pu être lues");
        public static string ExportFailed => T("The export failed", "L'export a échoué");
        public static string NothingToExport => T("The map is empty: nothing to export.", "La carte est vide : rien à exporter.");
        public static string CopiedToClipboard => T("Copied to the clipboard.", "Copié dans le presse-papiers.");
        public static string OpenExportQuestion => T("Open it now?", "L'ouvrir maintenant ?");
        public static string ExportDone(string path) => T($"Export saved:\r\n{path}\r\n\r\nOpen it now?", $"Export enregistré :\r\n{path}\r\n\r\nL'ouvrir maintenant ?");
        public static string ExcelLoadColumns(int count) =>
            T($"Also include the columns of the {count} displayed tables?\r\n(they are read from the organization, one table at a time)",
              $"Inclure aussi les colonnes des {count} tables affichées ?\r\n(elles sont lues dans l'organisation, une table à la fois)");
        public static string ExportTitle(string org) => string.IsNullOrEmpty(org)
            ? T("Dataverse data model", "Modèle de données Dataverse")
            : T($"Dataverse data model — {org}", $"Modèle de données Dataverse — {org}");
        public static string ExportSubtitle(int nodes, int edges, string scope) =>
            T($"{nodes} tables · {edges} relationships · {scope} · {DateTime.Now:yyyy-MM-dd HH:mm} · Dataverse Cartographer",
              $"{nodes} tables · {edges} relations · {scope} · {DateTime.Now:yyyy-MM-dd HH:mm} · Dataverse Cartographer");
        public static string ScopeAllTables => T("all tables", "toutes les tables");
        public static string ScopeSolution(string name) => T($"solution {name}", $"solution {name}");
        public static string ScopeFocus(string name, int depth) => T($"around {name} ({depth} level(s))", $"autour de {name} ({depth} niveau(x))");

        #endregion

        #region JavaScript page

        public static string TabJavaScript => "JavaScript";
        public static string ScriptFiltersTitle => T("JavaScript filters", "Filtres JavaScript");
        public static string ScriptFiltersSubtitle => T("Which scripts make up the map", "Quels scripts composent la carte");
        public static string LibraryLabel => T("Library (web resource)", "Bibliothèque (ressource web)");
        public static string AllLibraries => T("All libraries", "Toutes les bibliothèques");
        public static string LibraryItem(string name, int forms) => T($"{name} · {forms} form(s)", $"{name} · {forms} formulaire(s)");
        public static string HideMicrosoftScripts => T("Hide Microsoft libraries", "Masquer les bibliothèques Microsoft");
        public static string HideMicrosoftScriptsTip => T("Hides the first-party scripts (msdyn_, mscrm, adx_…)", "Masque les scripts Microsoft (msdyn_, mscrm, adx_…)");
        public static string IncludeInactiveForms => T("Include inactive forms", "Inclure les formulaires inactifs");
        public static string ShowScriptReads => T("Show reads and navigation", "Afficher les lectures et la navigation");
        public static string ShowScriptReadsTip => T("Also draws retrieves, FetchXML, openForm, navigateTo, lookupObjects", "Dessine aussi les retrieve, FetchXML, openForm, navigateTo, lookupObjects");
        public static string AnalyzeScripts => T("Analyze the scripts (tables called)", "Analyser les scripts (tables appelées)");
        public static string AnalyzeScriptsTip => T("Reads the code of the web resources to find the Xrm.WebApi / Web API calls and their table (heuristic)",
                                                    "Lit le code des ressources web pour trouver les appels Xrm.WebApi / Web API et leur table (heuristique)");
        public static string LoadScripts => T("Reload scripts", "Recharger les scripts");
        public static string ScriptExploreSubtitle => T("Chain around a table or a script", "Chaîne autour d'une table ou d'un script");
        public static string ScriptFocusLabel => T("Table or library", "Table ou bibliothèque");
        public static string ScriptFocusCue => T("Type a table or script…", "Tapez une table ou un script…");
        public static string ScriptHint => T("Follow the arrows: table → (form events) → library → (what its code calls) → table. Double-click a node to explore around it.",
                                             "Suivez les flèches : table → (événements de formulaire) → bibliothèque → (ce que son code appelle) → table. Double-cliquez sur un nœud pour explorer autour.");
        public static string ScriptSearchCue => T("Find a table or a script…", "Rechercher une table ou un script…");
        public static string TabHandlers => T("Form handlers", "Gestionnaires");
        public static string TabScriptCalls => T("Calls", "Appels");
        public static string ColFunctionCall => T("Function", "Fonction");
        public static string ColLine => T("Line", "Ligne");
        public static string LegendScript => "JavaScript";
        public static string LegendFormEvent => T("Form event", "Événement de formulaire");
        public static string LegendLoaded => T("Loaded only", "Chargée seulement");
        public static string ScriptsEmptyNotLoaded => T("Form scripts will be drawn here.", "Les scripts des formulaires seront dessinés ici.");
        public static string ScriptsEmptyNoMatch => T("No form script matches the filters.", "Aucun script de formulaire ne correspond aux filtres.");
        public static string LoadingScripts => T("Loading forms and scripts…", "Chargement des formulaires et scripts…");
        public static string ReadingForms => T("Reading the forms and their libraries…", "Lecture des formulaires et de leurs bibliothèques…");
        public static string ReadingWebResources => T("Reading the web resources…", "Lecture des ressources web…");
        public static string ScriptsLoadFailed => T("The forms and scripts could not be loaded", "Les formulaires et scripts n'ont pas pu être chargés");
        public static string AnalyzingScripts => T("Analyzing the scripts…", "Analyse des scripts…");
        public static string AnalyzingScript(string name, int index, int count) => T($"Analyzing {name} ({index}/{count})…", $"Analyse de {name} ({index}/{count})…");
        public static string ScriptAnalysisSummary(int calls) => T($"{calls} table call(s)", $"{calls} appel(s) de tables");
        public static string ScriptNotFound => "!" + T("Web resource not found", "Ressource web introuvable");
        public static string ScriptAnalysisStatus(int analyzed) => T($"{analyzed} script(s) analyzed.", $"{analyzed} script(s) analysé(s).");
        public static string ScriptAnalysisOff => T("Script analysis off: only the form events are drawn.", "Analyse des scripts désactivée : seuls les événements de formulaire sont dessinés.");
        public static string ScriptStatus(int libraries, int handlers, int calls, int tables) =>
            T($"{libraries:N0} libraries · {handlers:N0} handlers · {calls:N0} table calls · {tables:N0} tables",
              $"{libraries:N0} bibliothèques · {handlers:N0} gestionnaires · {calls:N0} appels de tables · {tables:N0} tables");
        public static string ScriptDetailsSubtitle => T("Select a table, a script or an arrow on the map", "Sélectionnez une table, un script ou une flèche sur la carte");
        public static string ScriptTriggerSubtitle => T("Form events that call the library", "Événements de formulaire qui appellent la bibliothèque");
        public static string ScriptActionSubtitle => T("What the script does on the table (static analysis)", "Ce que fait le script sur la table (analyse statique)");
        public static string NoHandlers => T("No form handler", "Aucun gestionnaire de formulaire");
        public static string NoScriptCalls => T("No table call found in the code", "Aucun appel de table trouvé dans le code");
        public static string LoadedOnly => T("loaded", "chargée");
        public static string WebResourceJs => T("JavaScript web resource", "Ressource web JavaScript");
        public static string PropForms => T("Forms", "Formulaires");
        public static string PropHandlers => T("Handlers", "Gestionnaires");
        public static string PropLibrary => T("Library", "Bibliothèque");
        public static string PropHandlersOnTable => T("Handlers on the table", "Gestionnaires sur la table");
        public static string PropScriptsCalling => T("Scripts calling it", "Scripts qui l'appellent");
        public static string LineLabel => T("line", "ligne");
        public static string ScopeAllScripts => T("all scripts", "tous les scripts");
        public static string ScriptExportTitle(string org) => string.IsNullOrEmpty(org)
            ? T("Dataverse form scripts", "Scripts de formulaires Dataverse")
            : T($"Dataverse form scripts — {org}", $"Scripts de formulaires Dataverse — {org}");
        public static string HoverNote => T("Note", "Remarque");
        public static string OutsideFiltersNote => T("Outside the filters (shown because it is linked)", "Hors filtres (affichée car liée)");
        public static string FiltersLabel(string attributes) => T($"filters: {attributes}", $"filtres : {attributes}");

        #endregion

        #region Dialogs

        public static string SomethingWentWrong => T("Something went wrong", "Une erreur est survenue");
        public static string MissingPrivileges => T("Missing privileges", "Privilèges manquants");
        public static string PrivilegeIntro => T(
            "Your user account does not have the privileges required for this operation in the connected environment.",
            "Votre compte utilisateur n'a pas les privilèges nécessaires à cette opération dans l'environnement connecté.");
        public static string MissingPrivilege(string name) => T($"Missing privilege: {name}", $"Privilège manquant : {name}");
        public static string PrivilegeSearch => T(
            "• Reading the data model requires read access to the metadata, solutions and publishers.",
            "• La lecture du modèle de données nécessite l'accès en lecture aux métadonnées, solutions et éditeurs.");
        public static string PrivilegeRestore => T(
            "• The plugin and JavaScript maps also read plugin assemblies, steps, forms and web resources.",
            "• Les cartes des plugins et du JavaScript lisent aussi les assemblies, étapes, formulaires et ressources web.");
        public static string PrivilegeAskAdmin => T(
            "Ask a system administrator to add them to one of your security roles, then try again.",
            "Demandez à un administrateur système de les ajouter à l'un de vos rôles de sécurité, puis réessayez.");
        public static string PleaseWait => T("Please wait…", "Veuillez patienter…");
        public static string ExpandDetails => T("Open in a large window", "Ouvrir dans une grande fenêtre");
        public static string DetailsViewerTitle => T("Technical details", "Détails techniques");
        public static string DetailsViewerSubtitle => T("Read, scroll and copy the full message", "Lisez, faites défiler et copiez le message complet");
        public static string WordWrap => T("Wrap lines", "Retour à la ligne");
        public static string ShowDetails => T("Show technical details", "Afficher les détails techniques");
        public static string HideDetails => T("Hide technical details", "Masquer les détails techniques");
        public static string CopyDetails => T("Copy details", "Copier les détails");
        public static string Copied => T("Copied", "Copié");
        public static string OpenFile => T("Open the file", "Ouvrir le fichier");
        public static string NotNow => T("Not now", "Plus tard");
        public static string ExportDoneTitle => T("Export saved", "Export enregistré");
        public static string IncludeColumns => T("Include the columns", "Inclure les colonnes");
        public static string TablesOnly => T("Tables only", "Tables seulement");
        public static string ExcelColumnsTitle => T("Excel inventory", "Inventaire Excel");

        #endregion

        #region Excel

        public sealed class ExcelTexts
        {
            public string TablesSheet => T("Tables", "Tables");
            public string RelationshipsSheet => T("Relationships", "Relations");
            public string ColumnsSheet => T("Columns", "Colonnes");
            public string DisplayName => T("Display name", "Nom complet");
            public string LogicalName => T("Logical name", "Nom logique");
            public string SchemaName => T("Schema name", "Nom de schéma");
            public string Custom => "Custom";
            public string Managed => T("Managed", "Managée");
            public string Ownership => T("Ownership", "Propriété");
            public string PrimaryKey => T("Primary key", "Clé primaire");
            public string PrimaryName => T("Primary name", "Nom principal");
            public string Prefix => T("Prefix", "Préfixe");
            public string Relationships => T("Relationships on map", "Relations sur la carte");
            public string OutsideFilters => T("Outside filters", "Hors filtres");
            public string Description => "Description";
            public string Type => T("Type", "Type");
            public string OneTable => T("Primary table (1) / Table 1", "Table principale (1) / Table 1");
            public string ManyTable => T("Related table (N) / Table 2", "Table associée (N) / Table 2");
            public string Lookup => T("Lookup column", "Colonne de recherche");
            public string ReferencedColumn => T("Referenced column", "Colonne référencée");
            public string Intersect => T("Intersect table", "Table intersect");
            public string Table => T("Table", "Table");
            public string Required => T("Required", "Requis");
            public string Role => T("Role", "Rôle");
            public string Targets => T("Lookup targets", "Cibles de la recherche");
            public string MaxLength => T("Max length", "Longueur max");
            public string StepsSheet => T("Steps", "Étapes");
            public string OperationsSheet => T("Code operations", "Opérations du code");
            public string Message => "Message";
            public string Stage => T("Stage", "Phase");
            public string Mode => "Mode";
            public string Rank => T("Rank", "Rang");
            public string Handler => T("Plugin type", "Type de plugin");
            public string Assembly => "Assembly";
            public string FilteringAttributes => T("Filtering attributes", "Attributs filtrés");
            public string Images => "Images";
            public string State => T("State", "État");
            public string StepName => T("Step name", "Nom de l'étape");
            public string Operation => T("Operation", "Opération");
            public string Method => T("Found in", "Trouvé dans");
            public string Inferred => T("Inferred", "Déduite");
            public string HandlersSheet => T("Form handlers", "Gestionnaires");
            public string CallsSheet => T("Script calls", "Appels des scripts");
            public string Form => T("Form", "Formulaire");
            public string Event => T("Event", "Événement");
            public string Source => T("Column / tab", "Colonne / onglet");
            public string Function => T("Function", "Fonction");
            public string Library => T("Library", "Bibliothèque");
            public string Line => T("Line", "Ligne");
        }

        public static ExcelTexts Excel { get; } = new ExcelTexts();

        #endregion
    }
}
