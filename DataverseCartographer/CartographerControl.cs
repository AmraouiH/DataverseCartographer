using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DataverseCartographer.Export;
using DataverseCartographer.Forms;
using DataverseCartographer.Localization;
using DataverseCartographer.Models;
using DataverseCartographer.Services;
using DataverseCartographer.UI;
using DataverseCartographer.UI.Controls;
using DataverseCartographer.UI.Rendering;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using XrmToolBox.Extensibility;

namespace DataverseCartographer
{
    /// <summary>
    /// Tool UI: filters, neighborhood exploration, interactive map, table / relationship details and exports.
    /// Metadata reading, graph building, layouts and exports live in the Services and Export folders; this class only orchestrates.
    /// </summary>
    public partial class CartographerControl : PluginControlBase
    {
        private const string LinkedInUrl = "https://www.linkedin.com/in/hamza-amraoui/";
        private const string DonateUrl = "https://www.paypal.com/paypalme/EntityFieldsAnalyser";

        private const int TabColumnsIndex = 0;
        private const int TabRelationsIndex = 1;
        private const int TabKeysIndex = 2;
        private const int TabFormsIndex = 3;
        private const int TabScriptsIndex = 4;
        private const int TabTableStepsIndex = 5;

        /// <summary>Above this many tables the map stays usable but gets hard to read: a hint is shown.</summary>
        private const int LargeGraph = 250;

        /// <summary>Item of the solution / publisher combos (Value null = no filter).</summary>
        private sealed class ComboItem
        {
            public string Text;
            public object Value;
            public override string ToString() => Text;
        }

        private readonly ThemeRenderer _renderer = new ThemeRenderer();
        private PluginSettings _settings = new PluginSettings();
        private OrgModel _model;
        private Graph _graph;
        private string _focus;
        private readonly Stack<string> _focusHistory = new Stack<string>();
        private readonly HashSet<string> _excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _controlLoaded;
        private bool _suppressEvents;
        private int _detailsRequest;
        private int _logicRequest;
        private string _columnsMessage;
        private string _logicMessage;
        private List<GraphNode> _searchHits = new List<GraphNode>();
        private int _searchIndex = -1;
        private TableInfo _searchSuggestion;
        private readonly Timer _searchTimer = new Timer { Interval = 250 };
        private readonly LoadingOverlay _loading = new LoadingOverlay();
        private bool _busy;

        public CartographerControl()
        {
            BuildLayout();
            Controls.Add(_loading);
            ApplyTheme();
            WireEvents();
            WirePluginEvents();
            WireScriptEvents();
            ApplyLanguage();
            ShowDetails();
            HandleCreated += (s, e) => BeginInvoke(new Action(() =>
            {
                RestoreDockOrder(cardDetails, pnlDetailPages, tabsDetails, flpDetailActions, lblCascadeWarning, dgvProperties);
                RestoreDockOrder(cardPluginDetails, pnlPluginPages, tabsPluginDetails, dgvPluginProperties);
                RestoreDockOrder(cardScriptDetails, pnlScriptPages, tabsScriptDetails, dgvScriptProperties);
            }));
            Strings.LanguageChanged += Strings_LanguageChanged;
        }

        #region Theme / language

        private void ApplyTheme()
        {
            tsHeader.Renderer = new FluentHeaderRenderer();
            tsddLanguage.DropDown.Renderer = _renderer;
            tsddExport.DropDown.Renderer = _renderer;
            cmsNode.Renderer = _renderer;

            tsbClose.Image = FluentIcons.Icon(FluentIcons.Close, FluentHeaderRenderer.TextSecondary);
            tsbLoad.Image = FluentIcons.Icon(FluentIcons.Refresh, Color.White);
            tsddExport.Image = FluentIcons.Icon(FluentIcons.Export, Color.FromArgb(16, 124, 65));
            tsddLanguage.Image = FluentIcons.Icon(FluentIcons.Globe, FluentHeaderRenderer.Brand);
            tsbAuthor.Image = FluentIcons.Icon(FluentIcons.Contact, FluentHeaderRenderer.TextSecondary);
            tsbDonate.Image = FluentIcons.Icon(FluentIcons.Heart, Color.FromArgb(196, 49, 75));

            tsmiPng.Image = LineIcons.ToBitmap(IconKind.Image, 16, Theme.Accent);
            tsmiSvg.Image = LineIcons.ToBitmap(IconKind.Image, 16, Theme.Muted);
            tsmiMermaid.Image = LineIcons.ToBitmap(IconKind.Graph, 16, Theme.Accent);
            tsmiPlantUml.Image = LineIcons.ToBitmap(IconKind.Graph, 16, Theme.Muted);
            tsmiCopyMermaid.Image = LineIcons.ToBitmap(IconKind.Copy, 16, Theme.Muted);
            tsmiCopyPlantUml.Image = LineIcons.ToBitmap(IconKind.Copy, 16, Theme.Muted);
            tsmiExcel.Image = LineIcons.ToBitmap(IconKind.List, 16, Theme.Success);

            tsmiExplore1.Image = LineIcons.ToBitmap(IconKind.Target, 16, Theme.Accent);
            tsmiExplore2.Image = LineIcons.ToBitmap(IconKind.Target, 16, Theme.Muted);
            tsmiCenter.Image = LineIcons.ToBitmap(IconKind.Fit, 16, Theme.Muted);
            tsmiHide.Image = LineIcons.ToBitmap(IconKind.Hide, 16, Theme.Muted);
            tsmiCopyLogical.Image = LineIcons.ToBitmap(IconKind.Copy, 16, Theme.Muted);
            tsmiCopySchema.Image = LineIcons.ToBitmap(IconKind.Copy, 16, Theme.Muted);

            lblCascadeWarning.Image = null;
        }

        private void Strings_LanguageChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            ApplyLanguage();
        }

