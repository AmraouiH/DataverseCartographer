using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DataverseCartographer.Localization;
using DataverseCartographer.Models;
using DataverseCartographer.Services;
using DataverseCartographer.UI;
using DataverseCartographer.UI.Controls;
using DataverseCartographer.UI.Rendering;
using XrmToolBox.Extensibility;

namespace DataverseCartographer
{
    /// <summary>
    /// « Plugins » page: technical map table → steps → plugin → code operations → table, filtered by solution or DLL.
    /// </summary>
    public partial class CartographerControl
    {
        private const int PageModel = 0;
        private const int PagePlugins = 1;
        private const int TabStepsIndex = 0;
        private const int TabOperationsIndex = 1;

        #region Controls

        private SegmentedTabs tabsMain;
        private Panel pageModel, pagePlugins;

        private CardPanel cardPluginFilters, cardPluginExplore, cardPluginMap, cardPluginDetails;
        private Label lblPluginSolution, lblPluginAssembly, lblPluginFocus, lblPluginDepth, lblPluginLayout, lblPluginStatus, lblPluginSearchInfo, lblPluginHint, lblPluginAnalysis;
        private ComboBox cboPluginSolution, cboPluginAssembly, cboPluginFocus, cboPluginLayout;
        private CheckBox chkHideMicrosoft, chkIncludeDisabled, chkShowReads, chkAnalyzeCode, chkPluginLabels;
        private NumericUpDown numPluginDepth;
        private ModernButton btnLoadPlugins, btnPluginExplore, btnPluginWholeMap, btnPluginRelayout, btnPluginFit, btnPluginZoomIn, btnPluginZoomOut;
        private TextBox txtPluginSearch;
        private GraphCanvas pluginCanvas;
        private DataGridView dgvPluginProperties, dgvPluginSteps, dgvPluginOperations;
        private SegmentedTabs tabsPluginDetails;
        private Label lblPluginGridInfo;
        private Panel pnlPluginPages;

        #endregion

        private PluginModel _plugins;
        private Graph _pluginGraph;
        private string _pluginFocus;
        private bool _pluginsLoading;
        private List<GraphNode> _pluginSearchHits = new List<GraphNode>();
        private int _pluginSearchIndex = -1;

        private Graph ActiveGraph => tabsMain.SelectedIndex == PagePlugins ? _pluginGraph : tabsMain.SelectedIndex == PageScripts ? _scriptGraph : _graph;

        #region Layout

        private void BuildPluginsPage()
        {
            pagePlugins = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas, Visible = false };

