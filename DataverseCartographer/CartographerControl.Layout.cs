using System.Drawing;
using System.Windows.Forms;
using DataverseCartographer.UI;
using DataverseCartographer.UI.Controls;

namespace DataverseCartographer
{
    /// <summary>
    /// Construction of the tool UI (built in code: header, filters and explore cards, map, details).
    /// Docking rule used everywhere: the Fill control is added first, then the edge controls from the innermost to the outermost.
    /// </summary>
    public partial class CartographerControl
    {
        #region Controls

        private ToolStrip tsHeader;
        private ToolStripLabel tslLogo;
        private ToolStripLabel tslSubtitle;
        private ToolStripButton tsbClose;
        private ToolStripButton tsbLoad;
        private ToolStripDropDownButton tsddExport;
        private ToolStripMenuItem tsmiPng, tsmiSvg, tsmiMermaid, tsmiPlantUml, tsmiExcel, tsmiCopyMermaid, tsmiCopyPlantUml;
        private ToolStripDropDownButton tsddLanguage;
        private ToolStripMenuItem tsmiEnglish;
        private ToolStripMenuItem tsmiFrench;
        private ToolStripButton tsbAuthor;
        private ToolStripButton tsbDonate;
        private ToolStripLabel tslConnection;

        // Filters
        private CardPanel cardFilters;
        private Label lblSolution, lblPublisher, lblTableKind;
        private ComboBox cboSolution, cboPublisher, cboTableKind;
        private CheckBox chkHideSystem, chkHideIsolated, chkOnlyUsed, chkManyToMany, chkSelf;

        // Explore
        private CardPanel cardExplore;
        private Label lblFocus, lblDepth, lblHidden, lblExploreHint;
        private ComboBox cboFocus;
        private NumericUpDown numDepth;
        private CheckBox chkBoundary;
        private ModernButton btnExplore, btnClearFocus, btnBack;
        private LinkLabel lnkShowHidden;

        // Map
        private CardPanel cardMap;
        private Label lblLayout, lblStatus, lblSearchInfo;
        private ComboBox cboLayout;
        private ModernButton btnRelayout, btnFit, btnZoomIn, btnZoomOut;
        private CheckBox chkLabels;
        private TextBox txtSearch;
        private GraphCanvas canvas;
        private ContextMenuStrip cmsNode;
        private ToolStripMenuItem tsmiExplore1, tsmiExplore2, tsmiHide, tsmiCenter, tsmiCopyLogical, tsmiCopySchema;

        // Details
        private CardPanel cardDetails;
        private DataGridView dgvProperties;
        private Label lblCascadeWarning;
        private FlowLayoutPanel flpDetailActions;
        private ModernButton btnFocusHere, btnHideTable, btnGoToTable;
        private SegmentedTabs tabsDetails;
        private Panel pnlDetailPages;
        private DataGridView dgvColumns, dgvRelations, dgvKeys, dgvForms, dgvScripts, dgvSteps;
        private Label lblColumnsInfo;

        private ToolTip toolTip;

        #endregion

        private void BuildLayout()
        {
            SuspendLayout();
            Font = Theme.Base;
            BackColor = Theme.Canvas;
            Size = new Size(1500, 900);
            toolTip = new ToolTip { AutoPopDelay = 15000 };

            BuildHeader();

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 12), BackColor = Theme.Canvas };