        /// <summary>Applies the current language to every static text, then refreshes the dynamic ones.</summary>
        private void ApplyLanguage()
        {
            _suppressEvents = true;
            try
            {
                tslLogo.Text = Strings.AppTitle;
                tslSubtitle.Text = Strings.AppSubtitle;
                tsbClose.Text = Strings.Close;
                tsbClose.ToolTipText = Strings.CloseTip;
                tsbLoad.Text = Strings.LoadMetadata;
                tsbLoad.ToolTipText = Strings.LoadMetadataTip;
                tsddExport.Text = Strings.Export;
                tsddExport.ToolTipText = Strings.ExportTip;
                tsmiPng.Text = Strings.ExportPng;
                tsmiSvg.Text = Strings.ExportSvg;
                tsmiMermaid.Text = Strings.ExportMermaid;
                tsmiPlantUml.Text = Strings.ExportPlantUml;
                tsmiCopyMermaid.Text = Strings.CopyMermaid;
                tsmiCopyPlantUml.Text = Strings.CopyPlantUml;
                tsmiExcel.Text = Strings.ExportExcel;
                tsddLanguage.Text = Strings.IsFrench ? Strings.FrenchName : Strings.EnglishName;
                tsddLanguage.ToolTipText = Strings.LanguageMenu;
                tsmiEnglish.Checked = !Strings.IsFrench;
                tsmiFrench.Checked = Strings.IsFrench;
                tsbAuthor.Text = Strings.AuthorName;
                tsbAuthor.ToolTipText = Strings.AuthorTip;
                tsbDonate.Text = Strings.Donate;
                tsbDonate.ToolTipText = Strings.DonateTip;
                RefreshConnectionLabel();

                cardFilters.Title = Strings.FiltersTitle;
                cardFilters.Subtitle = Strings.FiltersSubtitle;
                lblSolution.Text = Strings.SolutionLabel;
                lblPublisher.Text = Strings.PublisherLabel;
                lblTableKind.Text = Strings.TablesLabel;
                Refill(cboTableKind, Strings.TableKinds);
                chkHideSystem.Text = Strings.HideSystem;
                chkHideIsolated.Text = Strings.HideIsolated;
                chkOnlyUsed.Text = Strings.OnlyUsedTables;
                toolTip.SetToolTip(chkOnlyUsed, Strings.OnlyUsedTablesTip);
                chkManyToMany.Text = Strings.ShowManyToMany;
                chkSelf.Text = Strings.ShowSelf;
                toolTip.SetToolTip(chkHideSystem, Strings.HideSystemTip);
                toolTip.SetToolTip(chkHideIsolated, Strings.HideIsolatedTip);
                toolTip.SetToolTip(chkSelf, Strings.ShowSelfTip);
                FillSolutions();
                FillPublishers();

                cardExplore.Title = Strings.ExploreTitle;
                cardExplore.Subtitle = Strings.ExploreSubtitle;
                lblFocus.Text = Strings.FocusLabel;
                lblDepth.Text = Strings.DepthLabel;
                btnExplore.Text = Strings.ShowFocus;
                chkBoundary.Text = Strings.ShowBoundary;
                toolTip.SetToolTip(chkBoundary, Strings.ShowBoundaryTip);
                btnBack.Text = Strings.PreviousFocus;
                btnClearFocus.Text = Strings.ClearFocus;
                lnkShowHidden.Text = Strings.ShowHiddenTables;
                lblExploreHint.Text = Strings.ExploreHint;
                Theme.SetCue(cboFocus, Strings.FocusCue);

                lblLayout.Text = Strings.LayoutLabel;
                Refill(cboLayout, Strings.Layouts);
                btnRelayout.Text = Strings.Relayout;
                toolTip.SetToolTip(btnRelayout, Strings.RelayoutTip);
                toolTip.SetToolTip(btnFit, Strings.FitTip);
                toolTip.SetToolTip(btnZoomIn, Strings.ZoomInTip);
                toolTip.SetToolTip(btnZoomOut, Strings.ZoomOutTip);
                chkLabels.Text = Strings.Labels;
                toolTip.SetToolTip(chkLabels, Strings.LabelsTip);
                Theme.SetCue(txtSearch, Strings.SearchCue);

                tsmiExplore1.Text = Strings.MenuExplore(1);
                tsmiExplore2.Text = Strings.MenuExplore(2);
                tsmiCenter.Text = Strings.MenuCenter;
                tsmiHide.Text = Strings.MenuHide;
                tsmiCopyLogical.Text = Strings.MenuCopyLogicalName;
                tsmiCopySchema.Text = Strings.MenuCopySchemaName;

                btnFocusHere.Text = Strings.FocusHere;
                toolTip.SetToolTip(btnFocusHere, Strings.FocusHereTip);
                btnHideTable.Text = Strings.HideTable;
                toolTip.SetToolTip(btnHideTable, Strings.HideTableTip);
                btnGoToTable.Text = Strings.ShowTable;
                tabsDetails.Tabs[TabColumnsIndex].Text = Strings.TabColumns;
                tabsDetails.Tabs[TabRelationsIndex].Text = Strings.TabRelationships;
                tabsDetails.Tabs[TabKeysIndex].Text = Strings.TabKeys;
                tabsDetails.Invalidate();

                dgvColumns.Columns["colColDisplay"].HeaderText = Strings.ColDisplayName;
                dgvColumns.Columns["colColLogical"].HeaderText = Strings.ColLogicalName;
                dgvColumns.Columns["colColType"].HeaderText = Strings.ColType;
                dgvColumns.Columns["colColRequired"].HeaderText = Strings.ColRequired;
                dgvColumns.Columns["colColCustom"].HeaderText = Strings.ColCustom;
                dgvRelations.Columns["colRelKind"].HeaderText = Strings.ColRelKind;
                dgvRelations.Columns["colRelRelated"].HeaderText = Strings.ColRelated;
                dgvRelations.Columns["colRelVia"].HeaderText = Strings.ColVia;
                dgvRelations.Columns["colRelDelete"].HeaderText = Strings.ColDelete;
                dgvKeys.Columns["colKeyName"].HeaderText = Strings.ColKeyName;
                dgvKeys.Columns["colKeyColumns"].HeaderText = Strings.ColKeyColumns;
                dgvKeys.Columns["colKeyStatus"].HeaderText = Strings.ColKeyStatus;
                tabsDetails.Tabs[TabFormsIndex].Text = Strings.TabForms;
                tabsDetails.Tabs[TabScriptsIndex].Text = Strings.TabScripts;
                tabsDetails.Tabs[TabTableStepsIndex].Text = Strings.TabPlugins;
                tabsDetails.UpdateHeight();
                SetHeaders(dgvForms, Strings.ColFormName, Strings.ColFormType, Strings.ColState, Strings.ColLibraries, Strings.ColHandlers);
                SetHeaders(dgvScripts, Strings.ColLibrary, Strings.ColEvent, Strings.ColFunction, Strings.ColForm);
                SetHeaders(dgvSteps, Strings.ColMessage, Strings.ColStage, Strings.ColMode, Strings.ColRank, Strings.ColPlugin, Strings.ColFilteringAttributes, Strings.ColState);
                canvas.Legend = new[]
                {
                    new GraphCanvas.LegendItem(Strings.LegendCustom, Theme.CustomTable, GraphCanvas.LegendStyle.Dot),
                    new GraphCanvas.LegendItem(Strings.LegendStandard, Theme.StandardTable, GraphCanvas.LegendStyle.Dot),
                    new GraphCanvas.LegendItem(Strings.LegendFocus, Theme.FocusTable, GraphCanvas.LegendStyle.Dot),
                    new GraphCanvas.LegendItem(Strings.LegendOneToMany, Theme.Edge, GraphCanvas.LegendStyle.Line),
                    new GraphCanvas.LegendItem(Strings.LegendManyToMany, Theme.EdgeManyToMany, GraphCanvas.LegendStyle.Dashed),
                    new GraphCanvas.LegendItem(Strings.LegendCascadeDelete, GraphRenderer.CascadeDeleteEdge, GraphCanvas.LegendStyle.Line)
                };
                ApplyPluginsLanguage();
                ApplyScriptsLanguage();
            }
            finally
            {
                _suppressEvents = false;
            }

            canvas.EmptyText = _model == null ? Strings.EmptyNotLoaded : Strings.EmptyNoTables;
            canvas.Invalidate();
            UpdateStatus();
            UpdateSearch();
            ShowDetails();
        }

        /// <summary>Replaces the items of a fixed list, keeping the selected index.</summary>
        private static void Refill(ComboBox combo, string[] items)
        {
            var index = combo.SelectedIndex;
            combo.BeginUpdate();
            combo.Items.Clear();
            combo.Items.AddRange(items);
            combo.SelectedIndex = index >= 0 && index < items.Length ? index : 0;
            combo.EndUpdate();
        }

        #endregion

        #region Events

        private void WireEvents()
        {
            Load += CartographerControl_Load;

            tsbClose.Click += (s, e) => CloseTool();
            tsbLoad.Click += (s, e) =>
            {
                if (tabsMain.SelectedIndex == PagePlugins) ExecuteMethod(LoadPlugins);
                else if (tabsMain.SelectedIndex == PageScripts) ExecuteMethod(LoadScripts);
                else ExecuteMethod(LoadMetadata);
            };
            tsmiPng.Click += (s, e) => ExportFile(ExportKind.Png);
            tsmiSvg.Click += (s, e) => ExportFile(ExportKind.Svg);
            tsmiMermaid.Click += (s, e) => ExportFile(ExportKind.Mermaid);
            tsmiPlantUml.Click += (s, e) => ExportFile(ExportKind.PlantUml);
            tsmiExcel.Click += (s, e) => ExportExcel();
            tsmiCopyMermaid.Click += (s, e) => CopyDiagram(false);
            tsmiCopyPlantUml.Click += (s, e) => CopyDiagram(true);
            tsmiEnglish.Click += (s, e) => SetLanguage(Language.English);
            tsmiFrench.Click += (s, e) => SetLanguage(Language.French);
            tsbAuthor.Click += (s, e) => OpenUrl(LinkedInUrl);
            tsbDonate.Click += (s, e) => OpenUrl(DonateUrl);

            cboSolution.SelectedIndexChanged += (s, e) => OnFilterChanged();
            cboPublisher.SelectedIndexChanged += (s, e) => OnFilterChanged();
            cboTableKind.SelectedIndexChanged += (s, e) => OnFilterChanged();
            chkHideSystem.CheckedChanged += (s, e) => OnFilterChanged();
            chkHideIsolated.CheckedChanged += (s, e) => OnFilterChanged();
            chkOnlyUsed.CheckedChanged += (s, e) => OnFilterChanged();
            chkManyToMany.CheckedChanged += (s, e) => OnFilterChanged();
            chkSelf.CheckedChanged += (s, e) => OnFilterChanged();
            chkBoundary.CheckedChanged += (s, e) => OnFilterChanged();
            numDepth.ValueChanged += (s, e) =>
            {
                if (_focus != null) OnFilterChanged();
                else SaveSettings();
            };

            btnExplore.Click += (s, e) => ExploreFromCombo();
            cboFocus.SelectionChangeCommitted += (s, e) => BeginInvoke(new Action(ExploreFromCombo));
            cboFocus.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                ExploreFromCombo();
            };
            btnClearFocus.Click += (s, e) => SetFocusTable(null);
            btnBack.Click += (s, e) => GoBack();
            lnkShowHidden.LinkClicked += (s, e) =>
            {
                _excluded.Clear();
                RebuildGraph(true);
            };

