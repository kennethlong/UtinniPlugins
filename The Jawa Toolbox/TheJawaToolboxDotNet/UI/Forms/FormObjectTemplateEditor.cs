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
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using UtinniCore.Utinni;
using UtinniCoreDotNet.Editing;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.Formats.ObjectTemplate;
using UtinniCoreDotNet.Formats.Tre;
using UtinniCoreDotNet.PluginFramework;
using UtinniCoreDotNet.Saving;
using UtinniCoreDotNet.UI;
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

            // The only ENABLED, functional toolbar button in Plan 11-03 is Open…. The rest are wired in
            // Plan 11-04 (Save / Promote / Revert / Reload), or become enabled on LoadDocument
            // (Undo / Redo / Find / Replace / Show inherited).
            btnOpen.Click += OnOpenClicked;
            btnUndo.Click += OnUndoClicked;
            btnRedo.Click += OnRedoClicked;
            tglShowInherited.CheckedChanged += OnShowInheritedToggled;

            // Plan 11-04 stub handlers (status-only until the mutation/save bodies land).
            btnSave.Click += OnSaveButtonClick;
            btnPromote.Click += OnPromoteClicked;
            btnRevert.Click += OnRevertClicked;
            btnReload.Click += OnReloadClicked;

            // Configure the four fixed effective-view columns + the cell-state overlay formatter.
            ConfigureGridColumns();
            gridSurface.CellFormatting += OnGridCellFormatting;

            SetTitle(null);
            UpdateDirtyVisuals();
            UpdateCounters();
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
            gridSurface.ReadOnly = true; // value editing is wired Plan 11-04.

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
                ReadOnly = true,
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
                    lblReloadBadge.Visible = true;

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
                    gridRow.Cells[ColValue].Value = FormatEffectiveValue(field);
                    gridRow.Cells[ColOrigin].Value = FormatOrigin(field);
                    gridRow.Cells[ColType].Value = FormatType(field);

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
        }

        private void EnableDocumentControls()
        {
            btnFind.Enabled = true;
            btnReplace.Enabled = true;
            tglShowInherited.Enabled = true;
            // Save / Promote / Revert / Reload bodies land in Plan 11-04; they stay disabled this plan.
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

        // ── Plan 11-04 stub handlers (status-only until the mutation/save bodies land) ──

        private void OnSaveButtonClick(object sender, EventArgs e)
        {
            lblStatus.Text = "Save targets are wired in the next plan.";
            lblStatus.ForeColor = Colors.Font();
        }

        private void OnPromoteClicked(object sender, EventArgs e)
        {
            lblStatus.Text = "Promote to override is wired in the next plan.";
            lblStatus.ForeColor = Colors.Font();
        }

        private void OnRevertClicked(object sender, EventArgs e)
        {
            lblStatus.Text = "Revert to inherited is wired in the next plan.";
            lblStatus.ForeColor = Colors.Font();
        }

        private void OnReloadClicked(object sender, EventArgs e)
        {
            // CF-05 LOCKED candor copy: the editor STATES the reload, it never triggers one.
            lblStatus.Text = "Templates re-resolve on the next scene change for objects re-instantiated then. "
                + "Objects already in the world — and shared base templates — keep the cached version until a relog. "
                + "Trigger a scene change via TJT's chat-command load, or relog to guarantee.";
            lblStatus.ForeColor = Colors.Font();
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