            // Filters
            cardPluginFilters = new CardPanel { Dock = DockStyle.Top, Height = 392, Step = 1 };
            var filters = Stack();
            lblPluginSolution = FieldLabel();
            cboPluginSolution = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 25, DropDownWidth = 420 };
            lblPluginAssembly = FieldLabel();
            cboPluginAssembly = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 25, DropDownWidth = 420 };
            chkHideMicrosoft = Check();
            chkHideMicrosoft.Margin = new Padding(0, 12, 0, 0);
            chkIncludeDisabled = Check();
            chkShowReads = Check();
            chkAnalyzeCode = Check();
            chkHideMicrosoft.Checked = true;
            chkAnalyzeCode.Checked = true;
            btnLoadPlugins = new ModernButton { Variant = ButtonVariant.Secondary, Glyph = IconKind.Reload, AutoSize = true, MinimumSize = new Size(120, 30), Height = 30, Margin = new Padding(0, 12, 0, 0) };
            AddRows(filters, lblPluginSolution, cboPluginSolution, lblPluginAssembly, cboPluginAssembly, chkHideMicrosoft, chkIncludeDisabled, chkShowReads, chkAnalyzeCode, btnLoadPlugins);
            cardPluginFilters.Controls.Add(filters);

            // Explore
            cardPluginExplore = new CardPanel { Dock = DockStyle.Fill, Step = 2 };
            var explore = Stack();
            lblPluginFocus = FieldLabel();
            lblPluginFocus.Margin = new Padding(0, 0, 0, 4);
            cboPluginFocus = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                MaxDropDownItems = 25,
                DropDownWidth = 360
            };
            var depthRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Card, Margin = new Padding(0, 8, 0, 0) };
            lblPluginDepth = FieldLabel();
            lblPluginDepth.Margin = new Padding(0, 6, 8, 0);
            numPluginDepth = new NumericUpDown { Minimum = 1, Maximum = 6, Value = 1, Width = 52, Margin = new Padding(0, 3, 12, 0) };
            btnPluginExplore = new ModernButton { Variant = ButtonVariant.Primary, Glyph = IconKind.Target, AutoSize = true, MinimumSize = new Size(100, 30), Height = 30, Margin = Padding.Empty };
            depthRow.Controls.AddRange(new Control[] { lblPluginDepth, numPluginDepth, btnPluginExplore });
            btnPluginWholeMap = new ModernButton { Variant = ButtonVariant.Secondary, Glyph = IconKind.Graph, AutoSize = true, MinimumSize = new Size(110, 30), Height = 30, Margin = new Padding(0, 10, 0, 0) };
            lblPluginAnalysis = new Label { AutoSize = true, MaximumSize = new Size(250, 0), ForeColor = Theme.Muted, Margin = new Padding(0, 14, 0, 0) };
            lblPluginHint = new Label { AutoSize = true, MaximumSize = new Size(250, 0), ForeColor = Theme.Subtle, Font = Theme.Small, Margin = new Padding(0, 12, 0, 0) };
            AddRows(explore, lblPluginFocus, cboPluginFocus, depthRow, btnPluginWholeMap, lblPluginAnalysis, lblPluginHint);
            cardPluginExplore.Controls.Add(explore);

            var left = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = Theme.Canvas };
            left.Controls.Add(cardPluginExplore);
            left.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Theme.Canvas });
            left.Controls.Add(cardPluginFilters);

            // Map
            cardPluginMap = new CardPanel { Dock = DockStyle.Fill };
            var toolbar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Card };
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Card };
            lblPluginLayout = FieldLabel();
            lblPluginLayout.Margin = new Padding(0, 9, 6, 0);
            cboPluginLayout = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Margin = new Padding(0, 5, 8, 0) };
            btnPluginRelayout = ToolButton(IconKind.Layout);
            btnPluginFit = ToolButton(IconKind.Fit);
            btnPluginFit.MinimumSize = new Size(32, 30);
            btnPluginZoomIn = ToolButton(IconKind.ZoomIn);
            btnPluginZoomIn.MinimumSize = new Size(32, 30);
            btnPluginZoomOut = ToolButton(IconKind.ZoomOut);
            btnPluginZoomOut.MinimumSize = new Size(32, 30);
            chkPluginLabels = new CheckBox { AutoSize = true, Margin = new Padding(6, 9, 14, 0), Checked = true };
            tools.Controls.AddRange(new Control[] { lblPluginLayout, cboPluginLayout, btnPluginRelayout, btnPluginFit, btnPluginZoomIn, btnPluginZoomOut, chkPluginLabels });
            var searchHost = new Panel { Dock = DockStyle.Right, Width = 170, BackColor = Theme.Card, Padding = new Padding(0, 7, 0, 0) };
            txtPluginSearch = new TextBox { Dock = DockStyle.Top };
            searchHost.Controls.Add(txtPluginSearch);
            toolbar.Controls.Add(tools);
            toolbar.Controls.Add(searchHost);

            var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Theme.Card };
            lblPluginStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, Font = Theme.Small, BackColor = Theme.Card, AutoEllipsis = true };
            lblPluginSearchInfo = new Label { Dock = DockStyle.Right, AutoSize = true, ForeColor = Theme.SearchHit, Font = Theme.Semibold, BackColor = Theme.Card, Padding = new Padding(0, 6, 0, 0) };
            statusBar.Controls.Add(lblPluginStatus);
            statusBar.Controls.Add(lblPluginSearchInfo);

            pluginCanvas = new GraphCanvas { Dock = DockStyle.Fill, ShowLabels = true };
            var canvasHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0), BackColor = Theme.Card };
            canvasHost.Controls.Add(pluginCanvas);
            cardPluginMap.Controls.Add(canvasHost);
            cardPluginMap.Controls.Add(statusBar);
            cardPluginMap.Controls.Add(toolbar);

            // Details
            cardPluginDetails = new CardPanel { Dock = DockStyle.Right, Width = 440 };
            dgvPluginProperties = new DataGridView
            {
                Dock = DockStyle.Top,
                Height = 200,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ColumnHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            Theme.StyleGrid(dgvPluginProperties, false);
            dgvPluginProperties.RowTemplate.Height = 26;
            dgvPluginProperties.Columns.Add(new DataGridViewTextBoxColumn { Name = "colKey", FillWeight = 38, DefaultCellStyle = { ForeColor = Theme.Muted, Font = Theme.Semibold } });
            dgvPluginProperties.Columns.Add(new DataGridViewTextBoxColumn { Name = "colValue", FillWeight = 62 });

            tabsPluginDetails = new SegmentedTabs { Dock = DockStyle.Top, Height = 44 };
            tabsPluginDetails.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Play, BadgeColor = Theme.Accent });
            tabsPluginDetails.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Database, BadgeColor = Theme.Danger });

            var pages = pnlPluginPages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
            dgvPluginSteps = DetailGrid();
            AddColumns(dgvPluginSteps, ("colPsMessage", 14), ("colPsTable", 18), ("colPsStage", 14), ("colPsMode", 10), ("colPsRank", 7), ("colPsHandler", 26), ("colPsFilter", 20), ("colPsImages", 18), ("colPsState", 10));
            dgvPluginOperations = DetailGrid();
            AddColumns(dgvPluginOperations, ("colPoPlugin", 26), ("colPoOperation", 16), ("colPoTable", 20), ("colPoMethod", 38));
            lblPluginGridInfo = new Label { Dock = DockStyle.Top, Height = 26, ForeColor = Theme.Muted, Font = Theme.Small, TextAlign = ContentAlignment.MiddleLeft, Visible = false };
            pages.Controls.AddRange(new Control[] { dgvPluginSteps, dgvPluginOperations, lblPluginGridInfo });

            cardPluginDetails.Controls.Add(pages);
            cardPluginDetails.Controls.Add(tabsPluginDetails);
            cardPluginDetails.Controls.Add(dgvPluginProperties);

            pagePlugins.Controls.Add(cardPluginMap);
            pagePlugins.Controls.Add(new Splitter { Dock = DockStyle.Right, Width = 10, BackColor = Theme.Canvas, MinExtra = 300, MinSize = 300 });
            pagePlugins.Controls.Add(cardPluginDetails);
            pagePlugins.Controls.Add(new Splitter { Dock = DockStyle.Left, Width = 10, BackColor = Theme.Canvas, MinExtra = 300, MinSize = 240 });
            pagePlugins.Controls.Add(left);
        }

        private static void AddColumns(DataGridView grid, params (string Name, float Weight)[] columns)
        {
            foreach (var (name, weight) in columns) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, FillWeight = weight });
        }

        private void ApplyPluginsLanguage()
        {
            tabsMain.Tabs[PageModel].Text = Strings.TabDataModel;
            tabsMain.Tabs[PagePlugins].Text = Strings.TabPlugins;
            tabsMain.Invalidate();

            cardPluginFilters.Title = Strings.PluginFiltersTitle;
            cardPluginFilters.Subtitle = Strings.PluginFiltersSubtitle;
            lblPluginSolution.Text = Strings.SolutionLabel;
            lblPluginAssembly.Text = Strings.AssemblyLabel;
            chkHideMicrosoft.Text = Strings.HideMicrosoft;
            toolTip.SetToolTip(chkHideMicrosoft, Strings.HideMicrosoftTip);
            chkIncludeDisabled.Text = Strings.IncludeDisabled;
            chkShowReads.Text = Strings.ShowReads;
            toolTip.SetToolTip(chkShowReads, Strings.ShowReadsTip);
            chkAnalyzeCode.Text = Strings.AnalyzeCode;
            toolTip.SetToolTip(chkAnalyzeCode, Strings.AnalyzeCodeTip);
            btnLoadPlugins.Text = Strings.LoadPlugins;
            FillPluginSolutions();
            FillPluginAssemblies();

            cardPluginExplore.Title = Strings.ExploreTitle;
            cardPluginExplore.Subtitle = Strings.PluginExploreSubtitle;
            lblPluginFocus.Text = Strings.PluginFocusLabel;
            lblPluginDepth.Text = Strings.DepthLabel;
            btnPluginExplore.Text = Strings.ShowFocus;
            btnPluginWholeMap.Text = Strings.ClearFocus;
            lblPluginHint.Text = Strings.PluginHint + " " + Strings.LegendHint + ".";
            Theme.SetCue(cboPluginFocus, Strings.PluginFocusCue);

            lblPluginLayout.Text = Strings.LayoutLabel;
            Refill(cboPluginLayout, Strings.Layouts);
            btnPluginRelayout.Text = Strings.Relayout;
            toolTip.SetToolTip(btnPluginRelayout, Strings.RelayoutTip);
            toolTip.SetToolTip(btnPluginFit, Strings.FitTip);
            toolTip.SetToolTip(btnPluginZoomIn, Strings.ZoomInTip);
            toolTip.SetToolTip(btnPluginZoomOut, Strings.ZoomOutTip);
            chkPluginLabels.Text = Strings.Labels;
            Theme.SetCue(txtPluginSearch, Strings.PluginSearchCue);

            tabsPluginDetails.Tabs[TabStepsIndex].Text = Strings.TabSteps;
            tabsPluginDetails.Tabs[TabOperationsIndex].Text = Strings.TabCodeOperations;
            tabsPluginDetails.Invalidate();
            SetHeaders(dgvPluginSteps, Strings.ColMessage, Strings.ColTable, Strings.ColStage, Strings.ColMode, Strings.ColRank, Strings.ColHandler, Strings.ColFilteringAttributes, Strings.ColImages, Strings.ColState);
            SetHeaders(dgvPluginOperations, Strings.ColPlugin, Strings.ColOperation, Strings.ColTable, Strings.ColMethod);

            pluginCanvas.Legend = new[]
            {
                new GraphCanvas.LegendItem(Strings.LegendTable, Theme.StandardTable, GraphCanvas.LegendStyle.Dot, "table", nodes: n => n.Kind == NodeKind.Table),
                new GraphCanvas.LegendItem(Strings.LegendPlugin, GraphRenderer.PluginNode, GraphCanvas.LegendStyle.Square, "plugin", nodes: n => n.Kind == NodeKind.Plugin),
                new GraphCanvas.LegendItem(Strings.LegendSyncStep, GraphRenderer.SyncTrigger, GraphCanvas.LegendStyle.Line, "sync", edges: e => e.Kind == EdgeKind.Trigger && !e.Async),
                new GraphCanvas.LegendItem(Strings.LegendAsyncStep, GraphRenderer.AsyncTrigger, GraphCanvas.LegendStyle.Line, "async", edges: e => e.Kind == EdgeKind.Trigger && e.Async),
                new GraphCanvas.LegendItem(Strings.LegendWrite, GraphRenderer.WriteAction, GraphCanvas.LegendStyle.Line, "write", edges: e => e.Kind == EdgeKind.Action && !e.Muted),
                new GraphCanvas.LegendItem(Strings.LegendRead, GraphRenderer.ReadAction, GraphCanvas.LegendStyle.Dashed, "read", linked: chkShowReads)
            };
            SyncLegends();
            pluginCanvas.EmptyText = _plugins == null ? Strings.PluginsEmptyNotLoaded : Strings.PluginsEmptyNoMatch;
            pluginCanvas.Invalidate();
            UpdatePluginStatus();
            ShowPluginDetails();
        }

        private static void SetHeaders(DataGridView grid, params string[] headers)
        {
            for (var i = 0; i < headers.Length && i < grid.Columns.Count; i++) grid.Columns[i].HeaderText = headers[i];
        }

        #endregion

        #region Events

        private void WirePluginEvents()
        {
            tabsMain.SelectedIndexChanged += (s, e) => ShowMainPage();

            cboPluginSolution.SelectedIndexChanged += (s, e) => OnPluginSolutionChanged();
            cboPluginAssembly.SelectedIndexChanged += (s, e) => OnPluginFilterChanged();
            chkHideMicrosoft.CheckedChanged += (s, e) =>
            {
                if (_suppressEvents) return;
                FillPluginAssemblies();
                OnPluginFilterChanged();
            };
            chkIncludeDisabled.CheckedChanged += (s, e) => OnPluginFilterChanged();
            chkShowReads.CheckedChanged += (s, e) => OnPluginFilterChanged();
            chkAnalyzeCode.CheckedChanged += (s, e) => OnPluginFilterChanged();
            btnLoadPlugins.Click += (s, e) => ExecuteMethod(LoadPlugins);

            btnPluginExplore.Click += (s, e) => ExplorePluginFromCombo();
            cboPluginFocus.SelectionChangeCommitted += (s, e) => BeginInvoke(new Action(ExplorePluginFromCombo));
            cboPluginFocus.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                ExplorePluginFromCombo();
            };
            numPluginDepth.ValueChanged += (s, e) =>
            {
                if (_pluginFocus != null) OnPluginFilterChanged();
            };
            btnPluginWholeMap.Click += (s, e) => SetPluginFocus(null);

            cboPluginLayout.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressEvents) return;
                SaveSettings();
                if (_pluginGraph == null) return;
                GraphLayout.Apply(_pluginGraph, (LayoutKind)Math.Max(0, cboPluginLayout.SelectedIndex));
                pluginCanvas.FitToView();
            };
            btnPluginRelayout.Click += (s, e) =>
            {
                if (_pluginGraph == null) return;
                GraphLayout.Apply(_pluginGraph, (LayoutKind)Math.Max(0, cboPluginLayout.SelectedIndex));
                pluginCanvas.FitToView();
            };
            btnPluginFit.Click += (s, e) => pluginCanvas.FitToView();
            btnPluginZoomIn.Click += (s, e) => pluginCanvas.ZoomBy(1.25f);
            btnPluginZoomOut.Click += (s, e) => pluginCanvas.ZoomBy(1 / 1.25f);
            chkPluginLabels.CheckedChanged += (s, e) => pluginCanvas.ShowLabels = chkPluginLabels.Checked;

            txtPluginSearch.TextChanged += (s, e) => UpdatePluginSearch();
            txtPluginSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    txtPluginSearch.Clear();
                    return;
                }
                if (e.KeyCode != Keys.Enter || _pluginSearchHits.Count == 0) return;
                e.SuppressKeyPress = true;
                _pluginSearchIndex = (_pluginSearchIndex + 1) % _pluginSearchHits.Count;
                var node = _pluginSearchHits[_pluginSearchIndex];
                pluginCanvas.SelectNode(node);
                pluginCanvas.CenterOn(node);
                lblPluginSearchInfo.Text = $"{_pluginSearchIndex + 1}/{_pluginSearchHits.Count}";
            };

            pluginCanvas.SelectionChanged += (s, e) =>
            {
                ShowPluginDetails();
                RestoreDockOrder(cardPluginDetails, pnlPluginPages, tabsPluginDetails, dgvPluginProperties);
            };
            pluginCanvas.NodeDoubleClicked += (s, node) => SetPluginFocus(node.Key);
            pluginCanvas.Resize += (s, e) =>
            {
                if (_pluginGraph != null && pluginCanvas.ZoomLevel == 1f) pluginCanvas.FitToView();
            };
            tabsPluginDetails.SelectedIndexChanged += (s, e) => ShowPluginDetailPage();
            dgvPluginOperations.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0 || !(dgvPluginOperations.Rows[e.RowIndex].Tag is CodeOperation operation) || operation.Entity == null) return;
                var node = _pluginGraph?.Find(operation.Entity);
                if (node == null) return;
                pluginCanvas.SelectNode(node);
                pluginCanvas.CenterOn(node);
            };
        }

        private void ShowMainPage()
        {
            var page = tabsMain.SelectedIndex;
            pageModel.Visible = page == PageModel;
            pagePlugins.Visible = page == PagePlugins;
            pageScripts.Visible = page == PageScripts;
            if (page == PagePlugins && _plugins == null && !_pluginsLoading && Service != null) LoadPlugins();
            if (page == PageScripts && _scripts == null && !_scriptsLoading && Service != null) LoadScripts();
        }

        /// <summary>A solution narrows the DLL list to its assemblies: its components are read once, then cached.</summary>
        private void OnPluginSolutionChanged()
        {
            if (_suppressEvents || !_controlLoaded || _plugins == null) return;
            var solution = SelectedPluginSolution;
            if (solution != null && !_plugins.SolutionComponents.ContainsKey(solution.Id))
            {
                var plugins = _plugins;
                RunWork(new WorkAsyncInfo
                {
                    Message = Strings.ReadingSolutionTables,
                    Work = (worker, args) => args.Result = new PluginService(Service).LoadSolutionComponents(solution.Id),
                    PostWorkCallBack = e =>
                    {
                        if (e.Error != null)
                        {
                            ShowError(Strings.SolutionFailed, e.Error);
                            return;
                        }
                        plugins.SolutionComponents[solution.Id] = (HashSet<Guid>)e.Result;
                        if (plugins != _plugins) return;
                        FillPluginAssemblies();
                        RebuildPluginGraph(true);
                    }
                });
                return;
            }
            FillPluginAssemblies();
            RebuildPluginGraph(true);
        }

        private SolutionItem SelectedPluginSolution => (cboPluginSolution.SelectedItem as ComboItem)?.Value as SolutionItem;

        /// <summary>The assembly is in the solution, directly or through one of its plugin types or steps.</summary>
        private bool AssemblyInSolution(PluginAssemblyInfo assembly, HashSet<Guid> components)
        {
            if (components == null || components.Contains(assembly.Id)) return true;
            var types = _plugins.Types.Where(t => t.AssemblyId == assembly.Id).Select(t => t.Id).ToList();
            return types.Any(components.Contains)
                   || _plugins.Steps.Any(step => step.PluginTypeId.HasValue && components.Contains(step.Id) && types.Contains(step.PluginTypeId.Value));
        }

        private void OnPluginFilterChanged()
        {
            if (_suppressEvents || !_controlLoaded) return;
            SaveSettings();
            if (_plugins == null) return;
            RebuildPluginGraph(true);
        }

        #endregion

        #region Loading

        private void LoadPlugins()
        {
            if (_busy) return;
            _pluginsLoading = true;
            RunWork(new WorkAsyncInfo
            {
                Message = Strings.LoadingPlugins,
                Work = (worker, args) => args.Result = new PluginService(Service).LoadModel(m => worker.ReportProgress(0, m)),
                ProgressChanged = e => _loading.SetMessage(e.UserState as string ?? Strings.LoadingPlugins),
                PostWorkCallBack = e =>
                {
                    _pluginsLoading = false;
                    if (e.Error != null)
                    {
                        ShowError(Strings.PluginsLoadFailed, e.Error);
                        return;
                    }
                    _plugins = (PluginModel)e.Result;
                    LogInfo("Plugins loaded: {0} assemblies, {1} types, {2} steps.", _plugins.Assemblies.Count, _plugins.Types.Count, _plugins.Steps.Count);
                    _suppressEvents = true;
                    FillPluginSolutions();
                    FillPluginAssemblies();
                    _suppressEvents = false;
                    pluginCanvas.EmptyText = Strings.PluginsEmptyNoMatch;
                    RebuildPluginGraph(true);
                }
            });
        }

        private void FillPluginSolutions()
        {
            var previous = (cboPluginSolution.SelectedItem as ComboItem)?.Value as SolutionItem;
            var wasSuppressed = _suppressEvents;
            _suppressEvents = true;
            cboPluginSolution.BeginUpdate();
            cboPluginSolution.Items.Clear();
            cboPluginSolution.Items.Add(new ComboItem { Text = Strings.AllPluginSolutions });
            foreach (var solution in _model?.Solutions ?? new List<SolutionItem>())
            {
                cboPluginSolution.Items.Add(new ComboItem { Text = solution.ToString(), Value = solution });
            }
            var index = previous == null ? 0 : cboPluginSolution.Items.Cast<ComboItem>().ToList().FindIndex(i => (i.Value as SolutionItem)?.Id == previous.Id);
            cboPluginSolution.SelectedIndex = Math.Max(0, index);
            cboPluginSolution.EndUpdate();
            _suppressEvents = wasSuppressed;
        }

        private void FillPluginAssemblies()
        {
            var previous = (cboPluginAssembly.SelectedItem as ComboItem)?.Value as PluginAssemblyInfo;
            var wasSuppressed = _suppressEvents;
            _suppressEvents = true;
            cboPluginAssembly.BeginUpdate();
            cboPluginAssembly.Items.Clear();
            cboPluginAssembly.Items.Add(new ComboItem { Text = Strings.AllAssemblies });
            if (_plugins != null)
            {
                var solution = SelectedPluginSolution;
                HashSet<Guid> components = null;
                if (solution != null) _plugins.SolutionComponents.TryGetValue(solution.Id, out components);
                foreach (var assembly in _plugins.Assemblies.Where(a => (!chkHideMicrosoft.Checked || !a.IsMicrosoft) && AssemblyInSolution(a, components)))
                {
                    var types = _plugins.Types.Count(t => t.AssemblyId == assembly.Id);
                    cboPluginAssembly.Items.Add(new ComboItem { Text = Strings.AssemblyItem(assembly.Name, assembly.Version, types), Value = assembly });
                }
            }
            var index = previous == null ? 0 : cboPluginAssembly.Items.Cast<ComboItem>().ToList().FindIndex(i => (i.Value as PluginAssemblyInfo)?.Id == previous.Id);
            cboPluginAssembly.SelectedIndex = Math.Max(0, index);
            cboPluginAssembly.EndUpdate();
            _suppressEvents = wasSuppressed;
        }

        #endregion

        #region Graph

        private void RebuildPluginGraph(bool relayout)
        {
            if (_plugins == null)
            {
                pluginCanvas.Graph = null;
                UpdatePluginStatus();
                return;
            }

            var solution = (cboPluginSolution.SelectedItem as ComboItem)?.Value as SolutionItem;
            HashSet<Guid> components = null;
            if (solution != null && !_plugins.SolutionComponents.TryGetValue(solution.Id, out components))
            {
                var plugins = _plugins;
                RunWork(new WorkAsyncInfo
                {
                    Message = Strings.ReadingSolutionTables,
                    Work = (worker, args) => args.Result = new PluginService(Service).LoadSolutionComponents(solution.Id),
                    PostWorkCallBack = e =>
                    {
                        if (e.Error != null)
                        {
                            ShowError(Strings.SolutionFailed, e.Error);
                            return;
                        }
                        plugins.SolutionComponents[solution.Id] = (HashSet<Guid>)e.Result;
                        if (plugins == _plugins) RebuildPluginGraph(relayout);
                    }
                });
                return;
            }

            var filter = new PluginFilter
            {
                SolutionComponents = components,
                AssemblyId = ((cboPluginAssembly.SelectedItem as ComboItem)?.Value as PluginAssemblyInfo)?.Id,
                HideMicrosoft = chkHideMicrosoft.Checked,
                IncludeDisabled = chkIncludeDisabled.Checked,
                ShowReads = chkShowReads.Checked,
                Focus = _pluginFocus,
                Depth = (int)numPluginDepth.Value
            };

            // The code of the assemblies on the map is analyzed (once) before drawing the actions.
            if (chkAnalyzeCode.Checked)
            {
                var unfocused = new PluginFilter
                {
                    SolutionComponents = filter.SolutionComponents,
                    AssemblyId = filter.AssemblyId,
                    HideMicrosoft = filter.HideMicrosoft,
                    IncludeDisabled = filter.IncludeDisabled
                };
                var visibleTypes = PluginGraphBuilder.Build(_plugins, _model, unfocused).Nodes.Where(n => n.Kind == NodeKind.Plugin).Select(n => ((PluginTypeInfo)n.Tag).AssemblyId);
                var pending = _plugins.Assemblies
                    .Where(a => !a.Analyzed && (visibleTypes.Contains(a.Id) || filter.AssemblyId == a.Id))
                    .ToList();
                if (pending.Count > 0)
                {
                    AnalyzeAndRebuild(pending, relayout);
                    return;
                }
            }

            var previous = _pluginGraph;
            var selected = pluginCanvas.SelectedNode?.Key;
            var graph = PluginGraphBuilder.Build(_plugins, _model, filter);
            SyncLegends();
            pluginCanvas.ApplyLegendFilter(graph);
            if (!chkAnalyzeCode.Checked)
            {
                graph.Edges.RemoveAll(e => e.Kind == EdgeKind.Action);
                var linked = new HashSet<GraphNode>(graph.Edges.SelectMany(e => new[] { e.Source, e.Target }));
                graph.Nodes.RemoveAll(n => n.Kind == NodeKind.Table && !linked.Contains(n));
                graph.ByName.Clear();
                foreach (var node in graph.Nodes) graph.ByName[node.Key] = node;
            }

            var keep = !relayout && previous != null && graph.Nodes.All(n => previous.Find(n.Key) != null);
            if (keep)
            {
                foreach (var node in graph.Nodes)
                {
                    var old = previous.Find(node.Key);
                    node.X = old.X;
                    node.Y = old.Y;
                }
                GraphLayout.RouteEdges(graph);
            }
            else
            {
                GraphLayout.Apply(graph, (LayoutKind)Math.Max(0, cboPluginLayout.SelectedIndex));
            }

            _pluginGraph = graph;
            pluginCanvas.Graph = graph;
            if (!keep) pluginCanvas.FitToView();
            if (_pluginFocus == null) FillPluginFocus(graph);

            var reselect = graph.Find(selected) ?? graph.Nodes.FirstOrDefault(n => n.IsFocus);
            if (reselect != null) pluginCanvas.SelectNode(reselect);
            else ShowPluginDetails();
            UpdatePluginStatus();
            UpdatePluginSearch();
        }

        private void AnalyzeAndRebuild(List<PluginAssemblyInfo> assemblies, bool relayout)
        {
            var plugins = _plugins;
            var entities = _model != null
                ? new HashSet<string>(_model.Tables.Keys, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(plugins.Steps.Where(s => s.Entity != null).Select(s => s.Entity), StringComparer.OrdinalIgnoreCase);
            RunWork(new WorkAsyncInfo
            {
                Message = Strings.AnalyzingCode,
                Work = (worker, args) => new PluginService(Service).AnalyzeAssemblies(plugins, assemblies, entities, m => worker.ReportProgress(0, m)),
                ProgressChanged = e => _loading.SetMessage(e.UserState as string ?? Strings.AnalyzingCode),
                PostWorkCallBack = e =>
                {
                    if (e.Error != null)
                    {
                        // Never loop: the assemblies are flagged as analyzed even when the analysis failed.
                        foreach (var assembly in assemblies) assembly.Analyzed = true;
                        ShowError(Strings.AnalysisFailed, e.Error);
                    }
                    foreach (var assembly in assemblies) LogInfo("Code analysis of {0}: {1}", assembly.Name, assembly.AnalysisNote);
                    if (plugins == _plugins) RebuildPluginGraph(relayout);
                }
            });
        }

        private void FillPluginFocus(Graph graph)
        {
            cboPluginFocus.BeginUpdate();
            cboPluginFocus.Items.Clear();
            cboPluginFocus.Items.AddRange(graph.Nodes
                .OrderBy(n => n.Kind == NodeKind.Table ? 1 : 0)
                .ThenBy(n => n.Label, StringComparer.CurrentCultureIgnoreCase)
                .Select(n => (object)new ComboItem { Text = n.Kind == NodeKind.Table ? $"{n.Label} ({n.Key})" : $"{n.Label} · {n.SubLabel}", Value = n.Key })
                .ToArray());
            cboPluginFocus.EndUpdate();
            cboPluginFocus.Text = string.Empty;
        }

        private void ExplorePluginFromCombo()
        {
            var key = (cboPluginFocus.SelectedItem as ComboItem)?.Value as string
                      ?? cboPluginFocus.Items.Cast<ComboItem>().FirstOrDefault(i => string.Equals(i.Text, cboPluginFocus.Text, StringComparison.CurrentCultureIgnoreCase)
                                                                                     || string.Equals(i.Value as string, cboPluginFocus.Text, StringComparison.OrdinalIgnoreCase))?.Value as string;
            if (key == null)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            SetPluginFocus(key);
        }

        private void SetPluginFocus(string key)
        {
            _pluginFocus = key;
            if (key == null) cboPluginFocus.Text = string.Empty;
            RebuildPluginGraph(true);
        }

        /// <summary>From the « Plugins » tab of a table card: opens the plugin map around a plugin type.</summary>
        private void GoToPlugin(Guid typeId)
        {
            _pluginFocus = PluginGraphBuilder.TypePrefix + typeId;
            tabsMain.SelectedIndex = PagePlugins;
            if (_plugins != null) RebuildPluginGraph(true);
        }

        private void UpdatePluginStatus()
        {
            if (_plugins == null || _pluginGraph == null)
            {
                lblPluginStatus.Text = string.Empty;
                lblPluginAnalysis.Text = string.Empty;
                return;
            }
            var plugins = _pluginGraph.Nodes.Count(n => n.Kind == NodeKind.Plugin);
            var steps = _pluginGraph.Edges.Where(e => e.Kind == EdgeKind.Trigger).Sum(e => ((List<StepInfo>)e.Tag).Count);
            var actions = _pluginGraph.Edges.Count(e => e.Kind == EdgeKind.Action);
            var text = Strings.PluginStatus(plugins, steps, actions, _pluginGraph.Nodes.Count(n => n.Kind == NodeKind.Table));
            var focus = _pluginGraph.Nodes.FirstOrDefault(n => n.IsFocus);
            if (focus != null) text += Strings.FocusStatus(focus.Label, (int)numPluginDepth.Value);
            lblPluginStatus.Text = text;

            var analyzed = _plugins.Assemblies.Where(a => a.Analyzed).ToList();
            lblPluginAnalysis.Text = chkAnalyzeCode.Checked
                ? Strings.AnalysisStatus(analyzed.Count, analyzed.Count(a => a.AnalysisNote != null && a.AnalysisNote.StartsWith("!", StringComparison.Ordinal)))
                : Strings.AnalysisOff;
        }

        private void UpdatePluginSearch()
        {
            var text = txtPluginSearch.Text.Trim();
            _pluginSearchIndex = -1;
            if (text.Length == 0 || _pluginGraph == null)
            {
                _pluginSearchHits = new List<GraphNode>();
                pluginCanvas.SetSearchHits(null);
                lblPluginSearchInfo.Text = string.Empty;
                return;
            }
            bool Match(string value) => value != null && value.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0;
            _pluginSearchHits = _pluginGraph.Nodes
                .Where(n => Match(n.Label) || Match(n.SubLabel) || Match(n.Key) || n.Tag is PluginTypeInfo type && Match(type.TypeName))
                .OrderBy(n => n.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            pluginCanvas.SetSearchHits(_pluginSearchHits);
            lblPluginSearchInfo.Text = _pluginSearchHits.Count > 0 ? Strings.SearchHits(_pluginSearchHits.Count) : Strings.SearchNoMatch;
        }

        #endregion

        #region Details

        private void ShowPluginDetails()
        {
            var node = pluginCanvas.SelectedNode;
            var edge = pluginCanvas.SelectedEdge;
            dgvPluginProperties.Rows.Clear();
            dgvPluginSteps.Rows.Clear();
            dgvPluginOperations.Rows.Clear();
            lblPluginGridInfo.Text = string.Empty;

            if ((node == null && edge == null) || _plugins == null)
            {
                cardPluginDetails.Title = Strings.DetailsTitle;
                cardPluginDetails.Subtitle = Strings.PluginDetailsSubtitle;
                dgvPluginProperties.Visible = false;
                tabsPluginDetails.Visible = false;
                dgvPluginSteps.Visible = dgvPluginOperations.Visible = false;
                return;
            }

            IEnumerable<StepInfo> steps;
            IEnumerable<(PluginTypeInfo Type, CodeOperation Operation)> operations;
            var visibleSteps = _pluginGraph.Edges.Where(e => e.Kind == EdgeKind.Trigger).SelectMany(e => (List<StepInfo>)e.Tag).ToList();

            if (edge != null)
            {
                cardPluginDetails.Title = $"{edge.Source.Label} → {edge.Target.Label}";
                cardPluginDetails.Subtitle = edge.Kind == EdgeKind.Trigger ? Strings.TriggerSubtitle : Strings.ActionSubtitle;
                AddPluginProperty(edge.Kind == EdgeKind.Trigger ? Strings.PropTriggers : Strings.PropOperations, edge.Label);
                steps = edge.Kind == EdgeKind.Trigger ? (List<StepInfo>)edge.Tag : new List<StepInfo>();
                operations = edge.Kind == EdgeKind.Action
                    ? ((List<CodeOperation>)edge.Tag).Select(o => ((PluginTypeInfo)edge.Source.Tag, o))
                    : Enumerable.Empty<(PluginTypeInfo, CodeOperation)>();
                tabsPluginDetails.SelectedIndex = edge.Kind == EdgeKind.Trigger ? TabStepsIndex : TabOperationsIndex;
            }
            else if (node.Kind == NodeKind.Plugin)
            {
                var type = (PluginTypeInfo)node.Tag;
                var assembly = _plugins.FindAssembly(type.AssemblyId);
                cardPluginDetails.Title = type.ShortName;
                cardPluginDetails.Subtitle = type.TypeName;
                AddPluginProperty(Strings.PropAssembly, assembly?.ToString() ?? type.AssemblyName);
                if (assembly != null)
                {
                    AddPluginProperty(Strings.PropIsolation, assembly.IsolationMode == 2 ? Strings.Sandbox : Strings.NoIsolation);
                    AddPluginProperty(Strings.PropSource, Strings.SourceLabel(assembly));
                    AddPluginProperty(Strings.PropCodeAnalysis, assembly.Analyzed ? assembly.AnalysisNote?.TrimStart('!') : Strings.NotAnalyzed);
                }
                if (type.IsWorkflowActivity) AddPluginProperty(Strings.PropWorkflowActivity, Strings.Yes);
                steps = _plugins.Steps.Where(s => s.PluginTypeId == type.Id);
                operations = (type.Operations ?? new List<CodeOperation>()).Select(o => (type, o));
            }
            else if (node.Kind == NodeKind.Table)
            {
                cardPluginDetails.Title = node.Label;
                cardPluginDetails.Subtitle = node.Key;
                steps = visibleSteps.Where(s => string.Equals(s.Entity, node.Key, StringComparison.OrdinalIgnoreCase));
                operations = _pluginGraph.Nodes.Where(n => n.Kind == NodeKind.Plugin)
                    .SelectMany(n => (((PluginTypeInfo)n.Tag).Operations ?? new List<CodeOperation>())
                        .Where(o => string.Equals(o.Entity, node.Key, StringComparison.OrdinalIgnoreCase))
                        .Select(o => ((PluginTypeInfo)n.Tag, o)));
                AddPluginProperty(Strings.PropStepsOnTable, steps.Count().ToString());
                AddPluginProperty(Strings.PropPluginsWriting, operations.Where(o => !o.Operation.IsRead).Select(o => o.Type.Id).Distinct().Count().ToString());
            }
            else
            {
                cardPluginDetails.Title = node.Label;
                cardPluginDetails.Subtitle = node.SubLabel;
                steps = visibleSteps.Where(s => node.Kind == NodeKind.Endpoint ? (node.Tag is Guid id && s.EndpointId == id) : s.Entity == null && s.Message == node.Label);
                operations = Enumerable.Empty<(PluginTypeInfo, CodeOperation)>();
            }

            var stepList = steps.OrderBy(s => s.Entity).ThenBy(s => s.Message).ThenBy(s => s.Stage).ThenBy(s => s.Rank).ToList();
            foreach (var step in stepList)
            {
                var type = _plugins.FindType(step.PluginTypeId);
                var index = dgvPluginSteps.Rows.Add(step.Message, step.Entity ?? Strings.GlobalMessage, Strings.StageLabel(step.Stage), Strings.ModeLabel(step.Mode), step.Rank,
                    type?.ShortName ?? step.HandlerName, step.FilteringAttributes, Strings.ImagesLabel(step.Images), step.Enabled ? Strings.Enabled : Strings.Disabled);
                var row = dgvPluginSteps.Rows[index];
                row.Tag = step;
                row.Cells["colPsHandler"].ToolTipText = type?.TypeName;
                if (!step.Enabled) row.DefaultCellStyle.ForeColor = Theme.Subtle;
                if (!string.IsNullOrEmpty(step.Description)) row.Cells["colPsMessage"].ToolTipText = step.Description;
            }

            var operationList = operations.ToList();
            foreach (var (type, operation) in operationList)
            {
                var index = dgvPluginOperations.Rows.Add(type.ShortName, operation.Operation, operation.Entity ?? Strings.TableNotDetected, operation.Method);
                var row = dgvPluginOperations.Rows[index];
                row.Tag = operation;
                row.Cells["colPoMethod"].ToolTipText = operation.Inferred ? Strings.InferredTip : operation.Method;
                if (operation.IsRead) row.DefaultCellStyle.ForeColor = Theme.Muted;
                else row.Cells["colPoOperation"].Style.ForeColor = Theme.Danger;
                if (operation.Entity == null || operation.Inferred) row.Cells["colPoTable"].Style.ForeColor = Theme.Subtle;
            }

            tabsPluginDetails.Tabs[TabStepsIndex].Badge = stepList.Count.ToString();
            tabsPluginDetails.Tabs[TabOperationsIndex].Badge = operationList.Count.ToString();
            tabsPluginDetails.Invalidate();
            dgvPluginProperties.Height = Math.Max(30, Math.Min(dgvPluginProperties.Rows.Count * dgvPluginProperties.RowTemplate.Height + 4, 220));
            dgvPluginProperties.Visible = dgvPluginProperties.Rows.Count > 0;
            tabsPluginDetails.Visible = true;
            RestoreDockOrder(cardPluginDetails, pnlPluginPages, tabsPluginDetails, dgvPluginProperties);
            ShowPluginDetailPage();
        }

        private void AddPluginProperty(string key, string value) => dgvPluginProperties.Rows.Add(key, value ?? string.Empty);

        private void ShowPluginDetailPage()
        {
            var steps = tabsPluginDetails.SelectedIndex == TabStepsIndex;
            dgvPluginSteps.Visible = steps;
            dgvPluginOperations.Visible = !steps;
            var empty = steps ? dgvPluginSteps.Rows.Count == 0 : dgvPluginOperations.Rows.Count == 0;
            lblPluginGridInfo.Text = !empty ? string.Empty : steps ? Strings.NoSteps : chkAnalyzeCode.Checked ? Strings.NoOperations : Strings.AnalysisOff;
            lblPluginGridInfo.Visible = empty;
        }

        #endregion
    }
}