            cboLayout.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressEvents) return;
                SaveSettings();
                Relayout();
            };
            btnRelayout.Click += (s, e) => Relayout();
            btnFit.Click += (s, e) => canvas.FitToView();
            btnZoomIn.Click += (s, e) => canvas.ZoomBy(1.25f);
            btnZoomOut.Click += (s, e) => canvas.ZoomBy(1 / 1.25f);
            chkLabels.CheckedChanged += (s, e) =>
            {
                canvas.ShowLabels = chkLabels.Checked;
                SaveSettings();
            };

            txtSearch.TextChanged += (s, e) =>
            {
                _searchTimer.Stop();
                _searchTimer.Start();
            };
            _searchTimer.Tick += (s, e) =>
            {
                _searchTimer.Stop();
                UpdateSearch();
            };
            txtSearch.KeyDown += TxtSearch_KeyDown;

            canvas.SelectionChanged += (s, e) =>
            {
                ShowDetails();
                RestoreDockOrder(cardDetails, pnlDetailPages, tabsDetails, flpDetailActions, lblCascadeWarning, dgvProperties);
            };
            canvas.NodeDoubleClicked += (s, node) => SetFocusTable(node.Table.LogicalName);
            canvas.NodeMenuRequested += (s, e) => cmsNode.Show(canvas, e.Location);
            canvas.Resize += (s, e) =>
            {
                if (_graph != null && canvas.ZoomLevel == 1f) canvas.FitToView();
            };

            tsmiExplore1.Click += (s, e) => ExploreSelected(1);
            tsmiExplore2.Click += (s, e) => ExploreSelected(2);
            tsmiCenter.Click += (s, e) => canvas.CenterOn(canvas.SelectedNode);
            tsmiHide.Click += (s, e) => HideSelected();
            tsmiCopyLogical.Click += (s, e) => CopyText(canvas.SelectedNode?.Table.LogicalName);
            tsmiCopySchema.Click += (s, e) => CopyText(canvas.SelectedNode?.Table.SchemaName);

            btnFocusHere.Click += (s, e) =>
            {
                if (canvas.SelectedNode != null) SetFocusTable(canvas.SelectedNode.Table.LogicalName);
            };
            btnHideTable.Click += (s, e) => HideSelected();
            btnGoToTable.Click += (s, e) => GoToRelationTable();
            tabsDetails.SelectedIndexChanged += (s, e) => ShowDetailPage();
            dgvRelations.CellDoubleClick += DgvRelations_CellDoubleClick;
            dgvSteps.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && dgvSteps.Rows[e.RowIndex].Tag is StepInfo step && step.PluginTypeId.HasValue) GoToPlugin(step.PluginTypeId.Value);
            };
        }

        private void OnFilterChanged()
        {
            if (_suppressEvents || !_controlLoaded) return;
            SaveSettings();
            RebuildGraph(true);
        }

        #endregion

        #region Lifecycle / connection

        private void CartographerControl_Load(object sender, EventArgs e)
        {
            if (!SettingsManager.Instance.TryLoad(GetType(), out _settings) || _settings == null)
            {
                _settings = new PluginSettings();
                LogInfo("Default settings created.");
            }

            Strings.Current = Strings.FromCode(_settings.Language);
            ApplyLanguage();

            _suppressEvents = true;
            cboLayout.SelectedIndex = Clamp(_settings.Layout, cboLayout.Items.Count);
            cboTableKind.SelectedIndex = Clamp(_settings.TableKind, cboTableKind.Items.Count);
            chkHideSystem.Checked = _settings.HideSystemTables;
            chkHideIsolated.Checked = _settings.HideIsolated;
            chkOnlyUsed.Checked = _settings.OnlyUsedTables;
            chkManyToMany.Checked = _settings.ShowManyToMany;
            chkSelf.Checked = _settings.ShowSelfReferences;
            chkBoundary.Checked = _settings.ShowBoundary;
            chkLabels.Checked = _settings.ShowLabels;
            canvas.ShowLabels = _settings.ShowLabels;
            numDepth.Value = Math.Max(numDepth.Minimum, Math.Min(numDepth.Maximum, _settings.Depth));
            cboPluginLayout.SelectedIndex = Clamp(_settings.PluginLayout, cboPluginLayout.Items.Count);
            chkHideMicrosoft.Checked = _settings.HideMicrosoftPlugins;
            chkIncludeDisabled.Checked = _settings.IncludeDisabledSteps;
            chkShowReads.Checked = _settings.ShowPluginReads;
            chkAnalyzeCode.Checked = _settings.AnalyzePluginCode;
            cboScriptLayout.SelectedIndex = Clamp(_settings.ScriptLayout, cboScriptLayout.Items.Count);
            chkHideMsScripts.Checked = _settings.HideMicrosoftScripts;
            chkInactiveForms.Checked = _settings.IncludeInactiveForms;
            chkScriptReads.Checked = _settings.ShowScriptReads;
            chkAnalyzeScripts.Checked = _settings.AnalyzeScripts;
            _suppressEvents = false;

            _controlLoaded = true;

            // Deferred: requesting a connection synchronously in Load makes XrmToolBox register the tab twice.
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed) ExecuteMethod(LoadMetadata);
            }));
        }

        private static int Clamp(int index, int count) => index >= 0 && index < count ? index : 0;

        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            _model = null;
            _graph = null;
            _focus = null;
            _focusHistory.Clear();
            _excluded.Clear();
            canvas.Graph = null;
            _plugins = null;
            _pluginGraph = null;
            _pluginFocus = null;
            pluginCanvas.Graph = null;
            _scripts = null;
            _scriptGraph = null;
            _scriptFocus = null;
            scriptCanvas.Graph = null;
            FillSolutions();
            FillPublishers();
            FillFocusTables();
            ShowDetails();
            UpdateStatus();
            UpdateExploreState();

            // When actionName is set (connection requested by ExecuteMethod), the base class replays the action.
            base.UpdateConnection(newService, detail, actionName, parameter);
            RefreshConnectionLabel();

            if (detail != null) LogInfo("Connection: {0}", detail.WebApplicationUrl);

            if (_controlLoaded && string.IsNullOrEmpty(actionName) && newService != null)
            {
                LoadMetadata();
            }
        }

        private void RefreshConnectionLabel()
        {
            tslConnection.Text = ConnectionDetail == null ? Strings.NotConnected : Strings.ConnectedTo(ConnectionDetail.ConnectionName);
        }

        private string OrganizationName => ConnectionDetail?.OrganizationFriendlyName ?? ConnectionDetail?.ConnectionName;

        private void SetLanguage(Language language)
        {
            _settings.Language = Strings.ToCode(language);
            Strings.Current = language;
            SaveSettings();
        }

        private void SaveSettings()
        {
            if (_settings == null || !_controlLoaded) return;
            _settings.Layout = cboLayout.SelectedIndex;
            _settings.TableKind = cboTableKind.SelectedIndex;
            _settings.HideSystemTables = chkHideSystem.Checked;
            _settings.HideIsolated = chkHideIsolated.Checked;
            _settings.OnlyUsedTables = chkOnlyUsed.Checked;
            _settings.ShowManyToMany = chkManyToMany.Checked;
            _settings.ShowSelfReferences = chkSelf.Checked;
            _settings.ShowBoundary = chkBoundary.Checked;
            _settings.ShowLabels = chkLabels.Checked;
            _settings.Depth = (int)numDepth.Value;
            _settings.PluginLayout = cboPluginLayout.SelectedIndex;
            _settings.HideMicrosoftPlugins = chkHideMicrosoft.Checked;
            _settings.IncludeDisabledSteps = chkIncludeDisabled.Checked;
            _settings.ShowPluginReads = chkShowReads.Checked;
            _settings.AnalyzePluginCode = chkAnalyzeCode.Checked;
            _settings.ScriptLayout = cboScriptLayout.SelectedIndex;
            _settings.HideMicrosoftScripts = chkHideMsScripts.Checked;
            _settings.IncludeInactiveForms = chkInactiveForms.Checked;
            _settings.ShowScriptReads = chkScriptReads.Checked;
            _settings.AnalyzeScripts = chkAnalyzeScripts.Checked;
            SettingsManager.Instance.Save(GetType(), _settings);
        }

        #endregion

        #region Metadata

        private void LoadMetadata()
        {
            RunWork(new WorkAsyncInfo
            {
                Message = Strings.LoadingMetadata,
                Work = (worker, args) =>
                {
                    var service = new MetadataService(Service);
                    args.Result = service.LoadModel(m => worker.ReportProgress(0, m));
                    if (service.Warning != null) LogWarning("RetrieveMetadataChanges failed, full metadata used: {0}", service.Warning);
                },
                ProgressChanged = e => _loading.SetMessage(e.UserState as string ?? Strings.LoadingMetadata),
                PostWorkCallBack = e =>
                {
                    if (e.Error != null)
                    {
                        ShowError(Strings.LoadFailed, e.Error);
                        return;
                    }
                    _model = (OrgModel)e.Result;
                    _focus = null;
                    _focusHistory.Clear();
                    _excluded.Clear();
                    LogInfo("Metadata loaded: {0} tables, {1} relationships, {2} solutions.", _model.Tables.Count, _model.Relations.Count, _model.Solutions.Count);

                    _suppressEvents = true;
                    FillSolutions();
                    FillPublishers();
                    FillFocusTables();
                    FillPluginSolutions();
                    FillScriptSolutions();
                    _suppressEvents = false;
                    canvas.EmptyText = Strings.EmptyNoTables;
                    RebuildGraph(true);
                }
            });
        }

        private void FillSolutions()
        {
            var previous = (cboSolution.SelectedItem as ComboItem)?.Value as SolutionItem;
            var wasSuppressed = _suppressEvents;
            _suppressEvents = true;
            cboSolution.BeginUpdate();
            cboSolution.Items.Clear();
            cboSolution.Items.Add(new ComboItem { Text = Strings.AllSolutions });
            foreach (var solution in _model?.Solutions ?? new List<SolutionItem>())
            {
                cboSolution.Items.Add(new ComboItem { Text = solution.ToString(), Value = solution });
            }
            var index = previous == null ? 0 : cboSolution.Items.Cast<ComboItem>().ToList().FindIndex(i => (i.Value as SolutionItem)?.Id == previous.Id);
            cboSolution.SelectedIndex = Math.Max(0, index);
            cboSolution.EndUpdate();
            _suppressEvents = wasSuppressed;
        }

        private void FillPublishers()
        {
            var previous = (cboPublisher.SelectedItem as ComboItem)?.Value as string;
            var wasSuppressed = _suppressEvents;
            _suppressEvents = true;
            cboPublisher.BeginUpdate();
            cboPublisher.Items.Clear();
            cboPublisher.Items.Add(new ComboItem { Text = Strings.AllPublishers });
            if (_model != null)
            {
                foreach (var prefix in _model.UsedPrefixes())
                {
                    _model.Publishers.TryGetValue(prefix, out var name);
                    var count = _model.Tables.Values.Count(t => t.IsCustom && string.Equals(t.Prefix, prefix, StringComparison.OrdinalIgnoreCase) && !t.IsIntersect && !t.IsPrivate);
                    if (count > 0) cboPublisher.Items.Add(new ComboItem { Text = Strings.PublisherItem(prefix, name, count), Value = prefix });
                }
            }
            var index = previous == null ? 0 : cboPublisher.Items.Cast<ComboItem>().ToList().FindIndex(i => i.Value as string == previous);
            cboPublisher.SelectedIndex = Math.Max(0, index);
            cboPublisher.EndUpdate();
            _suppressEvents = wasSuppressed;
        }

        private void FillFocusTables()
        {
            cboFocus.BeginUpdate();
            cboFocus.Items.Clear();
            if (_model != null)
            {
                cboFocus.Items.AddRange(_model.Tables.Values
                    .Where(t => !t.IsPrivate && !t.IsIntersect)
                    .OrderBy(t => t.Label, StringComparer.CurrentCultureIgnoreCase)
                    .Cast<object>()
                    .ToArray());
            }
            cboFocus.EndUpdate();
            cboFocus.Text = _model?.Find(_focus)?.ToString() ?? string.Empty;
        }

        #endregion

        #region Graph

        private GraphFilter CurrentFilter(HashSet<Guid> solutionTables) => new GraphFilter
        {
            SolutionTables = solutionTables,
            Prefix = (cboPublisher.SelectedItem as ComboItem)?.Value as string,
            Kind = (TableKindFilter)Math.Max(0, cboTableKind.SelectedIndex),
            HideSystemTables = chkHideSystem.Checked,
            HideIsolated = chkHideIsolated.Checked,
            OnlyUsedTables = chkOnlyUsed.Checked,
            ShowManyToMany = chkManyToMany.Checked,
            ShowSelfReferences = chkSelf.Checked,
            ShowBoundary = chkBoundary.Checked,
            Focus = _focus,
            Depth = (int)numDepth.Value,
            Excluded = new HashSet<string>(_excluded, StringComparer.OrdinalIgnoreCase)
        };

        private SolutionItem SelectedSolution => (cboSolution.SelectedItem as ComboItem)?.Value as SolutionItem;

        private LayoutKind SelectedLayout => (LayoutKind)Math.Max(0, cboLayout.SelectedIndex);

        /// <summary>
        /// Rebuilds the graph from the filters. The tables of a solution are read once (asynchronously) then cached.
        /// With <paramref name="relayout"/> false, the tables already on the map keep their position.
        /// </summary>
        private void RebuildGraph(bool relayout)
        {
            if (_model == null)
            {
                canvas.Graph = null;
                UpdateStatus();
                return;
            }

            var solution = SelectedSolution;
            HashSet<Guid> solutionTables = null;
            if (solution != null && !_model.SolutionTables.TryGetValue(solution.Id, out solutionTables))
            {
                var model = _model;
                RunWork(new WorkAsyncInfo
                {
                    Message = Strings.ReadingSolutionTables,
                    Work = (worker, args) => args.Result = new MetadataService(Service).LoadSolutionTables(solution.Id),
                    PostWorkCallBack = e =>
                    {
                        if (e.Error != null)
                        {
                            ShowError(Strings.SolutionFailed, e.Error);
                            return;
                        }
                        model.SolutionTables[solution.Id] = (HashSet<Guid>)e.Result;
                        if (model == _model) RebuildGraph(relayout);
                    }
                });
                return;
            }

            if (chkOnlyUsed.Checked && !_model.UsageLoaded)
            {
                var usageModel = _model;
                RunWork(new WorkAsyncInfo
                {
                    Message = Strings.CheckingUsageTitle,
                    Work = (worker, args) => new MetadataService(Service).LoadUsage(usageModel, m => worker.ReportProgress(0, m)),
                    ProgressChanged = e => _loading.SetMessage(e.UserState as string ?? Strings.CheckingUsageTitle),
                    PostWorkCallBack = e =>
                    {
                        if (e.Error != null)
                        {
                            usageModel.UsageLoaded = true; // never loop: unknown tables stay visible
                            ShowError(Strings.UsageFailed, e.Error);
                        }
                        var tables = usageModel.Tables.Values.Where(t => !t.IsPrivate && !t.IsIntersect).ToList();
                        LogInfo("Table usage: {0} used, {1} empty, {2} unknown.", tables.Count(t => t.HasActiveRecords == true),
                            tables.Count(t => t.HasActiveRecords == false), tables.Count(t => t.HasActiveRecords == null));
                        if (usageModel == _model) RebuildGraph(relayout);
                    }
                });
                return;
            }

            var previous = _graph;
            var selected = canvas.SelectedNode?.Table.LogicalName;
            var graph = GraphBuilder.Build(_model, CurrentFilter(solutionTables));

            var keepPositions = !relayout && previous != null && graph.Nodes.All(n => previous.Find(n.Table.LogicalName) != null);
            if (keepPositions)
            {
                foreach (var node in graph.Nodes)
                {
                    var old = previous.Find(node.Table.LogicalName);
                    node.X = old.X;
                    node.Y = old.Y;
                }
            }
            else
            {
                GraphLayout.Apply(graph, SelectedLayout);
            }

            _graph = graph;
            canvas.Graph = graph;
            if (!keepPositions) canvas.FitToView();

            var focusNode = graph.Nodes.FirstOrDefault(n => n.IsFocus);
            var reselect = graph.Find(selected) ?? focusNode;
            if (reselect != null) canvas.SelectNode(reselect);
            else ShowDetails();

            UpdateStatus();
            UpdateExploreState();
            UpdateSearch();
        }

        private void Relayout()
        {
            if (_graph == null) return;
            GraphLayout.Apply(_graph, SelectedLayout);
            canvas.FitToView();
        }

        private void UpdateStatus()
        {
            if (_model == null || _graph == null)
            {
                lblStatus.Text = string.Empty;
                return;
            }
            var total = _model.Tables.Values.Count(t => !t.IsPrivate && !t.IsIntersect);
            var text = Strings.GraphStatus(_graph.Nodes.Count, _graph.Edges.Count, total);
            var focus = _model.Find(_focus);
            if (focus != null) text += Strings.FocusStatus(focus.Label, (int)numDepth.Value);
            if (chkOnlyUsed.Checked && _model.UsageLoaded) text += Strings.EmptyTablesHidden(_model.Tables.Values.Count(t => !t.IsPrivate && !t.IsIntersect && t.HasActiveRecords == false));
            if (_graph.Nodes.Count > LargeGraph) text += Strings.LargeGraphWarning;
            lblStatus.Text = text;
        }

        #endregion

        #region Explore

        private void ExploreFromCombo()
        {
            if (_model == null) return;
            var table = cboFocus.SelectedItem as TableInfo ?? FindTable(cboFocus.Text);
            if (table == null)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            SetFocusTable(table.LogicalName);
        }

        /// <summary>Finds a table from what the user typed: logical name, display name or « Display (logical) ».</summary>
        private TableInfo FindTable(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || _model == null) return null;
            var tables = _model.Tables.Values.Where(t => !t.IsPrivate && !t.IsIntersect).ToList();
            return tables.FirstOrDefault(t => string.Equals(t.LogicalName, text, StringComparison.OrdinalIgnoreCase))
                   ?? tables.FirstOrDefault(t => string.Equals(t.ToString(), text, StringComparison.OrdinalIgnoreCase))
                   ?? tables.FirstOrDefault(t => string.Equals(t.DisplayName, text, StringComparison.CurrentCultureIgnoreCase));
        }

        private void SetFocusTable(string logicalName, bool remember = true)
        {
            if (_model == null) return;
            if (string.Equals(logicalName, _focus, StringComparison.OrdinalIgnoreCase))
            {
                RebuildGraph(true);
                return;
            }
            if (remember) _focusHistory.Push(_focus);
            _focus = logicalName;
            if (logicalName != null) _excluded.Remove(logicalName);
            cboFocus.Text = _model.Find(_focus)?.ToString() ?? string.Empty;
            RebuildGraph(true);
        }

        private void GoBack()
        {
            if (_focusHistory.Count == 0) return;
            SetFocusTable(_focusHistory.Pop(), false);
        }

        private void ExploreSelected(int depth)
        {
            var node = canvas.SelectedNode;
            if (node == null) return;
            _suppressEvents = true;
            numDepth.Value = depth;
            _suppressEvents = false;
            SaveSettings();
            SetFocusTable(node.Table.LogicalName);
        }

        private void HideSelected()
        {
            var node = canvas.SelectedNode;
            if (node == null) return;
            _excluded.Add(node.Table.LogicalName);
            if (string.Equals(node.Table.LogicalName, _focus, StringComparison.OrdinalIgnoreCase))
            {
                _focus = null;
                cboFocus.Text = string.Empty;
                RebuildGraph(true);
                return;
            }
            canvas.SelectNode(null);
            RebuildGraph(false);
        }

        private void UpdateExploreState()
        {
            btnBack.Enabled = _focusHistory.Count > 0;
            var previous = _focusHistory.Count > 0 ? _model?.Find(_focusHistory.Peek())?.Label : null;
            toolTip.SetToolTip(btnBack, _focusHistory.Count > 0 ? Strings.PreviousFocusTip(previous ?? Strings.ClearFocus) : null);
            btnClearFocus.Enabled = _focus != null;
            lblHidden.Visible = lnkShowHidden.Visible = _excluded.Count > 0;
            lblHidden.Text = Strings.HiddenTables(_excluded.Count);
        }

        #endregion

        #region Search

        private void UpdateSearch()
        {
            var text = txtSearch.Text.Trim();
            _searchHits = new List<GraphNode>();
            _searchIndex = -1;
            _searchSuggestion = null;
            if (text.Length == 0 || _graph == null)
            {
                canvas.SetSearchHits(null);
                lblSearchInfo.Text = string.Empty;
                return;
            }

            bool Match(string value) => value != null && value.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0;
            _searchHits = _graph.Nodes.Where(node =>
                    Match(node.Table.LogicalName) || Match(node.Table.DisplayName) || Match(node.Table.SchemaName)
                    || _graph.Edges.Any(e => e.Target == node && Match(e.Relation.ToAttribute))
                    || node.Table.Details != null && node.Table.Details.Columns.Any(c => Match(c.LogicalName) || Match(c.DisplayName)))
                .OrderByDescending(n => string.Equals(n.Table.LogicalName, text, StringComparison.OrdinalIgnoreCase))
                .ThenBy(n => n.Table.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            canvas.SetSearchHits(_searchHits);

            if (_searchHits.Count > 0)
            {
                lblSearchInfo.Text = Strings.SearchHits(_searchHits.Count);
                return;
            }

            _searchSuggestion = FindTable(text) ?? _model?.Tables.Values
                .Where(t => !t.IsPrivate && !t.IsIntersect && (Match(t.LogicalName) || Match(t.DisplayName)))
                .OrderBy(t => t.LogicalName.Length)
                .FirstOrDefault();
            lblSearchInfo.Text = _searchSuggestion != null ? Strings.SearchNotDisplayed(_searchSuggestion.Label) : Strings.SearchNoMatch;
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                txtSearch.Clear();
                return;
            }
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _searchTimer.Stop();
            if (_searchIndex < 0) UpdateSearch();

            if (_searchHits.Count > 0)
            {
                _searchIndex = (_searchIndex + 1) % _searchHits.Count;
                var node = _searchHits[_searchIndex];
                canvas.SelectNode(node);
                canvas.CenterOn(node);
                lblSearchInfo.Text = $"{_searchIndex + 1}/{_searchHits.Count}";
            }
            else if (_searchSuggestion != null)
            {
                SetFocusTable(_searchSuggestion.LogicalName);
            }
        }

        #endregion

        #region Details

        private void ShowDetails()
        {
            var node = canvas.SelectedNode;
            var edge = canvas.SelectedEdge;
            dgvProperties.Rows.Clear();
            lblCascadeWarning.Visible = false;

            if (node != null)
            {
                ShowTableDetails(node);
                return;
            }
            if (edge != null)
            {
                ShowRelationDetails(edge.Relation);
                return;
            }

            cardDetails.Title = Strings.DetailsTitle;
            cardDetails.Subtitle = Strings.DetailsSubtitle;
            dgvProperties.Visible = false;
            flpDetailActions.Visible = false;
            tabsDetails.Visible = false;
            pnlDetailPages.Visible = false;
        }

        private void ShowTableDetails(GraphNode node)
        {
            var table = node.Table;
            cardDetails.Title = table.Label;
            cardDetails.Subtitle = $"{table.LogicalName}  ·  {(table.IsCustom ? Strings.CustomTable : Strings.StandardTable)}{(table.IsManaged ? Strings.ManagedSuffix : Strings.UnmanagedSuffix)}";

            AddProperty(Strings.PropSchemaName, table.SchemaName);
            AddProperty(Strings.PropOwnership, Strings.OwnershipLabel(table.Ownership));
            AddProperty(Strings.PropPrimaryKey, table.PrimaryIdAttribute);
            AddProperty(Strings.PropPrimaryName, table.PrimaryNameAttribute);
            if (!string.IsNullOrEmpty(table.Prefix)) AddProperty(Strings.PropPrefix, table.Prefix + "_");
            if (table.ObjectTypeCode.HasValue) AddProperty(Strings.PropObjectTypeCode, table.ObjectTypeCode.Value.ToString());
            if (table.IsActivity) AddProperty(Strings.PropActivity, Strings.Yes);
            if (table.RecordCount.HasValue || table.HasActiveRecords.HasValue) AddProperty(Strings.PropRecords, Strings.RecordsValue(table.RecordCount, table.HasActiveRecords));
            AddProperty(Strings.PropRelations, Strings.PropRelationsValue(
                table.Relations.Count(r => r.Kind == RelationKind.OneToMany && string.Equals(r.From, table.LogicalName, StringComparison.OrdinalIgnoreCase)),
                table.Relations.Count(r => r.Kind == RelationKind.OneToMany && !r.IsSelf && string.Equals(r.To, table.LogicalName, StringComparison.OrdinalIgnoreCase)),
                table.Relations.Count(r => r.Kind == RelationKind.ManyToMany)));
            if (!string.IsNullOrEmpty(table.Description)) AddProperty(Strings.PropDescription, table.Description);
            FitProperties();

            dgvProperties.Visible = true;
            flpDetailActions.Visible = true;
            btnFocusHere.Visible = true;
            btnHideTable.Visible = true;
            btnGoToTable.Visible = false;
            tabsDetails.Visible = true;
            pnlDetailPages.Visible = true;

            RestoreDockOrder(cardDetails, pnlDetailPages, tabsDetails, flpDetailActions, lblCascadeWarning, dgvProperties);
            FillRelations(table);
            if (table.Details != null) FillColumns(table);
            else LoadDetailsAsync(table);
            if (table.Logic != null) FillLogic(table);
            else LoadLogicAsync(table);
            ShowDetailPage();
        }

        private void ShowRelationDetails(RelationInfo relation)
        {
            cardDetails.Title = relation.SchemaName;
            cardDetails.Subtitle = Strings.RelationshipSubtitle(relation);

            AddProperty(Strings.PropKind, $"{relation.KindLabel}  ·  {(relation.IsCustom ? Strings.CustomRelationship : Strings.SystemRelationship)}{(relation.IsManaged ? Strings.ManagedSuffix : Strings.UnmanagedSuffix)}");
            if (relation.Kind == RelationKind.ManyToMany)
            {
                AddProperty(Strings.PropEntity1, TableLabel(relation.From));
                AddProperty(Strings.PropEntity2, TableLabel(relation.To));
                AddProperty(Strings.PropIntersect, relation.Intersect);
            }
            else
            {
                AddProperty(Strings.PropOneSide, TableLabel(relation.From));
                AddProperty(Strings.PropManySide, TableLabel(relation.To));
                AddProperty(Strings.PropLookup, $"{relation.To}.{relation.ToAttribute}");
                AddProperty(Strings.PropReferenced, $"{relation.From}.{relation.FromAttribute}");
                var cascade = relation.Cascade;
                if (cascade != null)
                {
                    AddProperty(Strings.CascadeHeader, string.Empty, true);
                    AddProperty("Delete", cascade.Delete);
                    AddProperty("Assign", cascade.Assign);
                    AddProperty("Share", cascade.Share);
                    AddProperty("Unshare", cascade.Unshare);
                    AddProperty("Reparent", cascade.Reparent);
                    AddProperty("Merge", cascade.Merge);
                    AddProperty("Rollup view", cascade.RollupView);

                    if (cascade.CascadesDelete || cascade.RestrictsDelete)
                    {
                        lblCascadeWarning.Text = cascade.CascadesDelete ? Strings.CascadeDeleteWarning : Strings.CascadeRestrictInfo;
                        lblCascadeWarning.BackColor = cascade.CascadesDelete ? Theme.DangerSoft : Theme.WarningSoft;
                        lblCascadeWarning.ForeColor = cascade.CascadesDelete ? Theme.Danger : Theme.WarningText;
                        lblCascadeWarning.Visible = true;
                    }
                }
            }
            FitProperties();

            dgvProperties.Visible = true;
            flpDetailActions.Visible = true;
            btnFocusHere.Visible = false;
            btnHideTable.Visible = false;
            btnGoToTable.Visible = true;
            tabsDetails.Visible = false;
            pnlDetailPages.Visible = false;
            RestoreDockOrder(cardDetails, pnlDetailPages, tabsDetails, flpDetailActions, lblCascadeWarning, dgvProperties);
        }

        private string TableLabel(string logicalName)
        {
            var table = _model?.Find(logicalName);
            return table == null ? logicalName : table.ToString();
        }

        private void AddProperty(string key, string value, bool header = false)
        {
            var index = dgvProperties.Rows.Add(key, value ?? string.Empty);
            if (!header) return;
            var row = dgvProperties.Rows[index];
            row.DefaultCellStyle.BackColor = Theme.AccentSoft;
            row.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
            row.Cells[0].Style.ForeColor = Theme.Accent;
        }

        /// <summary>The property grid takes the height of its rows (within limits) so the tabs stay visible.</summary>
        private void FitProperties()
        {
            var rows = dgvProperties.Rows.Count;
            dgvProperties.Height = Math.Max(60, Math.Min(rows * dgvProperties.RowTemplate.Height + 4, canvas.SelectedEdge != null ? 420 : 240));
        }

        private void ShowDetailPage()
        {
            var index = tabsDetails.SelectedIndex;
            var grids = new[] { dgvColumns, dgvRelations, dgvKeys, dgvForms, dgvScripts, dgvSteps };
            for (var i = 0; i < grids.Length; i++) grids[i].Visible = i == index;

            string message = null;
            switch (index)
            {
                case TabColumnsIndex:
                    message = _columnsMessage;
                    break;
                case TabKeysIndex:
                    message = _columnsMessage ?? (dgvKeys.Rows.Count == 0 ? Strings.NoKeys : null);
                    break;
                case TabFormsIndex:
                    message = _logicMessage ?? (dgvForms.Rows.Count == 0 ? Strings.NoForms : null);
                    break;
                case TabScriptsIndex:
                    message = _logicMessage ?? (dgvScripts.Rows.Count == 0 ? Strings.NoScripts : null);
                    break;
                case TabTableStepsIndex:
                    message = _logicMessage ?? (dgvSteps.Rows.Count == 0 ? Strings.NoSteps : null);
                    break;
            }
            lblColumnsInfo.Text = message ?? string.Empty;
            lblColumnsInfo.Visible = !string.IsNullOrEmpty(message);
        }

        private void FillRelations(TableInfo table)
        {
            dgvRelations.Rows.Clear();
            var relations = table.Relations
                .OrderBy(r => r.KindFrom(table.LogicalName))
                .ThenBy(r => r.Other(table.LogicalName), StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var relation in relations)
            {
                var other = relation.Other(table.LogicalName);
                var via = relation.Kind == RelationKind.ManyToMany ? relation.Intersect : relation.ToAttribute;
                var index = dgvRelations.Rows.Add(relation.KindFrom(table.LogicalName), other, via,
                    relation.Kind == RelationKind.ManyToMany ? "—" : relation.Cascade?.Delete);
                var row = dgvRelations.Rows[index];
                row.Tag = relation;
                row.Cells["colRelVia"].ToolTipText = relation.SchemaName;
                if (_graph?.Find(other) == null) row.DefaultCellStyle.ForeColor = Theme.Subtle;
                if (relation.Cascade?.CascadesDelete == true) row.Cells["colRelDelete"].Style.ForeColor = Theme.Danger;
            }
            tabsDetails.Tabs[TabRelationsIndex].Badge = relations.Count.ToString();
            tabsDetails.Invalidate();
        }

        private void FillColumns(TableInfo table)
        {
            dgvColumns.Rows.Clear();
            dgvKeys.Rows.Clear();
            _columnsMessage = null;
            foreach (var column in table.Details.Columns)
            {
                var index = dgvColumns.Rows.Add(column.DisplayName, column.LogicalName,
                    column.Targets == null ? column.Type : $"{column.Type} → {column.Targets}",
                    Strings.RequiredLabel(column.RequiredLevel), column.IsCustom ? Strings.Yes : string.Empty);
                var row = dgvColumns.Rows[index];
                if (column.IsPrimaryId || column.IsPrimaryName) row.DefaultCellStyle.Font = Theme.Semibold;
                if (column.IsRequired) row.Cells["colColRequired"].Style.ForeColor = Theme.Danger;
                if (!string.IsNullOrEmpty(column.Description)) row.Cells["colColDisplay"].ToolTipText = column.Description;
            }
            foreach (var key in table.Details.Keys)
            {
                dgvKeys.Rows.Add(key.DisplayName, string.Join(", ", key.Columns), key.Status);
            }
            tabsDetails.Tabs[TabColumnsIndex].Badge = table.Details.Columns.Count.ToString();
            tabsDetails.Tabs[TabKeysIndex].Badge = table.Details.Keys.Count.ToString();
            tabsDetails.Invalidate();
            ShowDetailPage();
        }

        /// <summary>Columns and keys are read in the background so the map stays responsive.</summary>
        private void LoadDetailsAsync(TableInfo table)
        {
            var request = ++_detailsRequest;
            dgvColumns.Rows.Clear();
            dgvKeys.Rows.Clear();
            tabsDetails.Tabs[TabColumnsIndex].Badge = null;
            tabsDetails.Tabs[TabKeysIndex].Badge = null;
            tabsDetails.Invalidate();
            _columnsMessage = Strings.LoadingColumns;
            ShowDetailPage();

            var service = Service;
            if (service == null) return;
            Task.Run(() => new MetadataService(service).LoadDetails(table)).ContinueWith(task =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (task.Exception != null)
                        {
                            var message = GetErrorMessage(task.Exception.GetBaseException());
                            LogWarning("Columns of {0} unavailable: {1}", table.LogicalName, message);
                            if (request == _detailsRequest)
                            {
                                _columnsMessage = Strings.ColumnsError(message);
                                ShowDetailPage();
                            }
                            return;
                        }
                        table.Details = task.Result;
                        if (request == _detailsRequest && canvas.SelectedNode?.Table == table) FillColumns(table);
                    }));
                }
                catch (InvalidOperationException)
                {
                    // Control closed meanwhile.
                }
            });
        }

        /// <summary>Forms (with their scripts) and plugin steps of the table.</summary>
        private void FillLogic(TableInfo table)
        {
            var logic = table.Logic;
            _logicMessage = null;
            dgvForms.Rows.Clear();
            dgvScripts.Rows.Clear();
            dgvSteps.Rows.Clear();

            foreach (var form in logic.Forms)
            {
                var index = dgvForms.Rows.Add(form.Name + (form.IsDefault ? "  ★" : string.Empty), Strings.FormTypeLabel(form.Type),
                    form.Active ? Strings.Active : Strings.Inactive, form.Libraries.Count, form.Handlers.Count);
                var row = dgvForms.Rows[index];
                row.Tag = form;
                if (!form.Active) row.DefaultCellStyle.ForeColor = Theme.Subtle;
                row.Cells["colFormLibraries"].ToolTipText = string.Join(Environment.NewLine, form.Libraries);
                if (!string.IsNullOrEmpty(form.Description)) row.Cells["colFormName"].ToolTipText = form.Description;
            }

            // Every handler, then the libraries loaded by a form without any handler (functions called from the ribbon, other scripts...)
            foreach (var form in logic.Forms)
            {
                foreach (var handler in form.Handlers)
                {
                    var index = dgvScripts.Rows.Add(handler.Library, Strings.EventLabel(handler.Event, handler.Source), handler.Function, handler.Form);
                    var row = dgvScripts.Rows[index];
                    row.Cells["colScriptFunction"].ToolTipText = handler.PassContext ? Strings.PassContextTip : null;
                    if (!handler.Enabled || !form.Active) row.DefaultCellStyle.ForeColor = Theme.Subtle;
                }
                foreach (var library in form.Libraries.Where(l => form.Handlers.All(h => !string.Equals(h.Library, l, StringComparison.OrdinalIgnoreCase))))
                {
                    var index = dgvScripts.Rows.Add(library, Strings.LibraryOnly, string.Empty, form.Name);
                    dgvScripts.Rows[index].DefaultCellStyle.ForeColor = Theme.Muted;
                }
            }

            foreach (var step in logic.Steps)
            {
                logic.Types.TryGetValue(step.PluginTypeId ?? Guid.Empty, out var type);
                var index = dgvSteps.Rows.Add(step.Message, Strings.StageLabel(step.Stage), Strings.ModeLabel(step.Mode), step.Rank,
                    type?.ShortName ?? step.HandlerName, step.FilteringAttributes, step.Enabled ? Strings.Enabled : Strings.Disabled);
                var row = dgvSteps.Rows[index];
                row.Tag = step;
                row.Cells["colStepPlugin"].ToolTipText = type == null ? step.HandlerName : string.Join(Environment.NewLine, type.TypeName, type.AssemblyName, Strings.OpenInPluginMapTip);
                if (step.CustomizationLevel == 0 || !step.Enabled) row.DefaultCellStyle.ForeColor = Theme.Subtle;
            }

            tabsDetails.Tabs[TabFormsIndex].Badge = logic.Forms.Count.ToString();
            tabsDetails.Tabs[TabScriptsIndex].Badge = logic.Forms.Sum(f => f.Handlers.Count).ToString();
            tabsDetails.Tabs[TabTableStepsIndex].Badge = logic.Steps.Count(s => s.CustomizationLevel != 0).ToString();
            tabsDetails.UpdateHeight();
            ShowDetailPage();
        }

        private void LoadLogicAsync(TableInfo table)
        {
            var request = ++_logicRequest;
            dgvForms.Rows.Clear();
            dgvScripts.Rows.Clear();
            dgvSteps.Rows.Clear();
            foreach (var index in new[] { TabFormsIndex, TabScriptsIndex, TabTableStepsIndex }) tabsDetails.Tabs[index].Badge = null;
            tabsDetails.UpdateHeight();
            _logicMessage = Strings.LoadingLogic;
            ShowDetailPage();

            var service = Service;
            if (service == null) return;
            Task.Run(() => new PluginService(service).LoadLogic(table)).ContinueWith(task =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (task.Exception != null)
                        {
                            var message = GetErrorMessage(task.Exception.GetBaseException());
                            LogWarning("Forms / steps of {0} unavailable: {1}", table.LogicalName, message);
                            if (request == _logicRequest)
                            {
                                _logicMessage = Strings.LogicError(message);
                                ShowDetailPage();
                            }
                            return;
                        }
                        table.Logic = task.Result;
                        if (request == _logicRequest && canvas.SelectedNode?.Table == table) FillLogic(table);
                    }));
                }
                catch (InvalidOperationException)
                {
                    // Control closed meanwhile.
                }
            });
        }

        private void DgvRelations_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || !(dgvRelations.Rows[e.RowIndex].Tag is RelationInfo relation) || canvas.SelectedNode == null) return;
            var other = _graph?.Find(relation.Other(canvas.SelectedNode.Table.LogicalName));
            if (other == null) return;
            canvas.SelectNode(other);
            canvas.CenterOn(other);
        }

        /// <summary>From a selected relationship: selects the « many » table (or Table 2), then the other one on the next click.</summary>
        private void GoToRelationTable()
        {
            var edge = canvas.SelectedEdge;
            if (edge == null) return;
            canvas.SelectNode(edge.Target);
            canvas.CenterOn(edge.Target);
        }

        #endregion

        #region Export

        private enum ExportKind
        {
            Png,
            Svg,
            Mermaid,
            PlantUml
        }

        private string ExportTitle => tabsMain.SelectedIndex == PagePlugins ? Strings.PluginExportTitle(OrganizationName)
            : tabsMain.SelectedIndex == PageScripts ? Strings.ScriptExportTitle(OrganizationName)
            : Strings.ExportTitle(OrganizationName);

        private bool CanExport()
        {
            var graph = ActiveGraph;
            if (graph != null && graph.Nodes.Count > 0) return true;
            MessageDialog.Show(this, MessageKind.Info, Strings.Export, Strings.NothingToExport);
            return false;
        }

        private string ExportScope()
        {
            if (tabsMain.SelectedIndex == PageScripts)
            {
                var library = (cboScriptLibrary.SelectedItem as ComboItem)?.Value as string;
                var scriptSolution = SelectedScriptSolution;
                var focusScript = _scriptGraph?.Nodes.FirstOrDefault(n => n.IsFocus);
                if (focusScript != null) return Strings.ScopeFocus(focusScript.Label, (int)numScriptDepth.Value);
                if (library != null) return library;
                return scriptSolution != null ? Strings.ScopeSolution(scriptSolution.FriendlyName) : Strings.ScopeAllScripts;
            }
            if (tabsMain.SelectedIndex == PagePlugins)
            {
                var assembly = (cboPluginAssembly.SelectedItem as ComboItem)?.Value as PluginAssemblyInfo;
                var pluginSolution = (cboPluginSolution.SelectedItem as ComboItem)?.Value as SolutionItem;
                var focusNode = _pluginGraph?.Nodes.FirstOrDefault(n => n.IsFocus);
                if (focusNode != null) return Strings.ScopeFocus(focusNode.Label, (int)numPluginDepth.Value);
                if (assembly != null) return assembly.Name;
                return pluginSolution != null ? Strings.ScopeSolution(pluginSolution.FriendlyName) : Strings.ScopeAllPlugins;
            }
            var focus = _model?.Find(_focus);
            if (focus != null) return Strings.ScopeFocus(focus.Label, (int)numDepth.Value);
            var solution = SelectedSolution;
            return solution != null ? Strings.ScopeSolution(solution.FriendlyName) : Strings.ScopeAllTables;
        }

        private string DefaultFileName(string extension)
        {
            var name = new StringBuilder(tabsMain.SelectedIndex == PagePlugins ? "plugins" : tabsMain.SelectedIndex == PageScripts ? "javascript" : "datamodel");
            var scope = _model?.Find(_focus)?.LogicalName ?? SelectedSolution?.UniqueName;
            if (!string.IsNullOrEmpty(scope)) name.Append('-').Append(scope);
            if (!string.IsNullOrEmpty(OrganizationName)) name.Append('-').Append(OrganizationName);
            var safe = new string(name.ToString().Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c).ToArray());
            return safe + extension;
        }

        private string AskPath(string filter, string extension)
        {
            using (var dialog = new SaveFileDialog { Filter = filter, FileName = DefaultFileName(extension), AddExtension = true, OverwritePrompt = true })
            {
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
            }
        }

        private void ExportFile(ExportKind kind)
        {
            if (!CanExport()) return;
            string path;
            switch (kind)
            {
                case ExportKind.Png: path = AskPath("PNG (*.png)|*.png", ".png"); break;
                case ExportKind.Svg: path = AskPath("SVG (*.svg)|*.svg", ".svg"); break;
                case ExportKind.Mermaid: path = AskPath("Mermaid (*.mmd)|*.mmd|Markdown (*.md)|*.md", ".mmd"); break;
                default: path = AskPath("PlantUML (*.puml)|*.puml", ".puml"); break;
            }
            if (path == null) return;

            var graph = ActiveGraph;
            var title = ExportTitle;
            var subtitle = Strings.ExportSubtitle(graph.Nodes.Count, graph.Edges.Count, ExportScope());
            try
            {
                Cursor = Cursors.WaitCursor;
                switch (kind)
                {
                    case ExportKind.Png:
                        ImageExporter.SavePng(graph, path, title, subtitle);
                        break;
                    case ExportKind.Svg:
                        ImageExporter.SaveSvg(graph, path, title, subtitle);
                        break;
                    case ExportKind.Mermaid:
                        var mermaid = DiagramExporter.ToMermaid(graph, title);
                        if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) mermaid = $"# {title}\n\n{subtitle}\n\n```mermaid\n{mermaid}```\n";
                        File.WriteAllText(path, mermaid, new UTF8Encoding(false));
                        break;
                    default:
                        File.WriteAllText(path, DiagramExporter.ToPlantUml(graph, title), new UTF8Encoding(false));
                        break;
                }
            }
            catch (Exception error)
            {
                ShowError(Strings.ExportFailed, error);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            LogInfo("Export {0}: {1}", kind, path);
            OfferToOpen(path);
        }

        private void CopyDiagram(bool plantUml)
        {
            if (!CanExport()) return;
            var title = ExportTitle;
            CopyText(plantUml ? DiagramExporter.ToPlantUml(ActiveGraph, title) : DiagramExporter.ToMermaid(ActiveGraph, title));
            MessageDialog.Show(this, MessageKind.Success, plantUml ? "PlantUML" : "Mermaid", Strings.CopiedToClipboard);
        }

        private void ExportExcel()
        {
            if (!CanExport()) return;
            var graph = ActiveGraph;
            if (graph.Kind != GraphKind.DataModel)
            {
                var pluginPath = AskPath("Excel (*.xlsx)|*.xlsx", ".xlsx");
                if (pluginPath == null) return;
                try
                {
                    if (graph.Kind == GraphKind.Plugins) ExcelExporter.SavePlugins(graph, pluginPath);
                    else ExcelExporter.SaveScripts(graph, pluginPath);
                }
                catch (Exception error)
                {
                    ShowError(Strings.ExportFailed, error);
                    return;
                }
                OfferToOpen(pluginPath);
                return;
            }
            var missing = graph.Nodes.Where(n => !n.IsGhost && n.Table.Details == null).Select(n => n.Table).ToList();
            var loadColumns = false;
            if (missing.Count > 0)
            {
                loadColumns = MessageDialog.Ask(this, MessageKind.Info, Strings.ExcelColumnsTitle, Strings.ExcelLoadColumns(graph.Nodes.Count(n => !n.IsGhost)), null, null,
                    Strings.IncludeColumns, Strings.TablesOnly);
            }

            var path = AskPath("Excel (*.xlsx)|*.xlsx", ".xlsx");
            if (path == null) return;

            RunWork(new WorkAsyncInfo
            {
                Message = Strings.Exporting,
                Work = (worker, args) =>
                {
                    if (loadColumns)
                    {
                        var service = new MetadataService(Service);
                        for (var i = 0; i < missing.Count; i++)
                        {
                            worker.ReportProgress(0, Strings.LoadingColumnsProgress(i + 1, missing.Count));
                            try
                            {
                                missing[i].Details = service.LoadDetails(missing[i]);
                            }
                            catch (Exception error)
                            {
                                LogWarning("Columns of {0} unavailable: {1}", missing[i].LogicalName, error.Message);
                            }
                        }
                    }
                    worker.ReportProgress(0, Strings.Exporting);
                    ExcelExporter.Save(graph, path);
                },
                ProgressChanged = e => _loading.SetMessage(e.UserState as string ?? Strings.Exporting),
                PostWorkCallBack = e =>
                {
                    if (e.Error != null)
                    {
                        ShowError(Strings.ExportFailed, e.Error);
                        return;
                    }
                    LogInfo("Excel export: {0}", path);
                    OfferToOpen(path);
                }
            });
        }

        private void OfferToOpen(string path)
        {
            if (MessageDialog.Ask(this, MessageKind.Success, Strings.ExportDoneTitle, path, null, Strings.OpenExportQuestion, Strings.OpenFile, Strings.NotNow))
            {
                OpenUrl(path);
            }
        }

        #endregion

        #region Helpers

        private void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception error)
            {
                LogWarning("Clipboard unavailable: {0}", error.Message);
            }
        }

        private void OpenUrl(string target)
        {
            try
            {
                Process.Start(target);
            }
            catch (Exception error)
            {
                LogWarning("Cannot open {0}: {1}", target, error.Message);
            }
        }

        /// <summary>
        /// Puts docked siblings back in their z-order (front = Fill control, back = outermost edge control).
        /// Showing a control whose window was never created can move it to the front, which breaks the docking.
        /// </summary>
        private static void RestoreDockOrder(Control parent, params Control[] frontToBack)
        {
            parent.SuspendLayout();
            for (var i = 0; i < frontToBack.Length; i++) parent.Controls.SetChildIndex(frontToBack[i], i);
            parent.ResumeLayout(true);
        }

        /// <summary>
        /// Runs a long operation in the background behind the loading layer (blurred screen + animated « AH » icon),
        /// instead of the standard XrmToolBox loading panel. Returns false when another operation is already running.
        /// </summary>
        private bool RunWork(WorkAsyncInfo info)
        {
            if (_busy) return false;
            _busy = true;
            _loading.Begin(this, Strings.PleaseWait, info.Message);

            var worker = new BackgroundWorker { WorkerReportsProgress = true };
            worker.DoWork += (s, e) => info.Work(worker, e);
            worker.ProgressChanged += (s, e) => info.ProgressChanged?.Invoke(e);
            worker.RunWorkerCompleted += (s, e) =>
            {
                worker.Dispose();
                _busy = false;
                if (IsDisposed) return;
                _loading.End();
                info.PostWorkCallBack?.Invoke(e);
            };
            worker.RunWorkerAsync(info.AsyncArgument);
            return true;
        }

        private void ShowError(string context, Exception error)
        {
            LogError("{0}: {1}", context, error.ToString());
            if (error is FaultException<OrganizationServiceFault> || error.InnerException is FaultException<OrganizationServiceFault>)
            {
                MessageDialog.ShowError(this, error);
                return;
            }
            MessageDialog.Show(this, MessageKind.Error, context, GetErrorMessage(error), error.ToString());
        }

        private static string GetErrorMessage(Exception error)
        {
            switch (error)
            {
                case FaultException<OrganizationServiceFault> fault:
                    return fault.Detail?.Message ?? fault.Message;
                default:
                    return error.InnerException != null ? $"{error.Message}\r\n{error.InnerException.Message}" : error.Message;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Strings.LanguageChanged -= Strings_LanguageChanged;
                _searchTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