            var left = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = Theme.Canvas };
            BuildFilters();
            BuildExplore();
            left.Controls.Add(cardExplore);
            left.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Theme.Canvas });
            left.Controls.Add(cardFilters);

            BuildMap();
            BuildDetails();

            pageModel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };
            pageModel.Controls.Add(cardMap);
            pageModel.Controls.Add(new Splitter { Dock = DockStyle.Right, Width = 10, BackColor = Theme.Canvas, MinExtra = 300, MinSize = 300 });
            pageModel.Controls.Add(cardDetails);
            pageModel.Controls.Add(new Splitter { Dock = DockStyle.Left, Width = 10, BackColor = Theme.Canvas, MinExtra = 300, MinSize = 240 });
            pageModel.Controls.Add(left);

            BuildPluginsPage();
            BuildScriptsPage();

            var pages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };
            pages.Controls.Add(pageModel);
            pages.Controls.Add(pagePlugins);
            pages.Controls.Add(pageScripts);

            var tabsHost = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Canvas, Padding = new Padding(0, 0, 0, 6) };
            tabsMain = new SegmentedTabs { Dock = DockStyle.Fill };
            tabsMain.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Database });
            tabsMain.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Graph });
            tabsMain.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Code });
            tabsHost.Controls.Add(tabsMain);

            body.Controls.Add(pages);
            body.Controls.Add(tabsHost);

            Controls.Add(body);
            Controls.Add(tsHeader);
            ResumeLayout(false);
        }

        #region Header

        private void BuildHeader()
        {
            tsHeader = new ToolStrip
            {
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                AutoSize = false,
                Height = 48,
                Padding = new Padding(10, 6, 10, 6),
                ImageScalingSize = new Size(16, 16),
                BackColor = Color.White,
                CanOverflow = true
            };

            tslLogo = new ToolStripLabel();
            tslSubtitle = new ToolStripLabel();
            tslConnection = new ToolStripLabel();

            tsbClose = new ToolStripButton
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                ImageScaling = ToolStripItemImageScaling.None,
                Margin = new Padding(0, 0, 2, 0),
                Padding = new Padding(6, 0, 6, 0),
                Overflow = ToolStripItemOverflow.Never
            };
            tsbLoad = HeaderButton();
            tsbLoad.Tag = "primary";
            tsbLoad.Font = Theme.Semibold;

            tsddExport = new ToolStripDropDownButton { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ImageScaling = ToolStripItemImageScaling.None, Font = Theme.Base, Padding = new Padding(8, 0, 8, 0), Margin = new Padding(2, 0, 2, 0) };
            tsmiPng = new ToolStripMenuItem();
            tsmiSvg = new ToolStripMenuItem();
            tsmiMermaid = new ToolStripMenuItem();
            tsmiPlantUml = new ToolStripMenuItem();
            tsmiExcel = new ToolStripMenuItem();
            tsmiCopyMermaid = new ToolStripMenuItem();
            tsmiCopyPlantUml = new ToolStripMenuItem();
            tsddExport.DropDownItems.AddRange(new ToolStripItem[]
            {
                tsmiPng, tsmiSvg, new ToolStripSeparator(), tsmiMermaid, tsmiPlantUml, tsmiCopyMermaid, tsmiCopyPlantUml, new ToolStripSeparator(), tsmiExcel
            });

            tsddLanguage = new ToolStripDropDownButton { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ImageScaling = ToolStripItemImageScaling.None, Font = Theme.Base, Padding = new Padding(8, 0, 8, 0), Margin = new Padding(2, 0, 2, 0) };
            tsmiEnglish = new ToolStripMenuItem(Localization.Strings.EnglishName);
            tsmiFrench = new ToolStripMenuItem(Localization.Strings.FrenchName);
            tsddLanguage.DropDownItems.AddRange(new ToolStripItem[] { tsmiEnglish, tsmiFrench });

            tsbAuthor = HeaderButton();
            tsbAuthor.Alignment = ToolStripItemAlignment.Right;
            tsbDonate = HeaderButton();
            tsbDonate.Alignment = ToolStripItemAlignment.Right;

            tsHeader.Items.AddRange(new ToolStripItem[]
            {
                tsbClose, Separator(), tsbLoad, tsddExport, Separator(), tsddLanguage, tsbAuthor, tsbDonate
            });
        }

        private static ToolStripSeparator Separator() => new ToolStripSeparator { Margin = new Padding(8, 0, 8, 0) };

        private static ToolStripButton HeaderButton() => new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ImageScaling = ToolStripItemImageScaling.None,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Font = Theme.Base,
            Margin = new Padding(2, 0, 2, 0),
            Padding = new Padding(8, 0, 8, 0)
        };

        #endregion

        #region Filters / explore

        private void BuildFilters()
        {
            cardFilters = new CardPanel { Dock = DockStyle.Top, Height = 418, Step = 1 };
            var stack = Stack();

            lblSolution = FieldLabel();
            cboSolution = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, MaxDropDownItems = 25, DropDownWidth = 420 };
            lblPublisher = FieldLabel();
            cboPublisher = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, MaxDropDownItems = 25, DropDownWidth = 320 };
            lblTableKind = FieldLabel();
            cboTableKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
            chkHideSystem = Check();
            chkHideIsolated = Check();
            chkOnlyUsed = Check();
            chkManyToMany = Check();
            chkSelf = Check();
            chkHideSystem.Margin = new Padding(0, 12, 0, 0);

            AddRows(stack, lblSolution, cboSolution, lblPublisher, cboPublisher, lblTableKind, cboTableKind, chkHideSystem, chkHideIsolated, chkOnlyUsed, chkManyToMany, chkSelf);
            cardFilters.Controls.Add(stack);
        }

        private void BuildExplore()
        {
            cardExplore = new CardPanel { Dock = DockStyle.Fill, Step = 2 };
            var stack = Stack();

            lblFocus = FieldLabel();
            lblFocus.Margin = new Padding(0, 0, 0, 4);
            cboFocus = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                Dock = DockStyle.Top,
                MaxDropDownItems = 25,
                DropDownWidth = 360
            };

            var depthRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Card, Margin = new Padding(0, 8, 0, 0) };
            lblDepth = FieldLabel();
            lblDepth.Margin = new Padding(0, 6, 8, 0);
            numDepth = new NumericUpDown { Minimum = 1, Maximum = 6, Value = 1, Width = 52, Margin = new Padding(0, 3, 12, 0) };
            btnExplore = new ModernButton { Variant = ButtonVariant.Primary, Glyph = IconKind.Target, AutoSize = true, MinimumSize = new Size(100, 30), Height = 30, Margin = new Padding(0, 0, 0, 0) };
            depthRow.Controls.AddRange(new Control[] { lblDepth, numDepth, btnExplore });

            chkBoundary = Check();
            chkBoundary.Margin = new Padding(0, 10, 0, 0);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Card, Margin = new Padding(0, 10, 0, 0) };
            btnBack = new ModernButton { Variant = ButtonVariant.Secondary, AutoSize = true, MinimumSize = new Size(80, 30), Height = 30, Margin = new Padding(0, 0, 8, 0), Enabled = false };
            btnClearFocus = new ModernButton { Variant = ButtonVariant.Secondary, Glyph = IconKind.Graph, AutoSize = true, MinimumSize = new Size(110, 30), Height = 30, Margin = Padding.Empty };
            buttons.Controls.AddRange(new Control[] { btnBack, btnClearFocus });

            lblHidden = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 14, 0, 0), Visible = false };
            lnkShowHidden = new LinkLabel { AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentPressed, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 2, 0, 0), Visible = false };
            lblExploreHint = new Label { AutoSize = true, MaximumSize = new Size(250, 0), ForeColor = Theme.Subtle, Font = Theme.Small, Margin = new Padding(0, 14, 0, 0) };

            AddRows(stack, lblFocus, cboFocus, depthRow, chkBoundary, buttons, lblHidden, lnkShowHidden, lblExploreHint);
            cardExplore.Controls.Add(stack);
        }

        /// <summary>Vertical stack whose rows take the full width.</summary>
        private static TableLayoutPanel Stack() => new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = Theme.Card,
            AutoScroll = true,
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) }
        };

        private static void AddRows(TableLayoutPanel stack, params Control[] controls)
        {
            stack.RowCount = controls.Length + 1;
            for (var i = 0; i < controls.Length; i++)
            {
                stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                if (controls[i] is ComboBox) controls[i].Dock = DockStyle.Fill;
                stack.Controls.Add(controls[i], 0, i);
            }
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        }

        private static Label FieldLabel() => new Label
        {
            AutoSize = true,
            ForeColor = Theme.Muted,
            Font = Theme.Semibold,
            Margin = new Padding(0, 8, 0, 4)
        };

        private static CheckBox Check() => new CheckBox
        {
            AutoSize = true,
            ForeColor = Theme.Text,
            Margin = new Padding(0, 6, 0, 0)
        };

        #endregion

        #region Map

        private void BuildMap()
        {
            cardMap = new CardPanel { Dock = DockStyle.Fill };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Card };
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Card };
            lblLayout = FieldLabel();
            lblLayout.Margin = new Padding(0, 9, 6, 0);
            cboLayout = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Margin = new Padding(0, 5, 8, 0) };
            btnRelayout = ToolButton(IconKind.Layout);
            btnFit = ToolButton(IconKind.Fit);
            btnFit.MinimumSize = new Size(32, 30);
            btnFit.Width = 32;
            btnZoomIn = ToolButton(IconKind.ZoomIn);
            btnZoomIn.MinimumSize = new Size(32, 30);
            btnZoomIn.Width = 32;
            btnZoomOut = ToolButton(IconKind.ZoomOut);
            btnZoomOut.MinimumSize = new Size(32, 30);
            btnZoomOut.Width = 32;
            chkLabels = new CheckBox { AutoSize = true, Margin = new Padding(6, 9, 14, 0) };
            tools.Controls.AddRange(new Control[] { lblLayout, cboLayout, btnRelayout, btnFit, btnZoomIn, btnZoomOut, chkLabels });

            // Search box pinned to the right of the toolbar, whatever the width of the map.
            var searchHost = new Panel { Dock = DockStyle.Right, Width = 170, BackColor = Theme.Card, Padding = new Padding(0, 7, 0, 0) };
            txtSearch = new TextBox { Dock = DockStyle.Top };
            searchHost.Controls.Add(txtSearch);
            toolbar.Controls.Add(tools);
            toolbar.Controls.Add(searchHost);

            var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Theme.Card };
            lblStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, Font = Theme.Small, BackColor = Theme.Card, AutoEllipsis = true };
            lblSearchInfo = new Label { Dock = DockStyle.Right, AutoSize = true, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.SearchHit, Font = Theme.Semibold, BackColor = Theme.Card, Padding = new Padding(0, 6, 0, 0) };
            statusBar.Controls.Add(lblStatus);
            statusBar.Controls.Add(lblSearchInfo);

            canvas = new GraphCanvas { Dock = DockStyle.Fill };
            var canvasHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0), BackColor = Theme.Card };
            canvasHost.Controls.Add(canvas);

            cardMap.Controls.Add(canvasHost);
            cardMap.Controls.Add(statusBar);
            cardMap.Controls.Add(toolbar);

            cmsNode = new ContextMenuStrip { ShowImageMargin = true };
            tsmiExplore1 = new ToolStripMenuItem();
            tsmiExplore2 = new ToolStripMenuItem();
            tsmiCenter = new ToolStripMenuItem();
            tsmiHide = new ToolStripMenuItem();
            tsmiCopyLogical = new ToolStripMenuItem();
            tsmiCopySchema = new ToolStripMenuItem();
            cmsNode.Items.AddRange(new ToolStripItem[]
            {
                tsmiExplore1, tsmiExplore2, tsmiCenter, new ToolStripSeparator(), tsmiHide, new ToolStripSeparator(), tsmiCopyLogical, tsmiCopySchema
            });
        }

        private static ModernButton ToolButton(IconKind glyph) => new ModernButton
        {
            Variant = ButtonVariant.Secondary,
            Glyph = glyph,
            AutoSize = true,
            MinimumSize = new Size(40, 30),
            Height = 30,
            Margin = new Padding(0, 4, 6, 0)
        };

        #endregion

        #region Details

        private void BuildDetails()
        {
            cardDetails = new CardPanel { Dock = DockStyle.Right, Width = 440 };

            dgvProperties = new DataGridView
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
            Theme.StyleGrid(dgvProperties, false);
            dgvProperties.RowTemplate.Height = 26;
            dgvProperties.Columns.Add(new DataGridViewTextBoxColumn { Name = "colKey", FillWeight = 38, DefaultCellStyle = { ForeColor = Theme.Muted, Font = Theme.Semibold } });
            dgvProperties.Columns.Add(new DataGridViewTextBoxColumn { Name = "colValue", FillWeight = 62 });

            lblCascadeWarning = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Font = Theme.Semibold,
                Visible = false
            };

            flpDetailActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, WrapContents = false, BackColor = Theme.Card, Padding = new Padding(0, 6, 0, 0) };
            btnFocusHere = new ModernButton { Variant = ButtonVariant.Primary, Glyph = IconKind.Target, AutoSize = true, MinimumSize = new Size(100, 30), Height = 30, Margin = new Padding(0, 0, 8, 0) };
            btnGoToTable = new ModernButton { Variant = ButtonVariant.Secondary, Glyph = IconKind.Search, AutoSize = true, MinimumSize = new Size(100, 30), Height = 30, Margin = new Padding(0, 0, 8, 0) };
            btnHideTable = new ModernButton { Variant = ButtonVariant.Ghost, Glyph = IconKind.Hide, AutoSize = true, MinimumSize = new Size(90, 30), Height = 30, Margin = Padding.Empty };
            flpDetailActions.Controls.AddRange(new Control[] { btnFocusHere, btnGoToTable, btnHideTable });

            tabsDetails = new SegmentedTabs { Dock = DockStyle.Top, Height = 76, Wrap = true };
            tabsDetails.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Fields, BadgeColor = Theme.Accent });
            tabsDetails.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Link, BadgeColor = Theme.Accent });
            tabsDetails.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Check, BadgeColor = Theme.Accent });
            tabsDetails.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Layout, BadgeColor = Theme.Accent });
            tabsDetails.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Log, BadgeColor = Theme.Accent });
            tabsDetails.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Play, BadgeColor = UI.Rendering.GraphRenderer.PluginNode });

            pnlDetailPages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
            dgvColumns = DetailGrid();
            dgvColumns.Columns.Add(new DataGridViewTextBoxColumn { Name = "colColDisplay", FillWeight = 34 });
            dgvColumns.Columns.Add(new DataGridViewTextBoxColumn { Name = "colColLogical", FillWeight = 32 });
            dgvColumns.Columns.Add(new DataGridViewTextBoxColumn { Name = "colColType", FillWeight = 20 });
            dgvColumns.Columns.Add(new DataGridViewTextBoxColumn { Name = "colColRequired", FillWeight = 16 });
            dgvColumns.Columns.Add(new DataGridViewTextBoxColumn { Name = "colColCustom", FillWeight = 12 });

            dgvRelations = DetailGrid();
            dgvRelations.Columns.Add(new DataGridViewTextBoxColumn { Name = "colRelKind", FillWeight = 10 });
            dgvRelations.Columns.Add(new DataGridViewTextBoxColumn { Name = "colRelRelated", FillWeight = 28 });
            dgvRelations.Columns.Add(new DataGridViewTextBoxColumn { Name = "colRelVia", FillWeight = 30 });
            dgvRelations.Columns.Add(new DataGridViewTextBoxColumn { Name = "colRelDelete", FillWeight = 16 });

            dgvKeys = DetailGrid();
            dgvKeys.Columns.Add(new DataGridViewTextBoxColumn { Name = "colKeyName", FillWeight = 34 });
            dgvKeys.Columns.Add(new DataGridViewTextBoxColumn { Name = "colKeyColumns", FillWeight = 44 });
            dgvKeys.Columns.Add(new DataGridViewTextBoxColumn { Name = "colKeyStatus", FillWeight = 22 });

            dgvForms = DetailGrid();
            AddColumns(dgvForms, ("colFormName", 34), ("colFormType", 20), ("colFormState", 14), ("colFormLibraries", 12), ("colFormHandlers", 12));
            dgvScripts = DetailGrid();
            AddColumns(dgvScripts, ("colScriptLibrary", 30), ("colScriptEvent", 22), ("colScriptFunction", 28), ("colScriptForm", 20));
            dgvSteps = DetailGrid();
            AddColumns(dgvSteps, ("colStepMessage", 14), ("colStepStage", 16), ("colStepMode", 10), ("colStepRank", 7), ("colStepPlugin", 30), ("colStepFilter", 18), ("colStepState", 10));

            lblColumnsInfo = new Label { Dock = DockStyle.Top, Height = 26, ForeColor = Theme.Muted, Font = Theme.Small, TextAlign = ContentAlignment.MiddleLeft, Visible = false };
            pnlDetailPages.Controls.AddRange(new Control[] { dgvColumns, dgvRelations, dgvKeys, dgvForms, dgvScripts, dgvSteps, lblColumnsInfo });

            cardDetails.Controls.Add(pnlDetailPages);
            cardDetails.Controls.Add(tabsDetails);
            cardDetails.Controls.Add(flpDetailActions);
            cardDetails.Controls.Add(lblCascadeWarning);
            cardDetails.Controls.Add(dgvProperties);
        }

        private static DataGridView DetailGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Visible = false
            };
            Theme.StyleGrid(grid, true);
            grid.RowTemplate.Height = 26;
            return grid;
        }

        #endregion
    }
}
