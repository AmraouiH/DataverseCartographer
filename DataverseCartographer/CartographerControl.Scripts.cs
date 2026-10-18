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
    /// « JavaScript » page: table → form events → library → Dataverse calls of its code → table, filtered by solution or library.
    /// </summary>
    public partial class CartographerControl
    {
        private const int PageScripts = 2;
        private const int TabHandlersIndex = 0;
        private const int TabCallsIndex = 1;

        #region Controls

        private Panel pageScripts, pnlScriptPages;
        private CardPanel cardScriptFilters, cardScriptExplore, cardScriptMap, cardScriptDetails;
        private Label lblScriptSolution, lblScriptLibrary, lblScriptFocus, lblScriptDepth, lblScriptLayout, lblScriptStatus, lblScriptSearchInfo, lblScriptHint, lblScriptAnalysis, lblScriptGridInfo;
        private ComboBox cboScriptSolution, cboScriptLibrary, cboScriptFocus, cboScriptLayout;
        private CheckBox chkHideMsScripts, chkInactiveForms, chkScriptReads, chkAnalyzeScripts, chkScriptLabels;
        private NumericUpDown numScriptDepth;
        private ModernButton btnLoadScripts, btnScriptExplore, btnScriptWholeMap, btnScriptRelayout, btnScriptFit, btnScriptZoomIn, btnScriptZoomOut;
        private TextBox txtScriptSearch;
        private GraphCanvas scriptCanvas;
        private DataGridView dgvScriptProperties, dgvScriptHandlers, dgvScriptCalls;
        private SegmentedTabs tabsScriptDetails;

        #endregion

        private ScriptModel _scripts;
        private Graph _scriptGraph;
        private string _scriptFocus;
        private bool _scriptsLoading;
        private List<GraphNode> _scriptSearchHits = new List<GraphNode>();
        private int _scriptSearchIndex = -1;

        #region Layout

        private void BuildScriptsPage()
        {
            pageScripts = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas, Visible = false };

            cardScriptFilters = new CardPanel { Dock = DockStyle.Top, Height = 392, Step = 1 };
            var filters = Stack();
            lblScriptSolution = FieldLabel();
            cboScriptSolution = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 25, DropDownWidth = 420 };
            lblScriptLibrary = FieldLabel();
            cboScriptLibrary = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 25, DropDownWidth = 460 };
            chkHideMsScripts = Check();
            chkHideMsScripts.Margin = new Padding(0, 12, 0, 0);
            chkHideMsScripts.Checked = true;
            chkInactiveForms = Check();
            chkScriptReads = Check();
            chkAnalyzeScripts = Check();
            chkAnalyzeScripts.Checked = true;
            btnLoadScripts = new ModernButton { Variant = ButtonVariant.Secondary, Glyph = IconKind.Reload, AutoSize = true, MinimumSize = new Size(120, 30), Height = 30, Margin = new Padding(0, 12, 0, 0) };
            AddRows(filters, lblScriptSolution, cboScriptSolution, lblScriptLibrary, cboScriptLibrary, chkHideMsScripts, chkInactiveForms, chkScriptReads, chkAnalyzeScripts, btnLoadScripts);
            cardScriptFilters.Controls.Add(filters);

            cardScriptExplore = new CardPanel { Dock = DockStyle.Fill, Step = 2 };
            var explore = Stack();
            lblScriptFocus = FieldLabel();
            lblScriptFocus.Margin = new Padding(0, 0, 0, 4);
            cboScriptFocus = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                MaxDropDownItems = 25,
                DropDownWidth = 380
            };
            var depthRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Card, Margin = new Padding(0, 8, 0, 0) };
            lblScriptDepth = FieldLabel();
            lblScriptDepth.Margin = new Padding(0, 6, 8, 0);
            numScriptDepth = new NumericUpDown { Minimum = 1, Maximum = 6, Value = 1, Width = 52, Margin = new Padding(0, 3, 12, 0) };
            btnScriptExplore = new ModernButton { Variant = ButtonVariant.Primary, Glyph = IconKind.Target, AutoSize = true, MinimumSize = new Size(100, 30), Height = 30, Margin = Padding.Empty };
            depthRow.Controls.AddRange(new Control[] { lblScriptDepth, numScriptDepth, btnScriptExplore });
            btnScriptWholeMap = new ModernButton { Variant = ButtonVariant.Secondary, Glyph = IconKind.Graph, AutoSize = true, MinimumSize = new Size(110, 30), Height = 30, Margin = new Padding(0, 10, 0, 0) };
            lblScriptAnalysis = new Label { AutoSize = true, MaximumSize = new Size(250, 0), ForeColor = Theme.Muted, Margin = new Padding(0, 14, 0, 0) };
            lblScriptHint = new Label { AutoSize = true, MaximumSize = new Size(250, 0), ForeColor = Theme.Subtle, Font = Theme.Small, Margin = new Padding(0, 12, 0, 0) };
            AddRows(explore, lblScriptFocus, cboScriptFocus, depthRow, btnScriptWholeMap, lblScriptAnalysis, lblScriptHint);
            cardScriptExplore.Controls.Add(explore);

            var left = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = Theme.Canvas };
            left.Controls.Add(cardScriptExplore);
            left.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Theme.Canvas });
            left.Controls.Add(cardScriptFilters);

            cardScriptMap = new CardPanel { Dock = DockStyle.Fill };
            var toolbar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Card };
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Card };
            lblScriptLayout = FieldLabel();
            lblScriptLayout.Margin = new Padding(0, 9, 6, 0);
            cboScriptLayout = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Margin = new Padding(0, 5, 8, 0) };
            btnScriptRelayout = ToolButton(IconKind.Layout);
            btnScriptFit = ToolButton(IconKind.Fit);
            btnScriptFit.MinimumSize = new Size(32, 30);
            btnScriptZoomIn = ToolButton(IconKind.ZoomIn);
            btnScriptZoomIn.MinimumSize = new Size(32, 30);
            btnScriptZoomOut = ToolButton(IconKind.ZoomOut);
            btnScriptZoomOut.MinimumSize = new Size(32, 30);
            chkScriptLabels = new CheckBox { AutoSize = true, Margin = new Padding(6, 9, 14, 0), Checked = true };
            tools.Controls.AddRange(new Control[] { lblScriptLayout, cboScriptLayout, btnScriptRelayout, btnScriptFit, btnScriptZoomIn, btnScriptZoomOut, chkScriptLabels });
            var searchHost = new Panel { Dock = DockStyle.Right, Width = 170, BackColor = Theme.Card, Padding = new Padding(0, 7, 0, 0) };
            txtScriptSearch = new TextBox { Dock = DockStyle.Top };
            searchHost.Controls.Add(txtScriptSearch);
            toolbar.Controls.Add(tools);
            toolbar.Controls.Add(searchHost);

            var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Theme.Card };
            lblScriptStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, Font = Theme.Small, BackColor = Theme.Card, AutoEllipsis = true };
            lblScriptSearchInfo = new Label { Dock = DockStyle.Right, AutoSize = true, ForeColor = Theme.SearchHit, Font = Theme.Semibold, BackColor = Theme.Card, Padding = new Padding(0, 6, 0, 0) };
            statusBar.Controls.Add(lblScriptStatus);
            statusBar.Controls.Add(lblScriptSearchInfo);

            scriptCanvas = new GraphCanvas { Dock = DockStyle.Fill, ShowLabels = true };
            var canvasHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0), BackColor = Theme.Card };
            canvasHost.Controls.Add(scriptCanvas);
            cardScriptMap.Controls.Add(canvasHost);
            cardScriptMap.Controls.Add(statusBar);
            cardScriptMap.Controls.Add(toolbar);

            cardScriptDetails = new CardPanel { Dock = DockStyle.Right, Width = 440 };
            dgvScriptProperties = new DataGridView
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
            Theme.StyleGrid(dgvScriptProperties, false);
            dgvScriptProperties.RowTemplate.Height = 26;
            dgvScriptProperties.Columns.Add(new DataGridViewTextBoxColumn { Name = "colKey", FillWeight = 38, DefaultCellStyle = { ForeColor = Theme.Muted, Font = Theme.Semibold } });
            dgvScriptProperties.Columns.Add(new DataGridViewTextBoxColumn { Name = "colValue", FillWeight = 62 });

            tabsScriptDetails = new SegmentedTabs { Dock = DockStyle.Top, Height = 44 };
            tabsScriptDetails.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Play, BadgeColor = Theme.Accent });
            tabsScriptDetails.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Code, BadgeColor = Theme.Danger });

            pnlScriptPages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
            dgvScriptHandlers = DetailGrid();
            AddColumns(dgvScriptHandlers, ("colShTable", 16), ("colShForm", 22), ("colShEvent", 22), ("colShFunction", 26), ("colShLibrary", 22));
            dgvScriptCalls = DetailGrid();
            AddColumns(dgvScriptCalls, ("colScLibrary", 22), ("colScOperation", 18), ("colScTable", 18), ("colScFunction", 24), ("colScLine", 8));
            lblScriptGridInfo = new Label { Dock = DockStyle.Top, Height = 26, ForeColor = Theme.Muted, Font = Theme.Small, TextAlign = ContentAlignment.MiddleLeft, Visible = false };
            pnlScriptPages.Controls.AddRange(new Control[] { dgvScriptHandlers, dgvScriptCalls, lblScriptGridInfo });

            cardScriptDetails.Controls.Add(pnlScriptPages);
            cardScriptDetails.Controls.Add(tabsScriptDetails);
            cardScriptDetails.Controls.Add(dgvScriptProperties);

            pageScripts.Controls.Add(cardScriptMap);
            pageScripts.Controls.Add(new Splitter { Dock = DockStyle.Right, Width = 10, BackColor = Theme.Canvas, MinExtra = 300, MinSize = 300 });
            pageScripts.Controls.Add(cardScriptDetails);
            pageScripts.Controls.Add(new Splitter { Dock = DockStyle.Left, Width = 10, BackColor = Theme.Canvas, MinExtra = 300, MinSize = 240 });
            pageScripts.Controls.Add(left);
        }

        private void ApplyScriptsLanguage()
        {
            tabsMain.Tabs[PageScripts].Text = Strings.TabJavaScript;
            cardScriptFilters.Title = Strings.ScriptFiltersTitle;
            cardScriptFilters.Subtitle = Strings.ScriptFiltersSubtitle;
            lblScriptSolution.Text = Strings.SolutionLabel;
            lblScriptLibrary.Text = Strings.LibraryLabel;
            chkHideMsScripts.Text = Strings.HideMicrosoftScripts;
            toolTip.SetToolTip(chkHideMsScripts, Strings.HideMicrosoftScriptsTip);
            chkInactiveForms.Text = Strings.IncludeInactiveForms;
            chkScriptReads.Text = Strings.ShowScriptReads;
            toolTip.SetToolTip(chkScriptReads, Strings.ShowScriptReadsTip);
            chkAnalyzeScripts.Text = Strings.AnalyzeScripts;
            toolTip.SetToolTip(chkAnalyzeScripts, Strings.AnalyzeScriptsTip);
            btnLoadScripts.Text = Strings.LoadScripts;
            FillScriptSolutions();
            FillScriptLibraries();

            cardScriptExplore.Title = Strings.ExploreTitle;
            cardScriptExplore.Subtitle = Strings.ScriptExploreSubtitle;
            lblScriptFocus.Text = Strings.ScriptFocusLabel;
            lblScriptDepth.Text = Strings.DepthLabel;
            btnScriptExplore.Text = Strings.ShowFocus;
            btnScriptWholeMap.Text = Strings.ClearFocus;
            lblScriptHint.Text = Strings.ScriptHint + " " + Strings.LegendHint + ".";
            Theme.SetCue(cboScriptFocus, Strings.ScriptFocusCue);

            lblScriptLayout.Text = Strings.LayoutLabel;
            Refill(cboScriptLayout, Strings.Layouts);
            btnScriptRelayout.Text = Strings.Relayout;
            toolTip.SetToolTip(btnScriptRelayout, Strings.RelayoutTip);
            toolTip.SetToolTip(btnScriptFit, Strings.FitTip);
            toolTip.SetToolTip(btnScriptZoomIn, Strings.ZoomInTip);
            toolTip.SetToolTip(btnScriptZoomOut, Strings.ZoomOutTip);
            chkScriptLabels.Text = Strings.Labels;
            Theme.SetCue(txtScriptSearch, Strings.ScriptSearchCue);

            tabsScriptDetails.Tabs[TabHandlersIndex].Text = Strings.TabHandlers;
            tabsScriptDetails.Tabs[TabCallsIndex].Text = Strings.TabScriptCalls;
            tabsScriptDetails.Invalidate();
            SetHeaders(dgvScriptHandlers, Strings.ColTable, Strings.ColForm, Strings.ColEvent, Strings.ColFunction, Strings.ColLibrary);
            SetHeaders(dgvScriptCalls, Strings.ColLibrary, Strings.ColOperation, Strings.ColTable, Strings.ColFunctionCall, Strings.ColLine);

            scriptCanvas.Legend = new[]
            {
                new GraphCanvas.LegendItem(Strings.LegendTable, Theme.StandardTable, GraphCanvas.LegendStyle.Dot, "table", nodes: n => n.Kind == NodeKind.Table),
                new GraphCanvas.LegendItem(Strings.LegendScript, GraphRenderer.ScriptNode, GraphCanvas.LegendStyle.Square, "script", nodes: n => n.Kind == NodeKind.Script),
                new GraphCanvas.LegendItem(Strings.LegendFormEvent, GraphRenderer.SyncTrigger, GraphCanvas.LegendStyle.Line, "event", edges: e => e.Kind == EdgeKind.Trigger && !e.Muted),
                new GraphCanvas.LegendItem(Strings.LegendLoaded, Theme.GhostTable, GraphCanvas.LegendStyle.Dashed, "loaded", edges: e => e.Kind == EdgeKind.Trigger && e.Muted),
                new GraphCanvas.LegendItem(Strings.LegendWrite, GraphRenderer.WriteAction, GraphCanvas.LegendStyle.Line, "write", edges: e => e.Kind == EdgeKind.Action && !e.Muted),
                new GraphCanvas.LegendItem(Strings.LegendRead, GraphRenderer.ReadAction, GraphCanvas.LegendStyle.Dashed, "read", linked: chkScriptReads)
            };
            SyncLegends();
            scriptCanvas.EmptyText = _scripts == null ? Strings.ScriptsEmptyNotLoaded : Strings.ScriptsEmptyNoMatch;
            scriptCanvas.Invalidate();
            UpdateScriptStatus();
            ShowScriptDetails();
        }

        #endregion

        #region Events

        private void WireScriptEvents()
        {
            cboScriptSolution.SelectedIndexChanged += (s, e) => OnScriptSolutionChanged();
            cboScriptLibrary.SelectedIndexChanged += (s, e) => OnScriptFilterChanged();
            chkHideMsScripts.CheckedChanged += (s, e) =>
            {
                if (_suppressEvents) return;
                FillScriptLibraries();
                OnScriptFilterChanged();
            };
            chkInactiveForms.CheckedChanged += (s, e) => OnScriptFilterChanged();
            chkScriptReads.CheckedChanged += (s, e) => OnScriptFilterChanged();
            chkAnalyzeScripts.CheckedChanged += (s, e) => OnScriptFilterChanged();
            btnLoadScripts.Click += (s, e) => ExecuteMethod(LoadScripts);

            btnScriptExplore.Click += (s, e) => ExploreScriptFromCombo();
            cboScriptFocus.SelectionChangeCommitted += (s, e) => BeginInvoke(new Action(ExploreScriptFromCombo));
            cboScriptFocus.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                ExploreScriptFromCombo();
            };
            numScriptDepth.ValueChanged += (s, e) =>
            {
                if (_scriptFocus != null) OnScriptFilterChanged();
            };
            btnScriptWholeMap.Click += (s, e) => SetScriptFocus(null);

            cboScriptLayout.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressEvents) return;
                SaveSettings();
                if (_scriptGraph == null) return;
                GraphLayout.Apply(_scriptGraph, (LayoutKind)Math.Max(0, cboScriptLayout.SelectedIndex));
                scriptCanvas.FitToView();
            };
            btnScriptRelayout.Click += (s, e) =>
            {
                if (_scriptGraph == null) return;
                GraphLayout.Apply(_scriptGraph, (LayoutKind)Math.Max(0, cboScriptLayout.SelectedIndex));
                scriptCanvas.FitToView();
            };
            btnScriptFit.Click += (s, e) => scriptCanvas.FitToView();
            btnScriptZoomIn.Click += (s, e) => scriptCanvas.ZoomBy(1.25f);
            btnScriptZoomOut.Click += (s, e) => scriptCanvas.ZoomBy(1 / 1.25f);
            chkScriptLabels.CheckedChanged += (s, e) => scriptCanvas.ShowLabels = chkScriptLabels.Checked;

            txtScriptSearch.TextChanged += (s, e) => UpdateScriptSearch();
            txtScriptSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    txtScriptSearch.Clear();
                    return;
                }
                if (e.KeyCode != Keys.Enter || _scriptSearchHits.Count == 0) return;
                e.SuppressKeyPress = true;
                _scriptSearchIndex = (_scriptSearchIndex + 1) % _scriptSearchHits.Count;
                var node = _scriptSearchHits[_scriptSearchIndex];
                scriptCanvas.SelectNode(node);
                scriptCanvas.CenterOn(node);
                lblScriptSearchInfo.Text = $"{_scriptSearchIndex + 1}/{_scriptSearchHits.Count}";
            };

            scriptCanvas.SelectionChanged += (s, e) =>
            {
                ShowScriptDetails();
                RestoreDockOrder(cardScriptDetails, pnlScriptPages, tabsScriptDetails, dgvScriptProperties);
            };
            scriptCanvas.NodeDoubleClicked += (s, node) => SetScriptFocus(node.Key);
            scriptCanvas.Resize += (s, e) =>
            {
                if (_scriptGraph != null && scriptCanvas.ZoomLevel == 1f) scriptCanvas.FitToView();
            };
            tabsScriptDetails.SelectedIndexChanged += (s, e) => ShowScriptDetailPage();
            dgvScriptCalls.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0 || !(dgvScriptCalls.Rows[e.RowIndex].Tag is ScriptCall call) || call.Entity == null) return;
                var node = _scriptGraph?.Find(call.Entity);
                if (node == null) return;
                scriptCanvas.SelectNode(node);
                scriptCanvas.CenterOn(node);
            };
        }

        private void OnScriptFilterChanged()
        {
            if (_suppressEvents || !_controlLoaded) return;
            SaveSettings();
            if (_scripts == null) return;
            RebuildScriptGraph(true);
        }

        private SolutionItem SelectedScriptSolution => (cboScriptSolution.SelectedItem as ComboItem)?.Value as SolutionItem;

        /// <summary>A solution narrows the library list to the scripts of its forms / web resources.</summary>
        private void OnScriptSolutionChanged()
        {
            if (_suppressEvents || !_controlLoaded || _scripts == null) return;
            var solution = SelectedScriptSolution;
            if (solution != null && !_scripts.SolutionComponents.ContainsKey(solution.Id))
            {
                var scripts = _scripts;
                RunWork(new WorkAsyncInfo
                {
                    Message = Strings.ReadingSolutionTables,
                    Work = (worker, args) => args.Result = new ScriptService(Service).LoadSolutionComponents(solution.Id),
                    PostWorkCallBack = e =>
                    {
                        if (e.Error != null)
                        {
                            ShowError(Strings.SolutionFailed, e.Error);
                            return;
                        }
                        scripts.SolutionComponents[solution.Id] = (HashSet<Guid>)e.Result;
                        if (scripts != _scripts) return;
                        FillScriptLibraries();
                        RebuildScriptGraph(true);
                    }
                });
                return;
            }
            FillScriptLibraries();
            RebuildScriptGraph(true);
        }

        #endregion

        #region Loading

        private void LoadScripts()
        {
            if (_busy) return;
            _scriptsLoading = true;
            RunWork(new WorkAsyncInfo
            {
                Message = Strings.LoadingScripts,
                Work = (worker, args) => args.Result = new ScriptService(Service).LoadModel(m => worker.ReportProgress(0, m)),
                ProgressChanged = e => _loading.SetMessage(e.UserState as string ?? Strings.LoadingScripts),
                PostWorkCallBack = e =>
                {
                    _scriptsLoading = false;
                    if (e.Error != null)
                    {
                        ShowError(Strings.ScriptsLoadFailed, e.Error);
                        return;
                    }
                    _scripts = (ScriptModel)e.Result;
                    LogInfo("Scripts loaded: {0} forms, {1} libraries.", _scripts.Forms.Count, _scripts.Libraries.Count);
                    _suppressEvents = true;
                    FillScriptSolutions();
                    FillScriptLibraries();
                    _suppressEvents = false;
                    scriptCanvas.EmptyText = Strings.ScriptsEmptyNoMatch;
                    RebuildScriptGraph(true);
                }
            });
        }

        private void FillScriptSolutions()
        {
            var previous = SelectedScriptSolution;
            var wasSuppressed = _suppressEvents;
            _suppressEvents = true;
            cboScriptSolution.BeginUpdate();
            cboScriptSolution.Items.Clear();
            cboScriptSolution.Items.Add(new ComboItem { Text = Strings.AllPluginSolutions });
            foreach (var solution in _model?.Solutions ?? new List<SolutionItem>())
            {
                cboScriptSolution.Items.Add(new ComboItem { Text = solution.ToString(), Value = solution });
            }
            var index = previous == null ? 0 : cboScriptSolution.Items.Cast<ComboItem>().ToList().FindIndex(i => (i.Value as SolutionItem)?.Id == previous.Id);
            cboScriptSolution.SelectedIndex = Math.Max(0, index);
            cboScriptSolution.EndUpdate();
            _suppressEvents = wasSuppressed;
        }

        private void FillScriptLibraries()
        {
            var previous = (cboScriptLibrary.SelectedItem as ComboItem)?.Value as string;
            var wasSuppressed = _suppressEvents;
            _suppressEvents = true;
            cboScriptLibrary.BeginUpdate();
            cboScriptLibrary.Items.Clear();
            cboScriptLibrary.Items.Add(new ComboItem { Text = Strings.AllLibraries });
            if (_scripts != null)
            {
                var solution = SelectedScriptSolution;
                HashSet<Guid> components = null;
                if (solution != null) _scripts.SolutionComponents.TryGetValue(solution.Id, out components);
                var filter = new ScriptFilter { HideMicrosoft = chkHideMsScripts.Checked, SolutionComponents = components };
                foreach (var library in _scripts.Libraries.Values.Where(l => ScriptGraphBuilder.LibraryAllowed(l, filter)).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
                {
                    cboScriptLibrary.Items.Add(new ComboItem { Text = Strings.LibraryItem(library.Name, library.Forms.Count), Value = library.Name });
                }
            }
            var index = previous == null ? 0 : cboScriptLibrary.Items.Cast<ComboItem>().ToList().FindIndex(i => string.Equals(i.Value as string, previous, StringComparison.OrdinalIgnoreCase));
            cboScriptLibrary.SelectedIndex = Math.Max(0, index);
            cboScriptLibrary.EndUpdate();
            _suppressEvents = wasSuppressed;
        }

        #endregion

        #region Graph

        private ScriptFilter CurrentScriptFilter(bool withFocus)
        {
            var solution = SelectedScriptSolution;
            HashSet<Guid> components = null;
            if (solution != null) _scripts.SolutionComponents.TryGetValue(solution.Id, out components);
            return new ScriptFilter
            {
                SolutionComponents = components,
                Library = (cboScriptLibrary.SelectedItem as ComboItem)?.Value as string,
                HideMicrosoft = chkHideMsScripts.Checked,
                IncludeInactiveForms = chkInactiveForms.Checked,
                ShowReads = chkScriptReads.Checked,
                Focus = withFocus ? _scriptFocus : null,
                Depth = (int)numScriptDepth.Value
            };
        }

        private void RebuildScriptGraph(bool relayout)
        {
            if (_scripts == null)
            {
                scriptCanvas.Graph = null;
                UpdateScriptStatus();
                return;
            }

            var filter = CurrentScriptFilter(true);

            // The code of the libraries on the map is analyzed (once) before drawing the calls.
            if (chkAnalyzeScripts.Checked)
            {
                var pending = _scripts.Libraries.Values
                    .Where(l => !l.Analyzed && ScriptGraphBuilder.LibraryAllowed(l, CurrentScriptFilter(false)))
                    .ToList();
                if (pending.Count > 0)
                {
                    AnalyzeScriptsAndRebuild(pending, relayout);
                    return;
                }
            }

            var previous = _scriptGraph;
            var selected = scriptCanvas.SelectedNode?.Key;
            var graph = ScriptGraphBuilder.Build(_scripts, _model, filter);
            SyncLegends();
            scriptCanvas.ApplyLegendFilter(graph);
            if (!chkAnalyzeScripts.Checked)
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
                GraphLayout.Apply(graph, (LayoutKind)Math.Max(0, cboScriptLayout.SelectedIndex));
            }

            _scriptGraph = graph;
            scriptCanvas.Graph = graph;
            if (!keep) scriptCanvas.FitToView();
            if (_scriptFocus == null) FillScriptFocus(graph);

            var reselect = graph.Find(selected) ?? graph.Nodes.FirstOrDefault(n => n.IsFocus);
            if (reselect != null) scriptCanvas.SelectNode(reselect);
            else ShowScriptDetails();
            UpdateScriptStatus();
            UpdateScriptSearch();
        }

        private void AnalyzeScriptsAndRebuild(List<ScriptLibraryInfo> libraries, bool relayout)
        {
            var scripts = _scripts;
            var tables = _model != null
                ? new HashSet<string>(_model.Tables.Keys, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(scripts.Forms.Select(f => f.Entity).Where(e => e != null), StringComparer.OrdinalIgnoreCase);
            var sets = _model?.EntitySets() ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            RunWork(new WorkAsyncInfo
            {
                Message = Strings.AnalyzingScripts,
                Work = (worker, args) => new ScriptService(Service).AnalyzeLibraries(libraries, tables, sets, m => worker.ReportProgress(0, m)),
                ProgressChanged = e => _loading.SetMessage(e.UserState as string ?? Strings.AnalyzingScripts),
                PostWorkCallBack = e =>
                {
                    if (e.Error != null)
                    {
                        foreach (var library in libraries) library.Analyzed = true;
                        ShowError(Strings.AnalysisFailed, e.Error);
                    }
                    if (scripts == _scripts) RebuildScriptGraph(relayout);
                }
            });
        }

        private void FillScriptFocus(Graph graph)
        {
            cboScriptFocus.BeginUpdate();
            cboScriptFocus.Items.Clear();
            cboScriptFocus.Items.AddRange(graph.Nodes
                .OrderBy(n => n.Kind == NodeKind.Table ? 1 : 0)
                .ThenBy(n => n.Label, StringComparer.CurrentCultureIgnoreCase)
                .Select(n => (object)new ComboItem { Text = n.Kind == NodeKind.Table ? $"{n.Label} ({n.Key})" : n.SubLabel, Value = n.Key })
                .ToArray());
            cboScriptFocus.EndUpdate();
            cboScriptFocus.Text = string.Empty;
        }

        private void ExploreScriptFromCombo()
        {
            var key = (cboScriptFocus.SelectedItem as ComboItem)?.Value as string
                      ?? cboScriptFocus.Items.Cast<ComboItem>().FirstOrDefault(i => string.Equals(i.Text, cboScriptFocus.Text, StringComparison.CurrentCultureIgnoreCase)
                                                                                     || string.Equals(i.Value as string, cboScriptFocus.Text, StringComparison.OrdinalIgnoreCase))?.Value as string;
            if (key == null)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            SetScriptFocus(key);
        }

        private void SetScriptFocus(string key)
        {
            _scriptFocus = key;
            if (key == null) cboScriptFocus.Text = string.Empty;
            RebuildScriptGraph(true);
        }

        private void UpdateScriptStatus()
        {
            if (_scripts == null || _scriptGraph == null)
            {
                lblScriptStatus.Text = string.Empty;
                lblScriptAnalysis.Text = string.Empty;
                return;
            }
            var libraries = _scriptGraph.Nodes.Count(n => n.Kind == NodeKind.Script);
            var handlers = _scriptGraph.Edges.Where(e => e.Kind == EdgeKind.Trigger).Sum(e => ((List<FormHandler>)e.Tag).Count);
            var calls = _scriptGraph.Edges.Count(e => e.Kind == EdgeKind.Action);
            var text = Strings.ScriptStatus(libraries, handlers, calls, _scriptGraph.Nodes.Count(n => n.Kind == NodeKind.Table));
            var focus = _scriptGraph.Nodes.FirstOrDefault(n => n.IsFocus);
            if (focus != null) text += Strings.FocusStatus(focus.Label, (int)numScriptDepth.Value);
            lblScriptStatus.Text = text;
            lblScriptAnalysis.Text = chkAnalyzeScripts.Checked ? Strings.ScriptAnalysisStatus(_scripts.Libraries.Values.Count(l => l.Analyzed)) : Strings.ScriptAnalysisOff;
        }

        private void UpdateScriptSearch()
        {
            var text = txtScriptSearch.Text.Trim();
            _scriptSearchIndex = -1;
            if (text.Length == 0 || _scriptGraph == null)
            {
                _scriptSearchHits = new List<GraphNode>();
                scriptCanvas.SetSearchHits(null);
                lblScriptSearchInfo.Text = string.Empty;
                return;
            }
            bool Match(string value) => value != null && value.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0;
            _scriptSearchHits = _scriptGraph.Nodes
                .Where(n => Match(n.Label) || Match(n.SubLabel) || Match(n.Key)
                            || n.Tag is ScriptLibraryInfo library && library.Handlers.Any(h => Match(h.Function)))
                .OrderBy(n => n.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            scriptCanvas.SetSearchHits(_scriptSearchHits);
            lblScriptSearchInfo.Text = _scriptSearchHits.Count > 0 ? Strings.SearchHits(_scriptSearchHits.Count) : Strings.SearchNoMatch;
        }

        #endregion

        #region Details

        private void ShowScriptDetails()
        {
            var node = scriptCanvas.SelectedNode;
            var edge = scriptCanvas.SelectedEdge;
            dgvScriptProperties.Rows.Clear();
            dgvScriptHandlers.Rows.Clear();
            dgvScriptCalls.Rows.Clear();

            if ((node == null && edge == null) || _scripts == null)
            {
                cardScriptDetails.Title = Strings.DetailsTitle;
                cardScriptDetails.Subtitle = Strings.ScriptDetailsSubtitle;
                dgvScriptProperties.Visible = false;
                tabsScriptDetails.Visible = false;
                dgvScriptHandlers.Visible = dgvScriptCalls.Visible = lblScriptGridInfo.Visible = false;
                RestoreDockOrder(cardScriptDetails, pnlScriptPages, tabsScriptDetails, dgvScriptProperties);
                return;
            }

            var handlers = new List<(string Table, FormHandler Handler)>();
            var calls = new List<(ScriptLibraryInfo Library, ScriptCall Call)>();
            if (edge != null)
            {
                cardScriptDetails.Title = $"{edge.Source.Label} → {edge.Target.Label}";
                cardScriptDetails.Subtitle = edge.Kind == EdgeKind.Trigger ? Strings.ScriptTriggerSubtitle : Strings.ScriptActionSubtitle;
                AddScriptProperty(edge.Kind == EdgeKind.Trigger ? Strings.ColEvent : Strings.PropOperations, edge.Label);
                if (edge.Kind == EdgeKind.Trigger) handlers.AddRange(((List<FormHandler>)edge.Tag).Select(h => (edge.Source.Key, h)));
                else calls.AddRange(((List<ScriptCall>)edge.Tag).Select(c => ((ScriptLibraryInfo)edge.Source.Tag, c)));
                tabsScriptDetails.SelectedIndex = edge.Kind == EdgeKind.Trigger ? TabHandlersIndex : TabCallsIndex;
            }
            else if (node.Kind == NodeKind.Script)
            {
                var library = (ScriptLibraryInfo)node.Tag;
                cardScriptDetails.Title = node.Label;
                cardScriptDetails.Subtitle = library.Name;
                AddScriptProperty(Strings.PropLibrary, library.DisplayName ?? library.Name);
                AddScriptProperty(Strings.PropForms, string.Join(", ", library.Forms.Select(f => $"{f.Name} ({f.Entity})").Distinct().Take(12)));
                AddScriptProperty(Strings.PropCodeAnalysis, library.Analyzed ? library.AnalysisNote?.TrimStart('!') : Strings.NotAnalyzed);
                if (library.IsManaged) AddScriptProperty("Managed", Strings.Yes);
                foreach (var form in library.Forms)
                {
                    handlers.AddRange(form.Handlers.Where(h => string.Equals(h.Library, library.Name, StringComparison.OrdinalIgnoreCase)).Select(h => (form.Entity, h)));
                }
                calls.AddRange((library.Calls ?? new List<ScriptCall>()).Select(c => (library, c)));
            }
            else
            {
                cardScriptDetails.Title = node.Label;
                cardScriptDetails.Subtitle = node.Key;
                foreach (var library in _scriptGraph.Nodes.Where(n => n.Kind == NodeKind.Script).Select(n => (ScriptLibraryInfo)n.Tag))
                {
                    foreach (var form in library.Forms.Where(f => string.Equals(f.Entity, node.Key, StringComparison.OrdinalIgnoreCase)))
                    {
                        handlers.AddRange(form.Handlers.Where(h => string.Equals(h.Library, library.Name, StringComparison.OrdinalIgnoreCase)).Select(h => (form.Entity, h)));
                    }
                    calls.AddRange((library.Calls ?? new List<ScriptCall>()).Where(c => string.Equals(c.Entity, node.Key, StringComparison.OrdinalIgnoreCase)).Select(c => (library, c)));
                }
                AddScriptProperty(Strings.PropHandlersOnTable, handlers.Count.ToString());
                AddScriptProperty(Strings.PropScriptsCalling, calls.Select(c => c.Library.Name).Distinct().Count().ToString());
            }

            foreach (var (table, handler) in handlers.Distinct())
            {
                var index = dgvScriptHandlers.Rows.Add(table, handler.Form, Strings.EventLabel(handler.Event, handler.Source), handler.Function, ScriptGraphBuilder.ShortName(handler.Library ?? string.Empty));
                var row = dgvScriptHandlers.Rows[index];
                row.Cells["colShLibrary"].ToolTipText = handler.Library;
                if (!handler.Enabled) row.DefaultCellStyle.ForeColor = Theme.Subtle;
            }
            foreach (var (library, call) in calls)
            {
                var index = dgvScriptCalls.Rows.Add(ScriptGraphBuilder.ShortName(library.Name), call.Operation, call.Entity ?? Strings.TableNotDetected, call.Function, call.Line);
                var row = dgvScriptCalls.Rows[index];
                row.Tag = call;
                row.Cells["colScLibrary"].ToolTipText = library.Name;
                if (call.IsRead) row.DefaultCellStyle.ForeColor = Theme.Muted;
                else row.Cells["colScOperation"].Style.ForeColor = Theme.Danger;
                if (call.Entity == null) row.Cells["colScTable"].Style.ForeColor = Theme.Subtle;
            }

            tabsScriptDetails.Tabs[TabHandlersIndex].Badge = dgvScriptHandlers.Rows.Count.ToString();
            tabsScriptDetails.Tabs[TabCallsIndex].Badge = dgvScriptCalls.Rows.Count.ToString();
            tabsScriptDetails.Invalidate();
            dgvScriptProperties.Height = Math.Max(30, Math.Min(dgvScriptProperties.Rows.Count * dgvScriptProperties.RowTemplate.Height + 4, 220));
            dgvScriptProperties.Visible = dgvScriptProperties.Rows.Count > 0;
            tabsScriptDetails.Visible = true;
            RestoreDockOrder(cardScriptDetails, pnlScriptPages, tabsScriptDetails, dgvScriptProperties);
            ShowScriptDetailPage();
        }

        private void AddScriptProperty(string key, string value) => dgvScriptProperties.Rows.Add(key, value ?? string.Empty);

        private void ShowScriptDetailPage()
        {
            var handlers = tabsScriptDetails.SelectedIndex == TabHandlersIndex;
            dgvScriptHandlers.Visible = handlers;
            dgvScriptCalls.Visible = !handlers;
            var empty = handlers ? dgvScriptHandlers.Rows.Count == 0 : dgvScriptCalls.Rows.Count == 0;
            lblScriptGridInfo.Text = !empty ? string.Empty : handlers ? Strings.NoHandlers : chkAnalyzeScripts.Checked ? Strings.NoScriptCalls : Strings.ScriptAnalysisOff;
            lblScriptGridInfo.Visible = empty;
        }

        #endregion
    }
}
