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

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TJT.Saving;
using TJT.UI.Controls;
using UtinniCore.Utinni;
using UtinniCoreDotNet.Editing;
using UtinniCoreDotNet.Formats.Datatable;
using UtinniCoreDotNet.Formats.Iff;
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
    /// Typed datatable editor window — a resizable <see cref="UtinniForm"/> registered ONCE via the
    /// plugin's <c>GetForms()</c> list (the Wave-1 editors are forms, not the fixed 417px SubPanel).
    /// Hosts a <see cref="ThemedDataGridView"/> bound to a <see cref="DataTableDocument"/> with
    /// per-type cell widgets. Ports the Phase 8 <c>FormIffEditor</c> shape with the typed-document
    /// layer swapped in.
    ///
    /// <para><b>Plan 09-03 staging:</b> this plan ships the host + grid + per-type widgets + the
    /// <c>Open…</c> entry point only. The Save▾ menu items + deferred toolbar buttons render DISABLED
    /// with explanatory tooltips (NOT throwing). Subsequent plans wire them: 09-04 (controller +
    /// undo/redo + structural ops), 09-05 (save targets + hand-offs), 09-06 (CSV + Find/Replace +
    /// sort). The <c>controller</c> field is null in this plan, so dirty visuals are always clean and
    /// cell edits commit directly to the model (see the grid-binding commit-back seam below).</para>
    ///
    /// <para><b>Singleton hide-not-dispose:</b> <see cref="FormDatatableEditor_FormClosing"/>
    /// delegates its CloseReason decision to
    /// <see cref="SingletonFormClosePolicy.ShouldHideInsteadOfDispose"/> — the Phase-8-smoke-locked
    /// pattern applied from commit 1.</para>
    /// </summary>
    public partial class FormDatatableEditor : UtinniForm, IEditorForm
    {
        private readonly IEditorPlugin editorPlugin;
        private readonly UtINI ini;
        private readonly ToolTip toolTip = new ToolTip();

        private DataTableDocument dtDocument;

        // Editor-local undo/redo + structural-op engine (Plan 09-04). Null until LoadDocument binds a
        // document; cell edits + structural ops route through controller.Apply.
        private DatatableEditController controller;

        // D-02 column-reorder/delete safety-net session-suppress flag (UI-SPEC assumption #5). Set when
        // the user ticks "Don't ask again this session"; per-form-instance, resets on Hide.
        private bool sessionSuppressColumnSafetyNet;

        // Right-click context menu for row/column/cell structural ops (built lazily on first bind).
        private ContextMenuStrip gridContextMenu;

        // Friendly display name for the loaded document (Save As… default name in Plan 09-05).
        private string displayName;

        /// <summary>
        /// Provenance descriptor for the currently loaded document. Plan 09-05 sets this on the open
        /// paths to gate the Save▾ modes; defaults to <see cref="OpenSource.Unknown"/>.
        /// </summary>
        public OpenSource Source { get; set; }

        // Save▾ drop-down items (kept as fields so RefreshSaveMenuEnabledState can flip them).
        private UtinniContextMenuStrip saveMenu;
        private ToolStripMenuItem miSaveInPlace;
        private ToolStripMenuItem miSaveLooseOverride;
        private ToolStripMenuItem miSaveAs;
        private ToolStripMenuItem miPatchLive;
        private ToolStripMenuItem miRepackTre;

        // Floating DT_HashString hash-preview overlay, alive only while a hash cell is in edit mode.
        private DatatableHashStringEditor hashEditor;
        private TextBox activeHashEditingControl;

        // Plan 09-05: save-in-flight barrier (PATTERNS § High-Leverage Reuse Points #4 + Phase 8
        // MEDIUM-9 stale-bytes reload race). True while a Save▾ Task is awaiting; disables Save▾ +
        // Reload until the await completes. lastSavedPath drives the reload-dispatch classification.
        private bool saveInFlight;
        private string lastSavedPath;

        public FormDatatableEditor(IEditorPlugin editorPlugin)
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

            Width = ini.GetInt("DatatableEditor", "width");
            Height = ini.GetInt("DatatableEditor", "height");

            // Theme via Colors.*() accessors only — no raw ARGB literals.
            toolbar.BackColor = Colors.Primary();
            sep1.BackColor = Colors.Primary();
            sep2.BackColor = Colors.Primary();
            sep3.BackColor = Colors.Primary();
            sep4.BackColor = Colors.Primary();
            sep5.BackColor = Colors.Primary();
            pnlFindReplace.BackColor = Colors.Primary();
            pnlStatus.BackColor = Colors.Primary();
            pnlCounters.BackColor = Colors.Primary();
            lblDirty.ForeColor = Colors.Secondary(); // accent for unsaved-changes marker
            lblStatus.ForeColor = Colors.Font();

            // Default provenance is Unknown (W-3 contract). Plan 09-05 open paths override.
            Source = OpenSource.Unknown.Instance;

            // Deferred-feature toolbar buttons ship DISABLED with explanatory tooltips (iter-2 item
            // 4 — NOT click handlers that throw). Subsequent plans enable + wire them. Plan 09-05
            // wires Save▾ (btnSave is the drop-down anchor) so its tooltip is no longer the deferred copy.
            toolTip.SetToolTip(btnSave, "Save the current datatable (choose a target).");
            toolTip.SetToolTip(btnImportCsv, "CSV import is wired by a later step (Plan 09-06).");
            toolTip.SetToolTip(btnExportCsv, "CSV export is wired by a later step (Plan 09-06).");
            toolTip.SetToolTip(btnFind, "Find is wired by a later step (Plan 09-06).");
            toolTip.SetToolTip(btnReplace, "Replace is wired by a later step (Plan 09-06).");

            // The only ENABLED, functional toolbar buttons in Plan 09-03 are Open… and Reload.
            btnOpen.Click += OnOpenClicked;
            btnReload.Click += OnReloadClicked;
            btnSave.Click += OnSaveButtonClick;

            // Plan 09-04: controller-backed undo/redo + structural ops. The buttons stay DISABLED until
            // a document is bound (UpdateUndoRedoState / OnEditApplied flip them); their handlers route
            // through the controller.
            btnUndo.Click += OnUndoClicked;
            btnRedo.Click += OnRedoClicked;
            btnAddRow.Click += OnAddRowClicked;
            btnAddColumn.Click += OnAddColumnClicked;
            btnResolveCascade.Click += OnResolveCascadeClicked;

            // Grid-binding commit-back wiring (iter-2 item 1).
            gridSurface.CellEndEdit += OnCellEndEdit;
            gridSurface.CellValueChanged += OnCellValueChanged;
            gridSurface.EditingControlShowing += OnEditingControlShowing;
            gridSurface.CellBeginEdit += OnCellBeginEdit;

            // Save▾ drop-down (items DISABLED + tooltip; wired in Plan 09-05).
            BuildSaveMenu();

            SetTitle(null);
            RefreshSaveMenuEnabledState();
            UpdateDirtyVisuals();
            UpdateCounters();
        }

        // ── IEditorForm ──────────────────────────────────────────────────────

        public string GetName() { return this.Text; }

        public Form Create(IEditorPlugin plugin, List<Form> parentChildren)
        {
            foreach (Form form in parentChildren)
            {
                if (form.GetType() == typeof(FormDatatableEditor))
                {
                    form.Activate();
                    return null;
                }
            }
            FormDatatableEditor newForm = new FormDatatableEditor(plugin);
            newForm.Show();
            parentChildren.Add(newForm);
            return newForm;
        }

        // ── Settings ─────────────────────────────────────────────────────────

        private void CreateSettings()
        {
            ini.AddSetting("DatatableEditor", "width", "1200", UtINI.Value.Types.VtInt);
            ini.AddSetting("DatatableEditor", "height", "760", UtINI.Value.Types.VtInt);
            ini.AddSetting("DatatableEditor", "splitterDistance", "360", UtINI.Value.Types.VtInt);
            ini.AddSetting("DatatableEditor", "findReplaceVisible", "0", UtINI.Value.Types.VtBool);
            ini.AddSetting("DatatableEditor", "editCommentRows", "0", UtINI.Value.Types.VtBool);
            ini.AddSetting("DatatableEditor", "looseOverrideDir", "", UtINI.Value.Types.VtString);
        }

        // ── Load ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Binds a typed <see cref="DataTableDocument"/> to the grid surface. Builds the per-column
        /// <see cref="DataGridViewColumn"/> via <see cref="DatatableColumnFactory.Build"/> and
        /// populates rows non-virtual per the grid-binding contract.
        /// </summary>
        public void LoadDocument(DataTableDocument doc, OpenSource source, string displayName)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            this.dtDocument = doc;
            this.Source = source ?? OpenSource.Unknown.Instance;
            this.displayName = displayName;

            // Plan 09-04: wire the editor-local undo/redo + structural-op controller. Replaces Plan
            // 09-03's null-controller staging. The session safety-net flag resets on each fresh load.
            if (controller != null) controller.EditApplied -= OnEditApplied;
            controller = new DatatableEditController(doc.Mutable);
            controller.EditApplied += OnEditApplied;
            sessionSuppressColumnSafetyNet = false;

            MutableDataTableDocument mutable = doc.Mutable;
            var columns = mutable.Columns
                .Select(c => DatatableColumnFactory.Build(c.Name, c.ColumnType))
                .ToList();

            gridSurface.BindMutable(mutable, columns);

            EnsureGridContextMenu();

            lblEmptyState.Visible = false;
            UpdateUndoRedoState();
            UpdateDirtyVisuals();
            RefreshSaveMenuEnabledState();
            UpdateCounters();
            btnResolveCascade.Visible = controller.PendingCascadeContext != null;
        }

        // ── Controller event wiring (Plan 09-04) ─────────────────────────────

        private void OnEditApplied(object sender, EventArgs e)
        {
            if (dtDocument != null) gridSurface.RefreshMutable(dtDocument.Mutable);
            UpdateUndoRedoState();
            UpdateDirtyVisuals();
            RefreshSaveMenuEnabledState();
            UpdateCounters();
            btnResolveCascade.Visible = controller != null && controller.PendingCascadeContext != null;
        }

        private void UpdateUndoRedoState()
        {
            btnUndo.Enabled = controller != null && controller.CanUndo;
            btnRedo.Enabled = controller != null && controller.CanRedo;
            btnAddRow.Enabled = controller != null;
            btnAddColumn.Enabled = controller != null;
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            if (controller != null && controller.CanUndo) controller.Undo();
        }

        private void OnRedoClicked(object sender, EventArgs e)
        {
            if (controller != null && controller.CanRedo) controller.Redo();
        }

        // ── Structural ops: Add row / Add column… ────────────────────────────

        private void OnAddRowClicked(object sender, EventArgs e)
        {
            if (controller == null || dtDocument == null) return;
            controller.Apply(DatatableEditCommands.AddRow(dtDocument.Mutable, dtDocument.Mutable.Rows.Count));
            RebindGrid();
        }

        private void OnAddColumnClicked(object sender, EventArgs e)
        {
            if (controller == null || dtDocument == null) return;
            using (var dlg = new FormAddColumnDialog(dtDocument.Mutable.Columns.Select(c => c.Name)))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    controller.Apply(DatatableEditCommands.AddColumn(
                        dtDocument.Mutable, dlg.Result.Name, dlg.Result.ColumnType, dtDocument.Mutable.Columns.Count));
                    RebindGrid();
                }
            }
        }

        // Structural ops change the grid's column/row shape, so a full rebind is required (OnEditApplied
        // alone only re-paints the existing cells). Rebuilds columns + rows from the mutated model.
        private void RebindGrid()
        {
            if (dtDocument == null) return;
            MutableDataTableDocument mutable = dtDocument.Mutable;
            var columns = mutable.Columns
                .Select(c => DatatableColumnFactory.Build(c.Name, c.ColumnType))
                .ToList();
            gridSurface.BindMutable(mutable, columns);
        }

        // ── Grid context menu (Remove/Move row + Remove/Move/Rename/ChangeType column) ──

        private void EnsureGridContextMenu()
        {
            if (gridContextMenu != null) return;
            gridContextMenu = new ContextMenuStrip();
            gridContextMenu.Items.Add("Remove row", null, (s, e) => RemoveSelectedRow());
            gridContextMenu.Items.Add("Move row up", null, (s, e) => MoveSelectedRow(-1));
            gridContextMenu.Items.Add("Move row down", null, (s, e) => MoveSelectedRow(+1));
            gridContextMenu.Items.Add(new ToolStripSeparator());
            gridContextMenu.Items.Add("Remove column", null, (s, e) => RemoveSelectedColumn());
            gridContextMenu.Items.Add("Move column left", null, (s, e) => MoveSelectedColumn(-1));
            gridContextMenu.Items.Add("Move column right", null, (s, e) => MoveSelectedColumn(+1));
            gridContextMenu.Items.Add("Rename column…", null, (s, e) => RenameSelectedColumn());
            gridContextMenu.Items.Add("Change column type…", null, (s, e) => ChangeSelectedColumnType());
            gridSurface.ContextMenuStrip = gridContextMenu;
        }

        private int SelectedRowIndex()
        {
            return gridSurface.CurrentCell != null ? gridSurface.CurrentCell.RowIndex : -1;
        }

        private int SelectedColumnIndex()
        {
            return gridSurface.CurrentCell != null ? gridSurface.CurrentCell.ColumnIndex : -1;
        }

        private void RemoveSelectedRow()
        {
            int r = SelectedRowIndex();
            if (controller == null || dtDocument == null || r < 0 || r >= dtDocument.Mutable.Rows.Count) return;
            controller.Apply(DatatableEditCommands.RemoveRow(dtDocument.Mutable.Rows[r]));
            RebindGrid();
        }

        private void MoveSelectedRow(int direction)
        {
            int r = SelectedRowIndex();
            if (controller == null || dtDocument == null || r < 0 || r >= dtDocument.Mutable.Rows.Count) return;
            MutableDataTableRow row = dtDocument.Mutable.Rows[r];
            controller.Apply(direction < 0
                ? DatatableEditCommands.MoveRowUp(row)
                : DatatableEditCommands.MoveRowDown(row));
            RebindGrid();
        }

        private void RemoveSelectedColumn()
        {
            int c = SelectedColumnIndex();
            if (controller == null || dtDocument == null || c < 0 || c >= dtDocument.Mutable.Columns.Count) return;
            if (!ConfirmColumnSafetyNet()) return;
            controller.Apply(DatatableEditCommands.RemoveColumn(dtDocument.Mutable.Columns[c]));
            RebindGrid();
        }

        private void MoveSelectedColumn(int direction)
        {
            int c = SelectedColumnIndex();
            if (controller == null || dtDocument == null || c < 0 || c >= dtDocument.Mutable.Columns.Count) return;
            if (!ConfirmColumnSafetyNet()) return;
            MutableDataTableColumn col = dtDocument.Mutable.Columns[c];
            controller.Apply(direction < 0
                ? DatatableEditCommands.MoveColumnLeft(col)
                : DatatableEditCommands.MoveColumnRight(col));
            RebindGrid();
        }

        private void RenameSelectedColumn()
        {
            int c = SelectedColumnIndex();
            if (controller == null || dtDocument == null || c < 0 || c >= dtDocument.Mutable.Columns.Count) return;
            MutableDataTableColumn col = dtDocument.Mutable.Columns[c];
            using (var dlg = new FormFourCcDialog("Rename column", col.Name))
            {
                dlg.Text = "Rename column";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    string newName = dlg.Value != null ? dlg.Value.Trim() : string.Empty;
                    if (newName.Length > 0)
                    {
                        controller.Apply(DatatableEditCommands.RenameColumn(col, newName));
                        RebindGrid();
                    }
                }
            }
        }

        private void ChangeSelectedColumnType()
        {
            int c = SelectedColumnIndex();
            if (controller == null || dtDocument == null || c < 0 || c >= dtDocument.Mutable.Columns.Count) return;
            MutableDataTableColumn col = dtDocument.Mutable.Columns[c];

            using (var dlg = new FormAddColumnDialog(System.Array.Empty<string>()))
            {
                dlg.Text = "Change column type";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                controller.Apply(DatatableEditCommands.ChangeColumnType(col, dlg.Result.ColumnType));
                RebindGrid();
            }

            // If the cascade flagged cells, surface the resolution modal from the controller state.
            if (controller.PendingCascadeContext != null)
            {
                ShowCascadeDialog();
            }
        }

        // ── D-02 column-reorder/delete safety-net (REUSE FormSaveConfirmDialog) ──

        private bool ConfirmColumnSafetyNet()
        {
            if (sessionSuppressColumnSafetyNet) return true;
            using (var dlg = new FormSaveConfirmDialog(
                heading: "Reorder or delete a column?",
                body: "This may break runtime consumers that read columns by index. Proceed?",
                acceptVerb: "Proceed",
                cancelVerb: "Cancel",
                showBackupCheckbox: true,
                backupCheckboxLabel: "Don't ask again this session"))
            {
                dlg.ShowDialog(this);
                if (dlg.Outcome == FormSaveConfirmDialog.ConfirmOutcome.Cancelled) return false;
                if (dlg.BackupRequested) sessionSuppressColumnSafetyNet = true;
                return true;
            }
        }

        // ── Resolve-cascade button + cascade modal ───────────────────────────

        private void OnResolveCascadeClicked(object sender, EventArgs e)
        {
            if (controller != null && controller.PendingCascadeContext != null)
            {
                ShowCascadeDialog();
            }
        }

        private void ShowCascadeDialog()
        {
            PendingTypeChangeCascade cascade = controller.PendingCascadeContext;
            if (cascade == null) return;

            MutableDataTableColumn col = cascade.ColumnIndex >= 0 && cascade.ColumnIndex < dtDocument.Mutable.Columns.Count
                ? dtDocument.Mutable.Columns[cascade.ColumnIndex]
                : null;
            if (col == null) return;

            using (var dlg = new FormTypeChangeCascadeDialog(
                col, cascade.NewType, new List<MutableDataTableCell>(cascade.NeedsReviewCellRefs)))
            {
                dlg.ShowDialog(this);
                if (dlg.Outcome == FormTypeChangeCascadeDialog.CascadeOutcome.Revert)
                {
                    // Roll the ChangeColumnType command back via the editor-local undo stack — the
                    // command's UndoOp clears PendingCascadeContext.
                    if (controller.CanUndo) controller.Undo();
                    RebindGrid();
                }
                else if (dlg.Outcome == FormTypeChangeCascadeDialog.CascadeOutcome.EditCellRequested)
                {
                    FocusCell(dlg.EditCellTarget);
                }
                else
                {
                    RebindGrid();
                }
            }
        }

        private void FocusCell(MutableDataTableCell target)
        {
            if (target == null || dtDocument == null) return;
            for (int r = 0; r < dtDocument.Mutable.Rows.Count; r++)
            {
                var cells = dtDocument.Mutable.Rows[r].Cells;
                for (int c = 0; c < cells.Count; c++)
                {
                    if (ReferenceEquals(cells[c], target) && r < gridSurface.Rows.Count && c < gridSurface.Columns.Count)
                    {
                        gridSurface.CurrentCell = gridSurface.Rows[r].Cells[c];
                        gridSurface.Focus();
                        return;
                    }
                }
            }
        }

        // ── Keyboard shortcuts (Ctrl+S / Ctrl+Z / Ctrl+Y) ────────────────────
        //
        // Ctrl+Z / Ctrl+Y are STUBBED to no-op in Plan 09-03 — the DatatableEditController (undo/
        // redo stack) ships in Plan 09-04. Ctrl+S is a default save fallback; Plan 09-05 wires the
        // actual save targets. Ctrl+F / Ctrl+H / F3 / Shift+F3 are deferred to Plan 09-06.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z))
            {
                if (controller != null && controller.CanUndo)
                {
                    controller.Undo();
                    RebindGrid();
                }
                return true;
            }
            if (keyData == (Keys.Control | Keys.Y))
            {
                if (controller != null && controller.CanRedo)
                {
                    controller.Redo();
                    RebindGrid();
                }
                return true;
            }
            if (keyData == (Keys.Control | Keys.S))
            {
                // ToDo Plan 09-05: trigger the default save target. No-op until save targets ship.
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ── Grid-binding commit-back (iter-2 item 1) ─────────────────────────
        //
        // Until Plan 09-04 ships DatatableEditController, cell edits commit to the model directly
        // without undo/redo; 09-04 swaps this direct setter for
        // controller.Apply(DatatableEditCommands.EditCellValue(cell, newValue)).
        //
        // CellEndEdit handles text + NumericUpDown columns; CellValueChanged handles CheckBox +
        // ComboBox columns which commit immediately.

        private void OnCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            // The hash-preview overlay is torn down when the hash cell leaves edit mode.
            DisposeHashEditor();

            if (dtDocument == null) return;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            MutableDataTableColumn column = dtDocument.Mutable.Columns[e.ColumnIndex];
            // CheckBox + ComboBox commit through CellValueChanged; skip them here.
            if (column.ColumnType.Type == DataType.Bool || column.ColumnType.Type == DataType.Enum) return;

            CommitCell(e.RowIndex, e.ColumnIndex);
        }

        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (dtDocument == null) return;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            MutableDataTableColumn column = dtDocument.Mutable.Columns[e.ColumnIndex];
            // Only CheckBox + ComboBox commit-immediately columns route here; text columns commit
            // via CellEndEdit (above) to avoid a per-keystroke commit storm.
            if (column.ColumnType.Type != DataType.Bool && column.ColumnType.Type != DataType.Enum) return;

            CommitCell(e.RowIndex, e.ColumnIndex);
        }

        // Resolve the backing cell and write the edited grid value to the model. For DT_HashString
        // the typed SOURCE STRING is hashed (item 5); CheckBox writes its bool; ComboBox writes the
        // selected enum label resolved against EnumMap; numeric columns coerce via the column type.
        private void CommitCell(int rowIndex, int columnIndex)
        {
            MutableDataTableRow row = dtDocument.Mutable.Rows[rowIndex];
            MutableDataTableCell cell = row.Cells[columnIndex];
            MutableDataTableColumn column = dtDocument.Mutable.Columns[columnIndex];
            DataTableColumnType ct = column.ColumnType;

            object gridValue = gridSurface.Rows[rowIndex].Cells[columnIndex].Value;
            string raw = gridValue == null ? string.Empty : gridValue.ToString();

            // Plan 09-04: route the edit through controller.Apply(EditCellValue) so it joins the undo/
            // redo stack (replaces the Plan 09-03 direct cell.Value setter). The EditCellValue command
            // is a no-op-friendly write — an equal value preserves the original slice + clean state.
            DataTableCellValue newValue;
            switch (ct.Type)
            {
                case DataType.Bool:
                {
                    bool isChecked = gridValue is bool b && b;
                    newValue = DataTableCellValue.FromInt(isChecked ? 1 : 0);
                    break;
                }
                case DataType.HashString:
                {
                    // item 5: the user types a SOURCE STRING; write its int32 hash. The source
                    // string is NOT persisted (only the int32 is on disk).
                    uint hash = DataTableHashCrc.Compute(raw);
                    int stored = unchecked((int)hash);
                    newValue = DataTableCellValue.FromInt(stored);
                    // Reflect the stored int32 back into the grid display (item 5 default display).
                    gridSurface.Rows[rowIndex].Cells[columnIndex].Value =
                        stored.ToString(CultureInfo.InvariantCulture);
                    break;
                }
                default:
                {
                    // DT_Int/DT_Float (NumericUpDown decimal coerced), DT_Enum (label → int),
                    // DT_String/DT_Comment/DT_PackedObjVars/DT_BitVector via the column coercion.
                    DataTableCellValue coerced;
                    if (ct.TryCoerceCellValue(raw, out coerced))
                    {
                        newValue = coerced;
                    }
                    else
                    {
                        lblStatus.Text = "Value \"" + raw + "\" is not valid for column \"" + column.Name + "\".";
                        lblStatus.ForeColor = Color.Red;
                        return;
                    }
                    break;
                }
            }

            if (controller != null)
            {
                controller.Apply(DatatableEditCommands.EditCellValue(cell, newValue));
            }
            else
            {
                cell.Value = newValue;
            }

            gridSurface.RefreshMutable(dtDocument.Mutable);
            UpdateDirtyVisuals();
            UpdateCounters();
        }

        // ── DT_Int / DT_Float editor swap-in ─────────────────────────────────

        private void OnEditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            if (dtDocument == null) return;
            int colIndex = gridSurface.CurrentCell != null ? gridSurface.CurrentCell.ColumnIndex : -1;
            if (colIndex < 0 || colIndex >= dtDocument.Mutable.Columns.Count) return;
            DataTableColumnType ct = dtDocument.Mutable.Columns[colIndex].ColumnType;

            var numeric = e.Control as DatatableNumericUpDownEditingControl;
            if (numeric == null) return;

            if (ct.Type == DataType.Int)
            {
                numeric.DecimalPlaces = 0;
                numeric.Increment = 1m;
                numeric.Minimum = int.MinValue;
                numeric.Maximum = int.MaxValue;
            }
            else if (ct.Type == DataType.Float)
            {
                numeric.DecimalPlaces = 6;
                numeric.Increment = 0.1m;
                // decimal cannot represent float.MinValue/MaxValue; clamp to decimal range.
                numeric.Minimum = decimal.MinValue;
                numeric.Maximum = decimal.MaxValue;
            }
        }

        // ── DT_HashString floating hash preview ──────────────────────────────

        private void OnCellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (dtDocument == null) return;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            DataTableColumnType ct = dtDocument.Mutable.Columns[e.ColumnIndex].ColumnType;
            if (ct.Type != DataType.HashString) return;

            DisposeHashEditor();
            Rectangle cellRect = gridSurface.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
            hashEditor = new DatatableHashStringEditor(gridSurface, gridSurface, cellRect);

            // Subscribe to the editing control's text changes so the preview is live as the user
            // types a source string. The editing control is created lazily by the grid; hook it on
            // the next EditingControlShowing-equivalent moment via the current cell's control.
            activeHashEditingControl = gridSurface.EditingControl as TextBox;
            if (activeHashEditingControl != null)
            {
                hashEditor.Update(activeHashEditingControl.Text);
                activeHashEditingControl.TextChanged += OnHashEditingControlTextChanged;
            }
        }

        private void OnHashEditingControlTextChanged(object sender, EventArgs e)
        {
            if (hashEditor == null) return;
            var tb = sender as TextBox;
            if (tb != null) hashEditor.Update(tb.Text);
        }

        private void DisposeHashEditor()
        {
            if (activeHashEditingControl != null)
            {
                activeHashEditingControl.TextChanged -= OnHashEditingControlTextChanged;
                activeHashEditingControl = null;
            }
            if (hashEditor != null)
            {
                hashEditor.Dispose();
                hashEditor = null;
            }
        }

        // ── Save▾ drop-down (items DISABLED + tooltip in Plan 09-03) ─────────

        // Plan 09-05: the five Save▾ items now carry click handlers dispatching to DatatableSaveTargets.
        // Their ENABLED state is composed by RefreshSaveMenuEnabledState (provenance gate ON TOP of Plan
        // 09-04's NeedsReview gate). Patch live client stays disabled (CF-03) with the inherited tooltip.
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
            miPatchLive.Enabled = false; // CF-03 — Mode 3 disabled in Phase 9.
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

        // Plan 09-05: composes the PROVENANCE gate (OpenSource pattern-match) ON TOP of Plan 09-04's
        // NeedsReview gate. Each Save▾ item's final enabled state is
        // `provenanceAllowsThisMode && !blockedByCascade && !saveInFlight` (UI-SPEC R-04 + round-2
        // MEDIUM 5 escape-hatch posture). When the cascade blocks, the locked R-04 tooltip wins on
        // EACH item. Save As… stays enabled on any loaded document (incl. Unknown) unless a cascade
        // blocks — the user's explicit escape hatch. Patch live client stays disabled (CF-03).
        private void RefreshSaveMenuEnabledState()
        {
            bool hasDoc = dtDocument != null;
            bool blockedByCascade = controller != null && controller.NeedsReviewCount > 0;
            string cascadeTooltip = blockedByCascade
                ? ("Resolve " + controller.NeedsReviewCount + " cell(s) that need review before saving.")
                : null;

            bool isLooseFile = Source is OpenSource.LooseFile;
            bool isTreArchive = Source is OpenSource.TreArchive;
            bool isUnknown = Source is OpenSource.Unknown;

            if (miSaveInPlace != null)
            {
                miSaveInPlace.Enabled = hasDoc && isLooseFile && !blockedByCascade && !saveInFlight;
                miSaveInPlace.ToolTipText = blockedByCascade
                    ? cascadeTooltip
                    : (isLooseFile ? "" : "Cannot save in place — file came from .tre or unknown source. Use Save as loose override or Save As.");
            }
            if (miSaveLooseOverride != null)
            {
                miSaveLooseOverride.Enabled = hasDoc && (isLooseFile || isTreArchive) && !blockedByCascade && !saveInFlight;
                miSaveLooseOverride.ToolTipText = blockedByCascade
                    ? cascadeTooltip
                    : ((isLooseFile || isTreArchive) ? "" : "Cannot resolve archive record — use Save As to write to a chosen file.");
            }
            if (miSaveAs != null)
            {
                // ALWAYS enabled (escape hatch — round-2 MEDIUM 5) unless a cascade blocks.
                miSaveAs.Enabled = hasDoc && !blockedByCascade && !saveInFlight;
                miSaveAs.ToolTipText = blockedByCascade ? cascadeTooltip : "Save the current edits to a path you choose.";
            }
            if (miPatchLive != null)
            {
                miPatchLive.Enabled = false; // CF-03 — disabled inherited.
                miPatchLive.ToolTipText = "Live patch requires opening from client memory — not wired in this phase.";
            }
            if (miRepackTre != null)
            {
                miRepackTre.Enabled = hasDoc && isTreArchive && !blockedByCascade && !saveInFlight;
                miRepackTre.ToolTipText = blockedByCascade
                    ? cascadeTooltip
                    : (isTreArchive ? "" : "Open from a packed .tre to repack the source archive.");
            }

            btnSave.Enabled = hasDoc && !blockedByCascade && !saveInFlight && (isLooseFile || isTreArchive || isUnknown);
        }

        private void OnSaveButtonClick(object sender, EventArgs e)
        {
            // Plan 09-05: anchor the Save▾ drop-down at the bottom of the Save button.
            if (saveMenu == null) return;
            saveMenu.Show(btnSave, new Point(0, btnSave.Height));
        }

        // ── Save▾ click handlers (Plan 09-05) ────────────────────────────────
        //
        // Each handler builds DTII bytes via DatatableSaveTargets (the < 100-line composition shim
        // over Phase 8's IffSaveTargets / TreRepackSaveTarget) and, on success, calls
        // controller.MarkSaved() (the Plan 09-04 seam — iter-2 item 8) to reset the dirty baseline +
        // clear the ● glyph, then routes the reload via ClientReloadDispatcher.Dispatch.

        private async void OnSaveInPlaceClick(object sender, EventArgs e)
        {
            if (dtDocument == null) return;
            if (!(Source is OpenSource.LooseFile))
            {
                SetSaveFailure("Cannot save in place — file came from .tre or unknown source. Use Save as loose override or Save As.");
                return;
            }
            await DoFileSaveAsync(() => DatatableSaveTargets.SaveInPlace(dtDocument.Mutable, Source), "in place");
        }

        private async void OnSaveLooseOverrideClick(object sender, EventArgs e)
        {
            if (dtDocument == null) return;
            string clientRoot = ResolveClientRoot();
            if (string.IsNullOrEmpty(clientRoot))
            {
                SetSaveFailure("Could not locate the client root — use Save As… and we'll remember the directory.");
                return;
            }
            string subDir = ini.GetString("DatatableEditor", "looseOverrideDir");
            if (string.IsNullOrEmpty(subDir))
            {
                // Phase 8 Open Q2 user-picks-path-on-first-save posture: best-guess default subdir +
                // a one-time hint so the user knows where the override landed and how to change it.
                subDir = "loose";
                lblStatus.Text = "Saving to " + subDir
                    + ". Change the loose-override directory in [DatatableEditor] looseOverrideDir if needed.";
                lblStatus.ForeColor = Colors.Font();
            }
            await DoFileSaveAsync(
                () => DatatableSaveTargets.SaveLooseOverride(dtDocument.Mutable, Source, clientRoot, subDir),
                "loose override");
        }

        private async void OnSaveAsClick(object sender, EventArgs e)
        {
            if (dtDocument == null) return;
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Save datatable as…";
                sfd.Filter = "Datatable files (*.tab)|*.tab|All files (*.*)|*.*";
                sfd.FileName = !string.IsNullOrEmpty(displayName) ? displayName : "untitled.tab";
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                string path = sfd.FileName;
                await DoFileSaveAsync(() => DatatableSaveTargets.SaveToPath(dtDocument.Mutable, path), "save-as");
            }
        }

        // CF-03: Mode 3 (in-memory live patch) is DISABLED in Phase 9. The menu item ships disabled
        // with the inherited tooltip; this handler is defensive (reachable only if the item were
        // somehow enabled) — no-op with the honest copy.
        private void OnPatchLiveClientClick(object sender, EventArgs e)
        {
            SetSaveFailure("Live patch is disabled in this phase.");
        }

        private async void OnRepackTreClick(object sender, EventArgs e)
        {
            if (dtDocument == null) return;
            var ta = Source as OpenSource.TreArchive;
            if (ta == null)
            {
                // Defensive — the item should be disabled in this state.
                SetSaveFailure("Open from a packed .tre to repack the source archive.");
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
                result = await DatatableSaveTargets.RepackIntoSourceTre(dtDocument.Mutable, ta, backupRequested)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetSaveFailure("Repack failed: " + ex.Message + " Your edits are kept in the editor.");
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
                    // No MarkSaved — nothing was written.
                    lblStatus.Text = "Client holds the archive — try Save as loose override instead.";
                    lblStatus.ForeColor = Color.Red;
                    break;

                case TreRepackSaveTarget.TreRepackResult.Failed:
                default:
                    // The Failed outcome also covers the Phase 8 WR-06 V6000 enumerate-only refusal
                    // (TreWriter.Repack throws NotSupportedException → save target returns Failed).
                    lblStatus.Text = "Repack failed — your edits are retained. If this is a V6000 (encrypted) archive, use Save as loose override.";
                    lblStatus.ForeColor = Color.Red;
                    break;
            }

            RefreshSaveMenuEnabledState();
            RefreshReloadButtonState();
        }

        // Shared file-save orchestration (modes 1/2/3): set the saveInFlight barrier (disables Save▾ +
        // Reload while the Task is in flight — MEDIUM-9), surface the Saving/Saved/Save-failed copy,
        // and on success call controller.MarkSaved() + route the reload dispatch.
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
                // iter-2 item 8: reset the dirty baseline + clear the ● glyph (MarkSaved raises
                // EditApplied → OnEditApplied → UpdateDirtyVisuals).
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

        private void SetSaveFailure(string message)
        {
            lblStatus.Text = message;
            lblStatus.ForeColor = Color.Red;
        }

        // Routes the just-saved DTII asset through Phase 8's tiered reload dispatcher. The DTII case
        // classifies as PendingNextSceneChange (tier-(b) per CF-05); the dispatch is for the routing-
        // table audit trail — the user-facing reload copy stays the locked CF-05 wording (OnReloadClicked).
        private void DispatchReload(string savedPath)
        {
            if (string.IsNullOrEmpty(savedPath)) return;
            try
            {
                ClientReloadDispatcher.Dispatch(savedPath, "DTII");
            }
            catch
            {
                // Dispatch gates on Game.IsRunning internally; never let a binding hiccup tear down
                // the save-success path.
            }
        }

        // ── Public entry points (Plan 09-05 D-10.2 / D-10.3 hand-offs) ───────

        /// <summary>
        /// TRE Browser hand-off (D-10.2). Reads the resolved payload as a typed datatable and binds it
        /// with TreArchive (or Unknown on degraded resolve) provenance — mirrors
        /// <c>FormIffEditor.OpenFromTreEntry</c> with the typed wrap swapped in.
        /// </summary>
        public void OpenFromTreEntry(byte[] payload, string resolvedArchivePath, string logicalPath, long archiveLocalOffset)
        {
            if (payload == null)
            {
                SetSaveFailure("TRE entry has no payload to open.");
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
                DataTableDocument dtDoc = DataTableDocument.FromIff(mIff);
                OpenSource src = TreRecordIndexResolver.ResolveOrUnknown(resolvedArchivePath, archiveLocalOffset, logicalPath);
                string name = logicalPath != null ? Path.GetFileName(logicalPath) : Path.GetFileName(resolvedArchivePath ?? "");
                LoadDocument(dtDoc, src, name);
                if (src is OpenSource.TreArchive)
                {
                    lblStatus.Text = "Opened " + name + " from " + Path.GetFileName(resolvedArchivePath ?? "");
                    lblStatus.ForeColor = Colors.Font();
                }
                else
                {
                    lblStatus.Text = "Opened " + name + " — record index unresolved; use Save As to write to a chosen file.";
                    lblStatus.ForeColor = Colors.Font();
                }
            }
            catch (Exception ex)
            {
                SetSaveFailure("TRE hand-off failed: " + ex.Message);
            }
        }

        /// <summary>
        /// IFF Editor hand-off (D-10.3). Wraps the IFF Editor's in-memory <see cref="MutableIffDocument"/>
        /// as a typed datatable WITHOUT re-parsing bytes (efficient hand-off — the IFF Editor passes its
        /// existing mutable doc + Source directly).
        /// </summary>
        public void OpenFromMutableIff(MutableIffDocument mutIff, OpenSource source, string displayName)
        {
            if (mutIff == null)
            {
                SetSaveFailure("IFF hand-off has no document to open.");
                return;
            }
            try
            {
                DataTableDocument dtDoc = DataTableDocument.FromIff(mutIff);
                LoadDocument(dtDoc, source ?? OpenSource.Unknown.Instance, displayName);
                lblStatus.Text = "Opened " + (displayName ?? "datatable") + " (typed view)";
                lblStatus.ForeColor = Colors.Font();
            }
            catch (Exception ex)
            {
                SetSaveFailure("Could not switch to the typed datatable view: " + ex.Message);
            }
        }

        // Resolves the client install root (process-module primary, then GetWorkingDirectory(), then
        // the [TreBrowser] clientDir ini fallback) — mirrors FormIffEditor.ResolveClientRoot.
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

        // Reload-button state: disabled while a save Task is in flight (MEDIUM-9). Otherwise the button
        // stays clickable — OnReloadClicked surfaces the CF-05 locked copy regardless of live-client.
        private void RefreshReloadButtonState()
        {
            btnReload.Enabled = !saveInFlight;
        }

        // ── Dirty visuals + counters ─────────────────────────────────────────

        private const string BaseTitle = "Datatable Editor";

        private void SetTitle(string prefix)
        {
            this.Text = string.IsNullOrEmpty(prefix) ? BaseTitle : prefix + " " + BaseTitle;
            this.Invalidate();
        }

        private void UpdateDirtyVisuals()
        {
            // Plan 09-03 has no controller, so dirty roll-up reads from the document model directly.
            bool dirty = dtDocument != null && dtDocument.Mutable.IsDirty;
            lblDirty.Text = dirty ? "Unsaved changes" : "";
            SetTitle(dirty ? "●" : null);
        }

        private void UpdateCounters()
        {
            if (dtDocument == null)
            {
                lblCounters.Text = "0 rows · 0 cols";
                lblCounters.ForeColor = Colors.FontDisabled();
                return;
            }

            MutableDataTableDocument m = dtDocument.Mutable;
            int dirtyRows = 0;
            for (int r = 0; r < m.Rows.Count; r++)
            {
                if (m.Rows[r].IsDirty) dirtyRows++;
            }

            int needsReview = controller != null ? controller.NeedsReviewCount : 0;

            if (dirtyRows == 0 && needsReview == 0)
            {
                lblCounters.Text = m.Rows.Count + " rows · " + m.Columns.Count + " cols";
                lblCounters.ForeColor = Colors.FontDisabled();
            }
            else
            {
                lblCounters.Text = m.Rows.Count + " rows · " + m.Columns.Count + " cols · "
                                   + needsReview + " needs review · " + dirtyRows + " dirty";
                lblCounters.ForeColor = Colors.Font();
            }
        }

        // ── Open… ────────────────────────────────────────────────────────────

        private void OnOpenClicked(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Open datatable…";
                ofd.Filter = "Datatable files (*.tab;*.iff)|*.tab;*.iff|All files (*.*)|*.*";
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
                DataTableDocument doc = DataTableDocument.FromIff(mutableIff);

                LoadDocument(doc, new OpenSource.LooseFile(path), Path.GetFileName(path));
                lblStatus.Text = "Opened " + Path.GetFileName(path);
                lblStatus.ForeColor = Colors.Font();
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Open failed: " + ex.Message + " Your edits are kept in the editor — try another save target.";
                lblStatus.ForeColor = Color.Red;
            }
        }

        // ── Reload-in-client (CF-05 candor — NO reload hook for datatables) ──

        private void OnReloadClicked(object sender, EventArgs e)
        {
            // CF-05 LOCKED: datatables re-resolve only on the next scene change. The user-facing copy
            // STAYS this locked wording (RESEARCH § Anti-Patterns Pitfall 10 + CF-05 lock).
            //
            // Plan 09-05: when a save has happened this session, delegate to ClientReloadDispatcher.
            // Dispatch for the routing-table audit trail — it routes the DTII case to
            // PendingNextSceneChange anyway (no fresh binding call, no scene-setup trigger). If
            // nothing has been saved yet (lastSavedPath == null), keep the candor copy unchanged.
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

            lblStatus.Text = "Datatables re-resolve on the next scene change. Trigger one via TJT's chat-command load.";
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

        // ── Singleton hide-not-dispose (MANDATORY FROM COMMIT 1) ─────────────

        private void FormDatatableEditor_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                ini.AddSetting("DatatableEditor", "width", Width.ToString(), UtINI.Value.Types.VtInt);
                ini.AddSetting("DatatableEditor", "height", Height.ToString(), UtINI.Value.Types.VtInt);
                ini.Save();
            }
            catch
            {
                // Persistence is best-effort; never block close.
            }

            // The CloseReason DECISION lives in the framework helper so the xUnit guard can exercise
            // it without instantiating the form. On user-initiated close, hide instead of disposing
            // so subsequent Show() calls re-Show this same singleton instance (Phase 8 b899504).
            if (SingletonFormClosePolicy.ShouldHideInsteadOfDispose(e.CloseReason))
            {
                e.Cancel = true;
                Hide();
            }
        }
    }
}
