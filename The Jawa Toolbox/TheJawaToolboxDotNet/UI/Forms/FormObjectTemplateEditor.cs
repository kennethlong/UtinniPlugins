/**
 * MIT License
 *
 * Copyright (c) 2020 Philip Klatt
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
**/
// Object-template editor host (Phase 11, CF-06) — a resizable UtinniForm cloned in shape from
// FormDatatableEditor (Phase 9). Renders the D-01 effective-inheritance view (Field · Effective
// value · Origin · Type) over a ThemedDataGridView with the ancestor breadcrumb header and origin
// overlays, wires the editor-local ObjectTemplateEditController + undo/redo, and exposes the two
// hand-off entry points (TRE Browser + IFF Editor). Object-template layout/inheritance semantics were
// studied from swg-client-v2 only (no code, comments, identifier names, or test fixtures copied from
// any reference source). Plan 03 delivers the READ/NAVIGATE surface; value editing, mutations, save,
// and the reload badge land in Plan 04.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using TJT.Saving;
using TJT.UI.Controls;
using UtinniCore.Utinni;
using UtinniCoreDotNet.Editing;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.Formats.ObjectTemplate;
using UtinniCoreDotNet.Formats.Tre;
using UtinniCoreDotNet.PluginFramework;
using UtinniCoreDotNet.Saving;
using UtinniCoreDotNet.UI;
using UtinniCoreDotNet.UI.Controls;
using UtinniCoreDotNet.UI.Forms;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Forms
{
    /// <summary>
    /// Typed object-template editor window — a resizable <see cref="UtinniForm"/> registered ONCE via
    /// the plugin's <c>GetForms()</c> list (the fifth and final V1 SubPanel; Wave-1 editors are forms,
    /// not the fixed 417px SubPanel). Hosts a <see cref="ThemedDataGridView"/> bound to the D-01
    /// effective-inheritance view of a <see cref="MutableObjectTemplate"/> with origin overlays + an
    /// ancestor breadcrumb. Clones the Phase 9 <c>FormDatatableEditor</c> shape with the object-template
    /// effective-view layer swapped in.
    ///
    /// <para><b>Plan 11-03 staging:</b> this plan ships the host + effective-view grid + breadcrumb +
    /// origin overlays + undo/redo + the Show-inherited toggle + the two hand-off entry points. The
    /// Save▾ items, Promote/Revert mutation bodies, and the reload-badge action land in Plan 11-04 —
    /// in THIS plan those toolbar controls exist but their click bodies are status-only stubs. The grid
    /// is read-only as a surface (value editing is wired Plan 11-04).</para>
    ///
    /// <para><b>Singleton hide-not-dispose:</b> <see cref="FormObjectTemplateEditor_FormClosing"/>
    /// delegates its CloseReason decision to
    /// <see cref="SingletonFormClosePolicy.ShouldHideInsteadOfDispose"/> — the Phase-8-smoke-locked
    /// pattern applied from commit 1 (mandatory for MEF-registered editor forms).</para>
    /// </summary>
    public partial class FormObjectTemplateEditor : UtinniForm, IEditorForm
    {
        private const string SettingsSection = "ObjectTemplateEditor";
        private const string BaseTitle = "Object Template Editor";

        private readonly IEditorPlugin editorPlugin;
        private readonly UtINI ini;
        private readonly ToolTip toolTip = new ToolTip();

        // The typed mutable model + its resolved effective view. Null until LoadDocument binds a document.
        private MutableObjectTemplate otDocument;
        private EffectiveTemplateView effectiveView;

        // Editor-local undo/redo controller (Plan 11-02 framework type). Null until LoadDocument binds.
        private ObjectTemplateEditController controller;

        // Friendly display name for the loaded document (Save As… default name in Plan 11-04).
        private string displayName;

        // Lazily-built, cached TRE archive index for DERV base-chain resolution. Built best-effort from
        // the client root; null when no client root is resolvable (then bases degrade to unresolved).
        private TreArchiveIndex archiveIndex;
        private bool archiveIndexAttempted;

        // View-only inherited-row visibility (default ON, persisted). When OFF the grid hides inherited
        // rows (view-only; the model + resolved chain are unchanged).
        private bool showInheritedRows = true;

        // Save▾ drop-down state (modes 1/2/4 via ObjectTemplateSaveTargets; mode 3 disabled CF-03).
        private UtinniContextMenuStrip saveMenu;
        private ToolStripMenuItem miSaveInPlace;
        private ToolStripMenuItem miSaveLooseOverride;
        private ToolStripMenuItem miSaveAs;
        private ToolStripMenuItem miPatchLive;
        private ToolStripMenuItem miRepackTre;
        private bool saveInFlight;
        private string lastSavedPath;

        // Grid context menu (promote / revert / edit raw bytes).
        private UtinniContextMenuStrip gridContextMenu;
        private ToolStripMenuItem miPromote;
        private ToolStripMenuItem miRevert;
        private ToolStripMenuItem miEditRawBytes;

        /// <summary>
        /// Provenance descriptor for the currently loaded document. The open paths set this to gate the
        /// Save▾ modes (Plan 11-04); defaults to <see cref="OpenSource.Unknown"/>.
        /// </summary>
        public OpenSource Source { get; set; }

        public FormObjectTemplateEditor(IEditorPlugin editorPlugin)
        {
            InitializeComponent();

            // Phase 6 (06-05): TJT owns its brand icon; load from the plugin dir, guarded.
            string tjtIconPath = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "Resources", "TJT.ico");
            if (File.Exists(tjtIconPath))
            {
                this.Icon = new Icon(tjtIconPath);
            }

            this.editorPlugin = editorPlugin;
            ini = editorPlugin.GetConfig();

            CreateSettings();
            ini.Load();

            Width = ini.GetInt(SettingsSection, "width");
            Height = ini.GetInt(SettingsSection, "height");

            // Theme via Colors.*() accessors only — no raw ARGB literals. Color.Red is the sole allowed
            // raw literal (unresolved-base + failure emphasis), applied at runtime in the formatters.
            toolbar.BackColor = Colors.Primary();
            sep1.BackColor = Colors.Primary();
            sep2.BackColor = Colors.Primary();
            sep3.BackColor = Colors.Primary();
            sep4.BackColor = Colors.Primary();
            pnlBreadcrumb.BackColor = Colors.Primary();
            pnlFindReplace.BackColor = Colors.Primary();
            pnlStatus.BackColor = Colors.Primary();
            pnlCounters.BackColor = Colors.Primary();
            lblBreadcrumbLead.ForeColor = Colors.FontDisabled();
            lblBreadcrumb.ForeColor = Colors.Font();
            lblDirty.ForeColor = Colors.Secondary(); // accent for the unsaved-changes marker
            lblStatus.ForeColor = Colors.Font();

            // Default provenance is Unknown (W-3 contract). Open paths override.
            Source = OpenSource.Unknown.Instance;

            // Toolbar tooltips (R-02 glyph-button tooltips on the Find pane).
            toolTip.SetToolTip(btnOpen, "Open an object template (choose a file).");
            toolTip.SetToolTip(btnSave, "Save the current object template (choose a target).");
            toolTip.SetToolTip(btnUndo, "Undo (Ctrl+Z)");
            toolTip.SetToolTip(btnRedo, "Redo (Ctrl+Y)");
            toolTip.SetToolTip(btnPromote, "Promote the selected inherited field to a local override.");
            toolTip.SetToolTip(btnRevert, "Revert the selected local override back to inherited.");
            toolTip.SetToolTip(btnFind, "Find (Ctrl+F)");
            toolTip.SetToolTip(btnReplace, "Replace (Ctrl+H)");
            toolTip.SetToolTip(tglShowInherited, "Show inherited fields. Toggle off to show local overrides only.");
            toolTip.SetToolTip(btnReload, "State how/when the client re-resolves the edit (it never auto-triggers a reload).");
            toolTip.SetToolTip(btnFindPrev, "Find previous (Shift+F3)");
            toolTip.SetToolTip(btnFindNext, "Find next (F3)");
            toolTip.SetToolTip(btnFindClose, "Close (Esc)");

            // Open… is enabled from the start; the rest become enabled on LoadDocument
            // (Undo / Redo / Find / Replace / Show inherited / Save / Promote / Revert / Reload).
            btnOpen.Click += OnOpenClicked;
            btnUndo.Click += OnUndoClicked;
            btnRedo.Click += OnRedoClicked;
            tglShowInherited.CheckedChanged += OnShowInheritedToggled;

            // Plan 11-04: the mutation + save + reload bodies are wired here (no longer status stubs).
            btnSave.Click += OnSaveButtonClick;
            btnPromote.Click += OnPromoteClicked;
            btnRevert.Click += OnRevertClicked;
            btnReload.Click += OnReloadClicked;

            // Save▾ drop-down (modes 1/2/4 via the ObjectTemplateSaveTargets shim; mode 3 disabled CF-03).
            BuildSaveMenu();

            // Configure the four fixed effective-view columns + the cell-state overlay formatter.
            ConfigureGridColumns();
            gridSurface.CellFormatting += OnGridCellFormatting;

            // Per-type value editing (D-02) + the D-04 commit seam (promote-on-inherited-edit).
            gridSurface.CellEndEdit += OnCellEndEdit;
            gridSurface.CellValueChanged += OnCellValueChanged;
            gridSurface.EditingControlShowing += OnEditingControlShowing;
            gridSurface.CellBeginEdit += OnCellBeginEdit;
            gridSurface.CellMouseDown += OnCellMouseDown;
            gridSurface.CellDoubleClick += OnCellDoubleClick;
            gridSurface.SelectionChanged += OnGridSelectionChanged;

            // Lazily-built grid context menu (promote / revert / edit raw bytes).
            BuildGridContextMenu();

            SetTitle(null);
            UpdateDirtyVisuals();
            UpdateCounters();
            RefreshMutationButtonsState();
        }

        // ── IEditorForm ──────────────────────────────────────────────────────

        public string GetName() { return this.Text; }

        public Form Create(IEditorPlugin plugin, List<Form> parentChildren)
        {
            foreach (Form form in parentChildren)
            {
                if (form.GetType() == typeof(FormObjectTemplateEditor))
                {
                    form.Activate();
                    return null;
                }
            }
            FormObjectTemplateEditor newForm = new FormObjectTemplateEditor(plugin);
            newForm.Show();
            parentChildren.Add(newForm);
            return newForm;
        }

        // ── Settings ─────────────────────────────────────────────────────────

        private void CreateSettings()
        {
            ini.AddSetting(SettingsSection, "width", "1200", UtINI.Value.Types.VtInt);
            ini.AddSetting(SettingsSection, "height", "760", UtINI.Value.Types.VtInt);
            ini.AddSetting(SettingsSection, "findReplaceVisible", "0", UtINI.Value.Types.VtBool);
            ini.AddSetting(SettingsSection, "showInheritedRows", "1", UtINI.Value.Types.VtBool);
            ini.AddSetting(SettingsSection, "looseOverrideDir", "", UtINI.Value.Types.VtString);
        }

        // ── Grid column configuration (4 fixed columns: Field · Effective value · Origin · Type) ──
        //
        // UI-SPEC § Columns + Phase 11 deviations vs the Phase 9 token map:
        //   AllowUserToOrderColumns = false (the column set is fixed); MultiSelect = false /
        //   SelectionMode = FullRowSelect (the unit of action is a FIELD row); only Effective value is
        //   AutoSizeMode = Fill. The grid is a ThemedDataGridView so the dark token map is inherited;
        //   this method binds OWN rows from EffectiveField (NOT BindMutable, which is datatable-typed).

        private const int ColField = 0;
        private const int ColValue = 1;
        private const int ColOrigin = 2;
        private const int ColType = 3;

        private void ConfigureGridColumns()
        {
            gridSurface.AllowUserToOrderColumns = false;
            gridSurface.AllowUserToResizeColumns = true;
            gridSurface.MultiSelect = false;
            gridSurface.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            // The grid is NOT globally read-only (Plan 11-04 wires value editing). Editability is
            // governed per-cell: only the Effective value cell of a typed-scalar local-or-inherited row
            // is editable; complex/raw and unresolved cells stay ReadOnly (set in BindEffectiveView).
            gridSurface.ReadOnly = false;

            var colField = new DataGridViewTextBoxColumn
            {
                Name = "Field",
                HeaderText = "Field",
                ReadOnly = true,
                FillWeight = 25,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
            var colValue = new DataGridViewTextBoxColumn
            {
                Name = "EffectiveValue",
                HeaderText = "Effective value",
                ReadOnly = false, // per-cell ReadOnly is set in BindEffectiveView by decoded type.
                FillWeight = 50,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
            var colOrigin = new DataGridViewTextBoxColumn
            {
                Name = "Origin",
                HeaderText = "Origin",
                ReadOnly = true,
                FillWeight = 18,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
            var colType = new DataGridViewTextBoxColumn
            {
                Name = "Type",
                HeaderText = "Type",
                ReadOnly = true,
                FillWeight = 7,
                SortMode = DataGridViewColumnSortMode.Automatic
            };

            gridSurface.Columns.AddRange(new DataGridViewColumn[] { colField, colValue, colOrigin, colType });

            // View-only sort: the param-chunk order on disk is preserved by the writer regardless of the
            // visual sort. The LOCKED hover tooltip states this.
            for (int i = 0; i < gridSurface.Columns.Count; i++)
            {
                gridSurface.Columns[i].ToolTipText = "View order only — save preserves the on-disk param order.";
            }
        }

        // ── Load ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Binds a typed <see cref="MutableObjectTemplate"/> to the effective-view grid. Runs the D-01
        /// DERV-chain resolve on a background <see cref="Task"/> (unresolved bases degrade gracefully —
        /// the open never blocks) and marshals back to bind the effective rows + render the breadcrumb.
        /// </summary>
        public void LoadDocument(MutableObjectTemplate doc, OpenSource source, string displayName)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            this.otDocument = doc;
            this.Source = source ?? OpenSource.Unknown.Instance;
            this.displayName = displayName;

            // Editor-local undo/redo controller (CON-M-05: no scene undo/redo-manager coupling).
            if (controller != null) controller.EditApplied -= OnEditApplied;
            controller = new ObjectTemplateEditController(doc);
            controller.EditApplied += OnEditApplied;

            // Loading state.
            lblEmptyState.Visible = false;
            lblStatus.Text = "Resolving inheritance chain…";
            lblStatus.ForeColor = Colors.Font();

            // Resolve the DERV chain off-UI-thread, then marshal back to bind. Capture the locals the
            // background task needs; the resolver never throws on the open path (D-01 LOCKED).
            MutableObjectTemplate captured = doc;
            Task.Run(() =>
            {
                EffectiveTemplateView view = ResolveEffectiveView(captured);
                if (!IsHandleCreated) return;
                BeginInvoke((Action)(() =>
                {
                    effectiveView = view;
                    BindEffectiveView();
                    RenderBreadcrumb();

                    pnlBreadcrumb.Visible = true;
                    // Reload badge visibility is governed by RefreshReloadButtonState (hidden when no
                    // live client per CF-05); set the LOCKED text here regardless.
                    lblReloadBadge.Text = "Reloads on next scene change (relog to guarantee).";

                    EnableDocumentControls();
                    UpdateUndoRedoState();
                    UpdateDirtyVisuals();
                    UpdateCounters();

                    if (HasUnresolvedBase(view))
                    {
                        string unresolvedName = FirstUnresolvedBaseName(view);
                        lblStatus.Text = (unresolvedName ?? "A base") +
                            " not found in loaded archives — showing local fields only for its branch.";
                        lblStatus.ForeColor = Color.Red;
                    }
                    else
                    {
                        lblStatus.Text = "Opened " + (displayName ?? "object template");
                        lblStatus.ForeColor = Colors.Font();
                    }
                }));
            });
        }

        // Resolves the effective view, wiring the resolver's base-locator to the lazily-built TRE archive
        // index when one is available. When no client root / archive index can be built, resolution uses
        // a null-returning locator so the open still succeeds with inherited bases marked unresolved
        // (D-01 LOCKED graceful degradation — the open NEVER blocks).
        private EffectiveTemplateView ResolveEffectiveView(MutableObjectTemplate doc)
        {
            try
            {
                TreArchiveIndex index = EnsureArchiveIndex();
                if (index != null)
                {
                    return ObjectTemplateResolver.ResolveViaArchive(doc, index);
                }
                // No archive index: resolve with a locator that always degrades (returns null). Local
                // params stay present + the breadcrumb records the base as unresolved.
                return ObjectTemplateResolver.Resolve(doc, _ => null);
            }
            catch (Exception)
            {
                // Last-resort defense: a resolve failure must never block the open. Degrade fully.
                return ObjectTemplateResolver.Resolve(doc, _ => null);
            }
        }

        // Lazily build + cache the TRE archive index from the resolved client root (best-effort). A
        // failure leaves archiveIndex null and we fall back to graceful degradation. Built ONCE.
        private TreArchiveIndex EnsureArchiveIndex()
        {
            if (archiveIndexAttempted) return archiveIndex;
            archiveIndexAttempted = true;
            try
            {
                string clientRoot = ResolveClientRoot();
                if (!string.IsNullOrEmpty(clientRoot))
                {
                    archiveIndex = TreArchiveIndex.Build(clientRoot);
                }
            }
            catch
            {
                archiveIndex = null; // degrade — never block the open.
            }
            return archiveIndex;
        }

        // ── Grid bind (own rows from EffectiveField) ─────────────────────────

        private void BindEffectiveView()
        {
            gridSurface.SuspendLayout();
            try
            {
                gridSurface.Rows.Clear();
                if (effectiveView == null) return;

                foreach (EffectiveField field in effectiveView.Fields)
                {
                    int rowIndex = gridSurface.Rows.Add();
                    DataGridViewRow gridRow = gridSurface.Rows[rowIndex];
                    gridRow.Tag = field; // origin overlays + selection read the field off the Tag.

                    gridRow.Cells[ColField].Value = field.FieldName;
                    gridRow.Cells[ColOrigin].Value = FormatOrigin(field);
                    gridRow.Cells[ColType].Value = FormatType(field);

                    // Per-type Effective-value cell. bool swaps to a checkbox cell; int/float/string stay
                    // text cells edited via the EditingControlShowing widget swap; complex/raw and
                    // unresolved cells are read-only (edited via the hex sub-editor or not at all).
                    bool isBool = field.EffectiveValue != null
                        && field.EffectiveValue.Kind == ObjectTemplateParamKind.Bool;
                    if (isBool)
                    {
                        var checkCell = new DataGridViewCheckBoxCell { ThreeState = false };
                        gridRow.Cells[ColValue] = checkCell;
                        checkCell.Value = field.EffectiveValue.BoolValue;
                        checkCell.ReadOnly = false;
                    }
                    else
                    {
                        bool isNumeric = field.EffectiveValue != null
                            && (field.EffectiveValue.Kind == ObjectTemplateParamKind.Int
                                || field.EffectiveValue.Kind == ObjectTemplateParamKind.Float);
                        if (isNumeric)
                        {
                            // Swap in the numeric cell so EditingControlShowing tunes the UtinniNumericUpDown.
                            gridRow.Cells[ColValue] = new ObjectTemplateNumericCell();
                        }
                        else if (!(gridRow.Cells[ColValue] is DataGridViewTextBoxCell))
                        {
                            // Restore a plain text cell if a previous bind left a checkbox/numeric cell here.
                            gridRow.Cells[ColValue] = new DataGridViewTextBoxCell();
                        }
                        gridRow.Cells[ColValue].Value = FormatEffectiveValue(field);
                        gridRow.Cells[ColValue].ReadOnly = !IsInlineEditable(field);
                    }

                    // Show-inherited toggle (view-only): hide inherited rows when OFF.
                    if (!showInheritedRows && field.Origin == EffectiveFieldOriginKind.Inherited)
                    {
                        gridRow.Visible = false;
                    }
                }
            }
            finally
            {
                gridSurface.ResumeLayout();
            }
        }

        // Re-applies the Show-inherited row visibility without rebuilding the grid (view-only).
        private void ApplyShowInheritedFilter()
        {
            foreach (DataGridViewRow gridRow in gridSurface.Rows)
            {
                EffectiveField field = gridRow.Tag as EffectiveField;
                if (field == null) continue;
                bool hide = !showInheritedRows && field.Origin == EffectiveFieldOriginKind.Inherited;
                gridRow.Visible = !hide;
            }
        }

        private static string FormatEffectiveValue(EffectiveField field)
        {
            if (field.Origin == EffectiveFieldOriginKind.UnresolvedBase || field.EffectiveValue == null)
            {
                return "(unresolved)";
            }
            return DescribeValue(field.EffectiveValue);
        }

        // Renders a typed value for the read surface. Complex/raw params show their value bytes as
        // lowercase hex; scalars show their typed form. (Inline editing widgets land in Plan 11-04.)
        private static string DescribeValue(ObjectTemplateParamValue value)
        {
            switch (value.Kind)
            {
                case ObjectTemplateParamKind.Bool:
                    return value.BoolValue ? "true" : "false";
                case ObjectTemplateParamKind.Int:
                    return DeltaPrefix(value.DeltaType) + value.IntValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case ObjectTemplateParamKind.Float:
                    return DeltaPrefix(value.DeltaType) + value.FloatValue.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
                case ObjectTemplateParamKind.String:
                    return value.StringValue;
                case ObjectTemplateParamKind.None:
                    return "(none)";
                case ObjectTemplateParamKind.RawBytesHexFallback:
                    return BytesToHex(value.GetRawBytesCopy());
                default:
                    return "";
            }
        }

        // The verbatim '+'/'-' delta byte prefixes a delta-on-base numeric; ' ' (absolute) shows nothing.
        private static string DeltaPrefix(byte? deltaType)
        {
            if (!deltaType.HasValue) return "";
            char c = (char)deltaType.Value;
            return (c == '+' || c == '-') ? c.ToString() : "";
        }

        private static string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            var sb = new System.Text.StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static string FormatOrigin(EffectiveField field)
        {
            switch (field.Origin)
            {
                case EffectiveFieldOriginKind.LocalOverride:
                    return "local override";
                case EffectiveFieldOriginKind.Inherited:
                    return "inherited from " + (field.OriginAncestorName ?? "?");
                case EffectiveFieldOriginKind.UnresolvedBase:
                    return "unresolved base " + (field.OriginAncestorName ?? "?");
                default:
                    return "";
            }
        }

        private static string FormatType(EffectiveField field)
        {
            if (field.Origin == EffectiveFieldOriginKind.UnresolvedBase || field.EffectiveValue == null)
            {
                return "(unknown)";
            }
            return field.EffectiveValue.ParamTypeLabel;
        }

        // ── Cell-state visual overlays (UI-SPEC § Cell-state visual overlays) ──
        //
        // local-override Origin cell at Colors.Secondary(); inherited rows (Field+Value+Type) at
        // Colors.FontDisabled() + italic; unresolved-base Origin + value at Color.Red. The Show-inherited
        // toggle hides inherited rows view-only (handled in BindEffectiveView / ApplyShowInheritedFilter).
        private void OnGridCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= gridSurface.Rows.Count) return;
            EffectiveField field = gridSurface.Rows[e.RowIndex].Tag as EffectiveField;
            if (field == null) return;

            switch (field.Origin)
            {
                case EffectiveFieldOriginKind.LocalOverride:
                    if (e.ColumnIndex == ColOrigin)
                    {
                        e.CellStyle.ForeColor = Colors.Secondary();
                    }
                    break;

                case EffectiveFieldOriginKind.Inherited:
                    // Greyed + italic on Field / Value / Type; the Origin cell stays dimmed (not accent).
                    if (e.ColumnIndex == ColField || e.ColumnIndex == ColValue
                        || e.ColumnIndex == ColType || e.ColumnIndex == ColOrigin)
                    {
                        e.CellStyle.ForeColor = Colors.FontDisabled();
                        e.CellStyle.Font = new Font(gridSurface.Font, FontStyle.Italic);
                    }
                    break;

                case EffectiveFieldOriginKind.UnresolvedBase:
                    // Origin + value in red; the field is unresolved (cannot promote a value we never got).
                    if (e.ColumnIndex == ColOrigin || e.ColumnIndex == ColValue || e.ColumnIndex == ColType)
                    {
                        e.CellStyle.ForeColor = Color.Red;
                    }
                    break;
            }
        }

        // ── Breadcrumb header (root → … → this) ──────────────────────────────
        //
        // Rendering: the terminal `this` segment at Colors.Secondary() (accent); resolved ancestors at
        // Colors.Font(); unresolved segments at Color.Red with `(unresolved)`. Because UtinniLabel is a
        // single-color label, the breadcrumb text is assembled as plain text and the label foreground
        // reflects the WORST state (red when any segment is unresolved, accent otherwise) so the
        // unresolved-vs-resolved distinction is never color-alone-only (the `(unresolved)` text carries it).
        private void RenderBreadcrumb()
        {
            if (effectiveView == null)
            {
                lblBreadcrumb.Text = "this";
                lblBreadcrumb.ForeColor = Colors.Secondary();
                return;
            }

            var segments = new List<string>();
            bool anyUnresolved = false;
            foreach (BreadcrumbSegment seg in effectiveView.Breadcrumb)
            {
                if (seg.IsOpenTemplate)
                {
                    segments.Add("this");
                }
                else if (seg.Resolved)
                {
                    segments.Add(seg.Name ?? "?");
                }
                else
                {
                    segments.Add((seg.Name ?? "?") + " → (unresolved)");
                    anyUnresolved = true;
                }
            }

            // The resolver emits root→this as [this, base1, base2, …]; render root→…→this for the user.
            segments.Reverse();
            lblBreadcrumb.Text = segments.Count > 0 ? string.Join(" → ", segments) : "this";
            lblBreadcrumb.ForeColor = anyUnresolved ? Color.Red : Colors.Secondary();
        }

        private static bool HasUnresolvedBase(EffectiveTemplateView view)
        {
            if (view == null) return false;
            foreach (BreadcrumbSegment seg in view.Breadcrumb)
            {
                if (!seg.IsOpenTemplate && !seg.Resolved) return true;
            }
            return false;
        }

        private static string FirstUnresolvedBaseName(EffectiveTemplateView view)
        {
            if (view == null) return null;
            foreach (BreadcrumbSegment seg in view.Breadcrumb)
            {
                if (!seg.IsOpenTemplate && !seg.Resolved) return seg.Name;
            }
            return null;
        }

        // ── Controller event wiring (Plan 11-02 controller) ──────────────────

        private void OnEditApplied(object sender, EventArgs e)
        {
            // Re-resolve + re-bind so promote/revert/edit reflect in the effective view (Plan 11-04 drives
            // the mutations; in Plan 11-03 the controller is wired but only undo/redo of nothing occurs).
            if (otDocument != null)
            {
                effectiveView = ResolveEffectiveView(otDocument);
                BindEffectiveView();
                RenderBreadcrumb();
            }
            UpdateUndoRedoState();
            UpdateDirtyVisuals();
            UpdateCounters();
            RefreshSaveMenuEnabledState();
            RefreshMutationButtonsState();
        }

        private void EnableDocumentControls()
        {
            btnFind.Enabled = true;
            btnReplace.Enabled = true;
            tglShowInherited.Enabled = true;
            RefreshSaveMenuEnabledState();
            RefreshReloadButtonState();
            RefreshMutationButtonsState();
        }

        private void UpdateUndoRedoState()
        {
            btnUndo.Enabled = controller != null && controller.CanUndo;
            btnRedo.Enabled = controller != null && controller.CanRedo;
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            if (controller != null && controller.CanUndo) controller.Undo();
        }

        private void OnRedoClicked(object sender, EventArgs e)
        {
            if (controller != null && controller.CanRedo) controller.Redo();
        }

        private void OnShowInheritedToggled(object sender, EventArgs e)
        {
            showInheritedRows = tglShowInherited.Checked;
            ini.AddSetting(SettingsSection, "showInheritedRows", showInheritedRows ? "1" : "0", UtINI.Value.Types.VtBool);
            try { ini.Save(); } catch { /* persistence best-effort */ }
            ApplyShowInheritedFilter();
        }

        // ── Per-type value editing (D-02) + the D-04 commit seam ─────────────────
        //
        // The Effective-value cell is the one editable surface. bool → checkbox cell; int/float →
        // UtinniNumericUpDown via EditingControlShowing; string → free text. Complex/raw/unresolved
        // cells are ReadOnly (edited via the hex sub-editor or not at all). On a committed scalar edit,
        // the commit BRANCHES on origin: a LocalOverride row routes to controller.Apply(EditValue(...));
        // an Inherited row routes to controller.Apply(AddOverride(...)) — editing an inherited value
        // PROMOTES it to a local override (D-04). All mutations go through the controller → undoable.

        private void OnEditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            EffectiveField field = SelectedField();
            if (field == null || field.EffectiveValue == null) return;

            var numeric = e.Control as ObjectTemplateNumericUpDownEditingControl;
            if (numeric == null) return;

            if (field.EffectiveValue.Kind == ObjectTemplateParamKind.Int)
            {
                numeric.DecimalPlaces = 0;
                numeric.Increment = 1m;
                numeric.Minimum = int.MinValue;
                numeric.Maximum = int.MaxValue;
            }
            else if (field.EffectiveValue.Kind == ObjectTemplateParamKind.Float)
            {
                numeric.DecimalPlaces = 6;
                numeric.Increment = 0.1m;
                numeric.Minimum = decimal.MinValue;
                numeric.Maximum = decimal.MaxValue;
            }
        }

        private void OnCellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != ColValue) { e.Cancel = e.ColumnIndex != ColValue; return; }
            EffectiveField field = FieldAt(e.RowIndex);
            if (field == null || !IsInlineEditable(field))
            {
                // Complex/raw/unresolved cells are not inline-editable (prevents corrupt typed edits).
                e.Cancel = true;
            }
        }

        // Commit checkbox toggles immediately (no Enter needed); text/numeric commit on CellEndEdit.
        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != ColValue) return;
            EffectiveField field = FieldAt(e.RowIndex);
            if (field == null || field.EffectiveValue == null) return;
            if (field.EffectiveValue.Kind != ObjectTemplateParamKind.Bool) return; // checkbox only.
            CommitCell(e.RowIndex);
        }

        private void OnCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != ColValue) return;
            EffectiveField field = FieldAt(e.RowIndex);
            if (field == null || field.EffectiveValue == null) return;
            if (field.EffectiveValue.Kind == ObjectTemplateParamKind.Bool) return; // handled on change.
            CommitCell(e.RowIndex);
        }

        // Reads the edited grid value, coerces it to a typed ObjectTemplateParamValue, and routes the
        // mutation through the controller — promoting an inherited row to a local override on commit.
        private void CommitCell(int rowIndex)
        {
            if (controller == null || rowIndex < 0 || rowIndex >= gridSurface.Rows.Count) return;
            EffectiveField field = FieldAt(rowIndex);
            if (field == null || field.EffectiveValue == null || !IsInlineEditable(field)) return;

            object cellValue = gridSurface.Rows[rowIndex].Cells[ColValue].Value;
            ObjectTemplateParamValue newValue;
            if (!TryCoerceValue(field.EffectiveValue, cellValue, out newValue))
            {
                lblStatus.Text = "Value \"" + (cellValue ?? "").ToString() + "\" is not valid for \"" + field.FieldName + "\".";
                lblStatus.ForeColor = Color.Red;
                // Re-bind to discard the rejected edit (restores the prior effective value display).
                BindEffectiveView();
                return;
            }

            // No-op guard: an unchanged value should not dirty the document.
            if (field.Origin == EffectiveFieldOriginKind.LocalOverride && ValuesEqual(field.EffectiveValue, newValue))
            {
                return;
            }

            if (field.Origin == EffectiveFieldOriginKind.LocalOverride)
            {
                controller.Apply(ObjectTemplateEditCommands.EditValue(field.FieldName, newValue));
            }
            else if (field.Origin == EffectiveFieldOriginKind.Inherited)
            {
                // Editing an inherited value PROMOTES it to a local override (D-04).
                controller.Apply(ObjectTemplateEditCommands.AddOverride(field.FieldName, newValue));
                lblStatus.Text = "Promoted \"" + field.FieldName + "\" to a local override.";
                lblStatus.ForeColor = Colors.Font();
            }
            // OnEditApplied re-resolves + re-binds.
        }

        // Coerces a grid cell's edited value (string/bool) into a typed ObjectTemplateParamValue that
        // matches the field's current Kind. Preserves the verbatim numeric delta byte. Returns false on
        // an invalid numeric input.
        private static bool TryCoerceValue(ObjectTemplateParamValue current, object cellValue, out ObjectTemplateParamValue result)
        {
            result = null;
            string raw = cellValue == null ? string.Empty : cellValue.ToString();
            switch (current.Kind)
            {
                case ObjectTemplateParamKind.Bool:
                {
                    bool b = cellValue is bool bv ? bv
                        : string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1";
                    result = ObjectTemplateParamValue.FromBool(b);
                    return true;
                }
                case ObjectTemplateParamKind.Int:
                {
                    int parsed;
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    {
                        // The numeric editor may surface a decimal-formatted string; coerce via decimal.
                        decimal d;
                        if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return false;
                        parsed = (int)Math.Truncate(d);
                    }
                    result = ObjectTemplateParamValue.FromInt(parsed, current.DeltaType ?? (byte)' ');
                    return true;
                }
                case ObjectTemplateParamKind.Float:
                {
                    float parsed;
                    if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return false;
                    result = ObjectTemplateParamValue.FromFloat(parsed, current.DeltaType ?? (byte)' ');
                    return true;
                }
                case ObjectTemplateParamKind.String:
                {
                    result = ObjectTemplateParamValue.FromString(raw);
                    return true;
                }
                default:
                    return false; // None / RawBytesHexFallback are not inline-editable.
            }
        }

        private static bool ValuesEqual(ObjectTemplateParamValue a, ObjectTemplateParamValue b)
        {
            if (a == null || b == null || a.Kind != b.Kind) return false;
            switch (a.Kind)
            {
                case ObjectTemplateParamKind.Bool: return a.BoolValue == b.BoolValue;
                case ObjectTemplateParamKind.Int: return a.IntValue == b.IntValue && a.DeltaType == b.DeltaType;
                case ObjectTemplateParamKind.Float: return a.FloatValue.Equals(b.FloatValue) && a.DeltaType == b.DeltaType;
                case ObjectTemplateParamKind.String: return string.Equals(a.StringValue, b.StringValue, StringComparison.Ordinal);
                default: return false;
            }
        }

        // Only SINGLE scalar typed values are inline-editable; complex/raw fallbacks and unresolved
        // rows are NOT (they are edited via the hex sub-editor or not at all — D-02 guarantee).
        private static bool IsInlineEditable(EffectiveField field)
        {
            if (field == null || field.EffectiveValue == null) return false;
            if (field.Origin == EffectiveFieldOriginKind.UnresolvedBase) return false;
            switch (field.EffectiveValue.Kind)
            {
                case ObjectTemplateParamKind.Bool:
                case ObjectTemplateParamKind.Int:
                case ObjectTemplateParamKind.Float:
                case ObjectTemplateParamKind.String:
                    return true;
                default:
                    return false; // None / RawBytesHexFallback → hex sub-editor.
            }
        }

        // ── Promote / Revert (D-04 — undoable via the controller) ────────────────

        private void OnPromoteClicked(object sender, EventArgs e)
        {
            PromoteSelectedField();
        }

        private void PromoteSelectedField()
        {
            if (controller == null) return;
            EffectiveField field = SelectedField();
            if (field == null) return;
            if (field.Origin != EffectiveFieldOriginKind.Inherited || field.EffectiveValue == null)
            {
                lblStatus.Text = "Select an inherited field to promote.";
                lblStatus.ForeColor = Colors.Font();
                return;
            }
            // Copy the inherited value verbatim into a new local chunk.
            controller.Apply(ObjectTemplateEditCommands.AddOverride(field.FieldName, field.EffectiveValue));
            lblStatus.Text = "Promoted \"" + field.FieldName + "\" to a local override.";
            lblStatus.ForeColor = Colors.Font();
        }

        private void OnRevertClicked(object sender, EventArgs e)
        {
            RevertSelectedField();
        }

        private void RevertSelectedField()
        {
            if (controller == null) return;
            EffectiveField field = SelectedField();
            if (field == null) return;
            if (field.Origin != EffectiveFieldOriginKind.LocalOverride)
            {
                lblStatus.Text = "Select a local override to revert.";
                lblStatus.ForeColor = Colors.Font();
                return;
            }

            // Determine the inherited value this field would fall back to (for the confirm + feedback).
            string ancestor;
            bool hasInherited = TryResolveInheritedValue(field.FieldName, out ancestor);
            if (!hasInherited)
            {
                // A local-only field has no inherited value to revert to — the button is disabled with
                // this tooltip, but guard defensively.
                lblStatus.Text = "No inherited value to revert to — this field exists only locally.";
                lblStatus.ForeColor = Colors.Font();
                return;
            }

            // Lightweight confirm (local + undoable, so a single inline confirm is sufficient).
            using (var dlg = new FormSaveConfirmDialog(
                heading: "Revert \"" + field.FieldName + "\"?",
                body: "This deletes the local override and restores the inherited value from "
                    + ancestor + ". You can undo this.",
                acceptVerb: "Revert",
                cancelVerb: "Cancel",
                showBackupCheckbox: false,
                backupCheckboxLabel: null))
            {
                dlg.ShowDialog(this);
                if (dlg.Outcome != FormSaveConfirmDialog.ConfirmOutcome.Accepted) return;
            }

            controller.Apply(ObjectTemplateEditCommands.RemoveOverride(field.FieldName));
            lblStatus.Text = "Reverted \"" + field.FieldName + "\" to the inherited value from " + ancestor + ".";
            lblStatus.ForeColor = Colors.Font();
        }

        // Resolves whether a field has an inherited value in some resolvable ancestor (and names it),
        // by re-resolving against the resolver with NO local chunk for that field. We approximate by
        // scanning the effective view computed WITHOUT the local override: cheaper here, we instead
        // walk the resolved chain via a throwaway resolve and look for the field as Inherited.
        private bool TryResolveInheritedValue(string fieldName, out string ancestorName)
        {
            ancestorName = null;
            if (otDocument == null) return false;
            // RemoveOverride mutates in place + raises EditApplied; to PREVIEW the inherited fallback
            // without mutating, resolve the chain and check whether any RESOLVED ancestor (a base) other
            // than the open template supplies this field. We reuse the base-locator walk: build a probe
            // template view by temporarily removing the local param, resolving, then re-adding it.
            //
            // Simpler + side-effect-free: re-run the resolver, then for the target field consult the
            // ancestors recorded in the breadcrumb. If any base is resolved AND the field is not
            // local-only, the revert lands on that base's value. We detect "local-only" by checking the
            // resolved view AFTER a hypothetical removal — done by removing, resolving, and restoring.
            MutableObjectTemplateParam local = null;
            foreach (MutableObjectTemplateParam p in otDocument.LocalParams)
            {
                if (string.Equals(p.FieldName, fieldName, StringComparison.Ordinal)) { local = p; break; }
            }
            if (local == null) return false;

            ObjectTemplateParamValue captured = local.Value;
            try
            {
                otDocument.RemoveOverride(fieldName);
                EffectiveTemplateView probe = ResolveEffectiveView(otDocument);
                foreach (EffectiveField f in probe.Fields)
                {
                    if (string.Equals(f.FieldName, fieldName, StringComparison.Ordinal)
                        && f.Origin == EffectiveFieldOriginKind.Inherited)
                    {
                        ancestorName = f.OriginAncestorName;
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                // Restore the local override so the document is unchanged by this preview.
                otDocument.AddOverride(fieldName, captured);
            }
        }

        // ── Reload candor (CF-05 — the editor STATES the reload; it never triggers one) ──

        private void OnReloadClicked(object sender, EventArgs e)
        {
            // CON-M-05 + CF-05: Utinni does NOT call ObjectTemplateList::reload. We surface the LOCKED
            // candor copy and route the saved asset through the routing-table audit trail only (which
            // classifies it as PendingNextSceneChange — no fresh reload hook is invoked).
            if (!string.IsNullOrEmpty(lastSavedPath))
            {
                bool clientUp = false;
                try { clientUp = Game.IsRunning; }
                catch { clientUp = false; }
                if (clientUp)
                {
                    DispatchReload(lastSavedPath);
                }
            }

            lblStatus.Text = "Templates re-resolve on the next scene change for objects re-instantiated then. "
                + "Objects already in the world — and shared base templates — keep the cached version until a relog. "
                + "Trigger a scene change via TJT's chat-command load, or relog to guarantee.";
            lblStatus.ForeColor = Colors.Font();

            // Optional 1s accent pulse on the reload badge to acknowledge the click.
            lblReloadBadge.ForeColor = Colors.Secondary();
            var pulse = new Timer { Interval = 1000 };
            pulse.Tick += (s, ev) =>
            {
                lblReloadBadge.ForeColor = Colors.Font();
                pulse.Stop();
                pulse.Dispose();
            };
            pulse.Start();
        }

        // ── Selection helpers + mutation-button enabled-state ────────────────────

        private EffectiveField FieldAt(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= gridSurface.Rows.Count) return null;
            return gridSurface.Rows[rowIndex].Tag as EffectiveField;
        }

        private EffectiveField SelectedField()
        {
            if (gridSurface.CurrentRow != null)
            {
                EffectiveField f = gridSurface.CurrentRow.Tag as EffectiveField;
                if (f != null) return f;
            }
            if (gridSurface.SelectedRows.Count > 0)
            {
                return gridSurface.SelectedRows[0].Tag as EffectiveField;
            }
            return null;
        }

        private void OnGridSelectionChanged(object sender, EventArgs e)
        {
            RefreshMutationButtonsState();
        }

        // Promote enables on an inherited (resolved) row; Revert enables on a local override that has a
        // resolvable inherited value (else DISABLED with the locked tooltip).
        private void RefreshMutationButtonsState()
        {
            EffectiveField field = SelectedField();
            bool hasDoc = otDocument != null;

            bool canPromote = hasDoc && field != null
                && field.Origin == EffectiveFieldOriginKind.Inherited && field.EffectiveValue != null;
            btnPromote.Enabled = canPromote;
            if (miPromote != null) miPromote.Enabled = canPromote;

            bool isLocal = hasDoc && field != null && field.Origin == EffectiveFieldOriginKind.LocalOverride;
            bool canRevert = false;
            if (isLocal)
            {
                string ancestor;
                canRevert = TryResolveInheritedValue(field.FieldName, out ancestor);
            }
            btnRevert.Enabled = canRevert;
            if (miRevert != null)
            {
                miRevert.Enabled = canRevert;
                miRevert.ToolTipText = (isLocal && !canRevert)
                    ? "No inherited value to revert to — this field exists only locally."
                    : "";
            }
            toolTip.SetToolTip(btnRevert, (isLocal && !canRevert)
                ? "No inherited value to revert to — this field exists only locally."
                : "Revert the selected local override back to inherited.");

            bool isComplex = hasDoc && field != null && field.EffectiveValue != null
                && field.EffectiveValue.Kind == ObjectTemplateParamKind.RawBytesHexFallback
                && field.Origin != EffectiveFieldOriginKind.UnresolvedBase;
            if (miEditRawBytes != null) miEditRawBytes.Enabled = isComplex;
        }

        // ── Grid context menu (promote / revert / edit raw bytes) ────────────────

        private void BuildGridContextMenu()
        {
            gridContextMenu = new UtinniContextMenuStrip();
            miPromote = new ToolStripMenuItem("Promote to local override");
            miPromote.Click += (s, e) => PromoteSelectedField();
            miRevert = new ToolStripMenuItem("Revert to inherited value");
            miRevert.Click += (s, e) => RevertSelectedField();
            miEditRawBytes = new ToolStripMenuItem("Edit raw bytes…");
            miEditRawBytes.Click += (s, e) => EditSelectedRawBytes();
            gridContextMenu.Items.AddRange(new ToolStripItem[]
            {
                miPromote, miRevert, new ToolStripSeparator(), miEditRawBytes,
            });
        }

        private void OnCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            // Right-click selects the row, then shows the context menu anchored at the cursor.
            gridSurface.ClearSelection();
            gridSurface.Rows[e.RowIndex].Selected = true;
            gridSurface.CurrentCell = gridSurface.Rows[e.RowIndex].Cells[ColField];
            RefreshMutationButtonsState();
            gridContextMenu.Show(Cursor.Position);
        }

        // Double-clicking a complex/raw value cell opens the hex sub-editor.
        private void OnCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != ColValue) return;
            EffectiveField field = FieldAt(e.RowIndex);
            if (field == null || field.EffectiveValue == null) return;
            if (field.EffectiveValue.Kind == ObjectTemplateParamKind.RawBytesHexFallback
                && field.Origin != EffectiveFieldOriginKind.UnresolvedBase)
            {
                EditSelectedRawBytes();
            }
        }

        // Opens the FormParamHexEditor modal for the selected complex param and, on OK, replaces the
        // param's raw leaf bytes via the controller (dirty + undoable). Inherited complex params are
        // promoted to a local override carrying the edited bytes.
        private void EditSelectedRawBytes()
        {
            if (controller == null) return;
            EffectiveField field = SelectedField();
            if (field == null || field.EffectiveValue == null) return;
            if (field.EffectiveValue.Kind != ObjectTemplateParamKind.RawBytesHexFallback
                || field.Origin == EffectiveFieldOriginKind.UnresolvedBase)
            {
                return;
            }

            byte[] currentBytes = field.EffectiveValue.GetRawBytesCopy();
            ObjectTemplateDataTypeTag tag = field.EffectiveValue.DataTypeTag;
            using (var dlg = new FormParamHexEditor(field.FieldName, currentBytes))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.ResultBytes == null) return;
                ObjectTemplateParamValue newValue = ObjectTemplateParamValue.FromRawBytes(dlg.ResultBytes, tag);

                if (field.Origin == EffectiveFieldOriginKind.LocalOverride)
                {
                    controller.Apply(ObjectTemplateEditCommands.EditValue(field.FieldName, newValue));
                }
                else
                {
                    // Inherited complex param → promote to a local override with the edited bytes.
                    controller.Apply(ObjectTemplateEditCommands.AddOverride(field.FieldName, newValue));
                    lblStatus.Text = "Promoted \"" + field.FieldName + "\" to a local override.";
                    lblStatus.ForeColor = Colors.Font();
                }
            }
        }

        // ── Save▾ drop-down (modes 1/2/4 via the ObjectTemplateSaveTargets shim) ──

        private void BuildSaveMenu()
        {
            saveMenu = new UtinniContextMenuStrip();
            miSaveInPlace = new ToolStripMenuItem("Save (in place)");
            miSaveInPlace.Click += OnSaveInPlaceClick;
            miSaveLooseOverride = new ToolStripMenuItem("Save as loose override");
            miSaveLooseOverride.Click += OnSaveLooseOverrideClick;
            miSaveAs = new ToolStripMenuItem("Save As…");
            miSaveAs.Click += OnSaveAsClick;
            miPatchLive = new ToolStripMenuItem("Patch live client (in memory)");
            miPatchLive.Enabled = false; // CF-03 — Mode 3 disabled.
            miPatchLive.ToolTipText = "Live patch requires opening from client memory — not wired in this phase.";
            miPatchLive.Click += OnPatchLiveClientClick;
            miRepackTre = new ToolStripMenuItem("Repack into source .tre…");
            miRepackTre.Enabled = false;
            miRepackTre.ToolTipText = "Open from a packed .tre to repack the source archive.";
            miRepackTre.Click += OnRepackTreClick;
            saveMenu.Items.AddRange(new ToolStripItem[]
            {
                miSaveInPlace, miSaveLooseOverride, miSaveAs,
                new ToolStripSeparator(),
                miPatchLive, miRepackTre,
            });
        }

        // Provenance-gated enabled-state (no Phase-9 cascade term — OT has no column cascade).
        private void RefreshSaveMenuEnabledState()
        {
            bool hasDoc = otDocument != null;
            bool isLooseFile = Source is OpenSource.LooseFile;
            bool isTreArchive = Source is OpenSource.TreArchive;
            bool isUnknown = Source is OpenSource.Unknown;

            if (miSaveInPlace != null)
            {
                miSaveInPlace.Enabled = hasDoc && isLooseFile && !saveInFlight;
                miSaveInPlace.ToolTipText = isLooseFile
                    ? ""
                    : "Cannot save in place — file came from .tre or unknown source. Use Save as loose override or Save As.";
            }
            if (miSaveLooseOverride != null)
            {
                miSaveLooseOverride.Enabled = hasDoc && (isLooseFile || isTreArchive) && !saveInFlight;
                miSaveLooseOverride.ToolTipText = (isLooseFile || isTreArchive)
                    ? ""
                    : "Cannot resolve archive record — use Save As to write to a chosen file.";
            }
            if (miSaveAs != null)
            {
                miSaveAs.Enabled = hasDoc && !saveInFlight; // escape hatch — always available on a doc.
                miSaveAs.ToolTipText = "Save the current edits to a path you choose.";
            }
            if (miPatchLive != null)
            {
                miPatchLive.Enabled = false; // CF-03 — disabled inherited.
                miPatchLive.ToolTipText = "Live patch requires opening from client memory — not wired in this phase.";
            }
            if (miRepackTre != null)
            {
                miRepackTre.Enabled = hasDoc && isTreArchive && !saveInFlight;
                miRepackTre.ToolTipText = isTreArchive
                    ? ""
                    : "Open from a packed .tre to repack the source archive.";
            }

            btnSave.Enabled = hasDoc && !saveInFlight && (isLooseFile || isTreArchive || isUnknown);
        }

        private void OnSaveButtonClick(object sender, EventArgs e)
        {
            if (saveMenu == null) return;
            saveMenu.Show(btnSave, new Point(0, btnSave.Height));
        }

        private async void OnSaveInPlaceClick(object sender, EventArgs e)
        {
            if (otDocument == null) return;
            if (!(Source is OpenSource.LooseFile))
            {
                SetFailure("Cannot save in place — file came from .tre or unknown source. Use Save as loose override or Save As.");
                return;
            }
            await DoFileSaveAsync(() => ObjectTemplateSaveTargets.SaveInPlace(otDocument, Source), "in place");
        }

        private async void OnSaveLooseOverrideClick(object sender, EventArgs e)
        {
            if (otDocument == null) return;
            string clientRoot = ResolveClientRoot();
            if (string.IsNullOrEmpty(clientRoot))
            {
                SetFailure("Could not locate the client root — use Save As… and we'll remember the directory.");
                return;
            }
            string subDir = ini.GetString(SettingsSection, "looseOverrideDir");
            if (string.IsNullOrEmpty(subDir))
            {
                subDir = "loose";
                lblStatus.Text = "Saving to " + subDir
                    + ". Change the loose-override directory in [" + SettingsSection + "] looseOverrideDir if needed.";
                lblStatus.ForeColor = Colors.Font();
            }
            await DoFileSaveAsync(
                () => ObjectTemplateSaveTargets.SaveLooseOverride(otDocument, Source, clientRoot, subDir),
                "loose override");
        }

        private async void OnSaveAsClick(object sender, EventArgs e)
        {
            if (otDocument == null) return;
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Save object template as…";
                sfd.Filter = "Object template files (*.iff)|*.iff|All files (*.*)|*.*";
                sfd.FileName = !string.IsNullOrEmpty(displayName) ? displayName : "untitled.iff";
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                string path = sfd.FileName;
                await DoFileSaveAsync(() => ObjectTemplateSaveTargets.SaveToPath(otDocument, path), "save-as");
            }
        }

        // CF-03: Mode 3 ships disabled; this handler is defensive (reachable only if somehow enabled).
        private void OnPatchLiveClientClick(object sender, EventArgs e)
        {
            SetFailure("Live patch is disabled in this phase.");
        }

        private async void OnRepackTreClick(object sender, EventArgs e)
        {
            if (otDocument == null) return;
            var ta = Source as OpenSource.TreArchive;
            if (ta == null)
            {
                SetFailure("Open from a packed .tre to repack the source archive.");
                return;
            }

            string archiveName = Path.GetFileName(ta.TrePath ?? "");
            bool backupRequested;
            using (var dlg = new FormSaveConfirmDialog(
                heading: "Repack " + archiveName + "?",
                body: "This rewrites the entire " + archiveName + " archive on disk and replaces it "
                    + "atomically. Untouched entries are preserved byte-for-byte; only the edited entry "
                    + "recompresses. If the client holds the archive open, the repack is refused without "
                    + "a partial-write. Continue?",
                acceptVerb: "Repack",
                cancelVerb: "Cancel",
                showBackupCheckbox: true,
                backupCheckboxLabel: "Create a timestamped backup (" + archiveName + ".<yyyyMMdd-HHmmss>.bak) first"))
            {
                dlg.ShowDialog(this);
                if (dlg.Outcome != FormSaveConfirmDialog.ConfirmOutcome.Accepted) return;
                backupRequested = dlg.BackupRequested;
            }

            saveInFlight = true;
            RefreshSaveMenuEnabledState();
            RefreshReloadButtonState();
            lblStatus.Text = "Saving (repack " + archiveName + ")…";
            lblStatus.ForeColor = Colors.Font();

            TreRepackSaveTarget.TreRepackResult result;
            try
            {
                result = await ObjectTemplateSaveTargets.RepackIntoSourceTre(otDocument, ta, backupRequested)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetFailure("Repack failed: " + ex.Message + " Your edits are kept in the editor.");
                saveInFlight = false;
                RefreshSaveMenuEnabledState();
                RefreshReloadButtonState();
                return;
            }

            saveInFlight = false;

            switch (result)
            {
                case TreRepackSaveTarget.TreRepackResult.Replaced:
                case TreRepackSaveTarget.TreRepackResult.BackedUpThenReplaced:
                    lastSavedPath = ta.TrePath;
                    lblStatus.Text = result == TreRepackSaveTarget.TreRepackResult.BackedUpThenReplaced
                        ? "Repacked " + archiveName + " (backup created)"
                        : "Repacked " + archiveName;
                    lblStatus.ForeColor = Colors.Font();
                    if (controller != null) controller.MarkSaved();
                    DispatchReload(lastSavedPath);
                    break;

                case TreRepackSaveTarget.TreRepackResult.RefusedClientHoldsArchive_LooseOverrideRecommended:
                    lblStatus.Text = "Client holds the archive — try Save as loose override instead.";
                    lblStatus.ForeColor = Color.Red;
                    break;

                case TreRepackSaveTarget.TreRepackResult.Failed:
                default:
                    lblStatus.Text = "Repack failed — your edits are retained. If this is a V6000 (encrypted) archive, use Save as loose override.";
                    lblStatus.ForeColor = Color.Red;
                    break;
            }

            RefreshSaveMenuEnabledState();
            RefreshReloadButtonState();
        }

        // Shared file-save orchestration (modes 1/2): the saveInFlight barrier disables Save▾ + Reload
        // while the Task is in flight (stale-bytes barrier); on success MarkSaved() clears the dirty
        // baseline + the reload is routed through the audit trail.
        private async Task<bool> DoFileSaveAsync(
            Func<Task<IffSaveTargets.SaveResult>> saveOp,
            string modeLabel)
        {
            saveInFlight = true;
            RefreshSaveMenuEnabledState();
            RefreshReloadButtonState();
            lblStatus.Text = "Saving (" + modeLabel + ")…";
            lblStatus.ForeColor = Colors.Font();

            IffSaveTargets.SaveResult result;
            try
            {
                result = await saveOp().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                result = IffSaveTargets.SaveResult.Failure(ex.Message);
            }

            saveInFlight = false;

            if (result.Ok)
            {
                lblStatus.Text = "Saved " + (displayName ?? Path.GetFileName(result.Path) ?? "<untitled>") + " (" + modeLabel + ")";
                lblStatus.ForeColor = Colors.Font();
                lastSavedPath = result.Path;
                if (controller != null) controller.MarkSaved();
                DispatchReload(result.Path);
            }
            else
            {
                lblStatus.Text = (result.Message ?? "Save failed.") + " Your edits are kept in the editor — try another save target.";
                lblStatus.ForeColor = Color.Red;
            }
            RefreshSaveMenuEnabledState();
            RefreshReloadButtonState();
            return result.Ok;
        }

        // Routes the just-saved object-template asset through Phase 8's tiered reload dispatcher. The
        // object-template case classifies as PendingNextSceneChange (tier-(b) per CF-05); the dispatch is
        // for the routing-table audit trail — the user-facing reload copy stays the locked CF-05 wording.
        private void DispatchReload(string savedPath)
        {
            if (string.IsNullOrEmpty(savedPath)) return;
            try
            {
                ClientReloadDispatcher.Dispatch(savedPath, RootTypeForDispatch());
            }
            catch
            {
                // Dispatch gates on Game.IsRunning internally; never let a binding hiccup tear down
                // the save-success path.
            }
        }

        // The open template's root TypeId (e.g. SHOT/STOT/SBOT) so the classifier routes it via the
        // object-template allowlist; falls back to null (conservative .iff fallback) if unavailable.
        private string RootTypeForDispatch()
        {
            return otDocument != null && !string.IsNullOrEmpty(otDocument.RootType)
                ? otDocument.RootType.Trim()
                : null;
        }

        // Reload-button state: disabled while a save Task is in flight (stale-bytes barrier); hidden +
        // disabled with the no-client tooltip when no live client is up. Otherwise enabled — OnReloadClicked
        // surfaces the CF-05 locked candor.
        private void RefreshReloadButtonState()
        {
            bool clientUp = false;
            try { clientUp = Game.IsRunning; }
            catch { clientUp = false; }

            if (!clientUp)
            {
                btnReload.Enabled = false;
                toolTip.SetToolTip(btnReload, "No live client — start SWG to apply edits in-session.");
                lblReloadBadge.Visible = false;
                return;
            }

            btnReload.Enabled = !saveInFlight;
            toolTip.SetToolTip(btnReload, "State how/when the client re-resolves the edit (it never auto-triggers a reload).");
            lblReloadBadge.Visible = otDocument != null;
        }

        // ── Keyboard shortcuts (Ctrl+Z / Ctrl+Y) ─────────────────────────────
        //
        // Caught at the form BEFORE the DataGridView sees them. MUST NOT dispatch to the scene
        // UndoRedoManager (CON-M-05 — extra-load-bearing for object templates).
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z))
            {
                if (controller != null && controller.CanUndo) controller.Undo();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Y))
            {
                if (controller != null && controller.CanRedo) controller.Redo();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ── Public entry points (TRE Browser + IFF Editor hand-offs) ─────────

        /// <summary>
        /// TRE Browser hand-off. Reads the resolved payload as a typed object template and binds it with
        /// TreArchive (or Unknown on degraded resolve) provenance — mirrors
        /// <c>FormDatatableEditor.OpenFromTreEntry</c> with the typed object-template wrap swapped in.
        /// </summary>
        public void OpenFromTreEntry(byte[] payload, string resolvedArchivePath, string logicalPath, long archiveLocalOffset)
        {
            if (payload == null)
            {
                SetFailure("TRE entry has no payload to open.");
                return;
            }
            try
            {
                IffDocument iff;
                using (var ms = new MemoryStream(payload, writable: false))
                {
                    iff = IffReader.Read(ms);
                }
                MutableIffDocument mIff = MutableIffDocument.FromDocument(iff, payload);
                MutableObjectTemplate otDoc = MutableObjectTemplate.FromMutableIff(mIff);
                OpenSource src = TreRecordIndexResolver.ResolveOrUnknown(resolvedArchivePath, archiveLocalOffset, logicalPath);
                string name = logicalPath != null ? Path.GetFileName(logicalPath) : Path.GetFileName(resolvedArchivePath ?? "");
                LoadDocument(otDoc, src, name);
            }
            catch (Exception ex)
            {
                SetFailure("TRE hand-off failed: " + ex.Message);
            }
        }

        /// <summary>
        /// IFF Editor hand-off. Wraps the IFF Editor's in-memory <see cref="MutableIffDocument"/> as a
        /// typed object template WITHOUT re-parsing bytes (the IFF Editor passes its existing mutable doc
        /// + Source directly).
        /// </summary>
        public void OpenFromMutableIff(MutableIffDocument mutIff, OpenSource source, string displayName)
        {
            if (mutIff == null)
            {
                SetFailure("IFF hand-off has no document to open.");
                return;
            }
            try
            {
                MutableObjectTemplate otDoc = MutableObjectTemplate.FromMutableIff(mutIff);
                LoadDocument(otDoc, source ?? OpenSource.Unknown.Instance, displayName);
            }
            catch (Exception ex)
            {
                SetFailure("Could not switch to the typed object-template view: " + ex.Message);
            }
        }

        // ── Open… ────────────────────────────────────────────────────────────

        private void OnOpenClicked(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Open object template…";
                ofd.Filter = "Object template files (*.iff)|*.iff|All files (*.*)|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                OpenFromLooseFile(ofd.FileName);
            }
        }

        private void OpenFromLooseFile(string path)
        {
            try
            {
                lblStatus.Text = "Opening " + Path.GetFileName(path) + "…";
                lblStatus.ForeColor = Colors.Font();

                byte[] bytes = File.ReadAllBytes(path);
                IffDocument iff;
                using (var ms = new MemoryStream(bytes, writable: false))
                {
                    iff = IffReader.Read(ms);
                }
                MutableIffDocument mutableIff = MutableIffDocument.FromDocument(iff, bytes);
                MutableObjectTemplate otDoc = MutableObjectTemplate.FromMutableIff(mutableIff);

                LoadDocument(otDoc, new OpenSource.LooseFile(path), Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                SetFailure("Open failed: " + ex.Message);
            }
        }

        // Resolves the client install root (process-module primary, then GetWorkingDirectory(), then the
        // [TreBrowser] clientDir ini fallback) — mirrors FormDatatableEditor.ResolveClientRoot.
        private string ResolveClientRoot()
        {
            try
            {
                string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                string moduleDir = Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(moduleDir) && Directory.Exists(moduleDir)) return moduleDir;
            }
            catch { /* fall through */ }
            try
            {
                string wd = UtinniCore.Utility.utility.GetWorkingDirectory();
                if (!string.IsNullOrEmpty(wd) && Directory.Exists(wd)) return wd;
            }
            catch { /* binding unavailable outside a live client */ }
            try
            {
                string configured = ini.GetString("TreBrowser", "clientDir");
                if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured)) return configured;
            }
            catch { /* ini may not have the key yet */ }
            return null;
        }

        // ── Dirty visuals + counters ─────────────────────────────────────────

        private void SetTitle(string prefix)
        {
            this.Text = string.IsNullOrEmpty(prefix) ? BaseTitle : prefix + " " + BaseTitle;
            this.Invalidate();
        }

        private void UpdateDirtyVisuals()
        {
            bool dirty = controller != null && controller.IsDirty;
            lblDirty.Text = dirty ? "Unsaved changes" : "";
            SetTitle(dirty ? "●" : null);
        }

        private void UpdateCounters()
        {
            if (effectiveView == null)
            {
                lblCounters.Text = "0 fields";
                lblCounters.ForeColor = Colors.FontDisabled();
                return;
            }

            int fields = 0;
            int overrides = 0;
            int unresolved = 0;
            foreach (EffectiveField f in effectiveView.Fields)
            {
                fields++;
                if (f.Origin == EffectiveFieldOriginKind.LocalOverride) overrides++;
                else if (f.Origin == EffectiveFieldOriginKind.UnresolvedBase) unresolved++;
            }
            // A dirty count reflects net-applied edits relative to the save baseline (Plan 11-04 mutates).
            bool dirty = controller != null && controller.IsDirty;
            int dirtyCount = dirty ? 1 : 0;

            if (!dirty && unresolved == 0)
            {
                lblCounters.Text = fields + " fields · " + overrides + " local";
                lblCounters.ForeColor = Colors.FontDisabled();
            }
            else
            {
                string text = fields + " fields · " + overrides + " local · " + dirtyCount + " dirty";
                if (unresolved > 0) text += " · " + unresolved + " unresolved";
                lblCounters.Text = text;
                lblCounters.ForeColor = unresolved > 0 ? Color.Red : Colors.Font();
            }
        }

        private void SetFailure(string message)
        {
            lblStatus.Text = message;
            lblStatus.ForeColor = Color.Red;
        }

        // ── Singleton hide-not-dispose (MANDATORY FROM COMMIT 1) ─────────────

        private void FormObjectTemplateEditor_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                ini.AddSetting(SettingsSection, "width", Width.ToString(), UtINI.Value.Types.VtInt);
                ini.AddSetting(SettingsSection, "height", Height.ToString(), UtINI.Value.Types.VtInt);
                ini.Save();
            }
            catch
            {
                // Persistence is best-effort; never block close.
            }

            // The CloseReason DECISION lives in the framework helper so the xUnit guard can exercise it
            // without instantiating the form. On user-initiated close, hide instead of disposing so
            // subsequent Show() calls re-Show this same singleton instance (Phase 8 b899504).
            if (SingletonFormClosePolicy.ShouldHideInsteadOfDispose(e.CloseReason))
            {
                e.Cancel = true;
                Hide();
            }
        }
    }
}
