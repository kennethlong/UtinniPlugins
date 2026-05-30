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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using TJT.Saving;
using UtinniCore.Utinni;
using UtinniCoreDotNet.Editing;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.Formats.StringTable;
using UtinniCoreDotNet.Formats.Tre;
using UtinniCoreDotNet.PluginFramework;
using UtinniCoreDotNet.UI;
using UtinniCoreDotNet.UI.Controls;
using UtinniCoreDotNet.UI.Forms;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Forms
{
    /// <summary>
    /// String-table (<c>.stf</c>) editor window — a resizable <see cref="UtinniForm"/> registered ONCE
    /// via the plugin's <c>GetForms()</c> list (the Wave-1 editors are forms, not the fixed 417px
    /// SubPanel). A STRICTLY SIMPLER sibling of <see cref="FormDatatableEditor"/>: a two-column
    /// (Key + Text) <see cref="ThemedDataGridView"/> over a <see cref="MutableStringTableDocument"/>
    /// with the four D-01 T4 verbs routed through 10-01's <see cref="StringTableEditController"/> — no
    /// per-type cell widgets, no type-change cascade, no column structural ops.
    ///
    /// <para><b>Staging:</b> 10-03 shipped the host + grid + T4 mutation + the <c>Open…</c> entry point
    /// + the read-only <c>id</c>-column toggle. 10-04 wired the bulk/translation features: Find/Replace
    /// (key + text, regex opt-in), the live filter row (Ctrl+L, view-only Row.Visible), view-only column
    /// sort (locked tooltip), and CSV/TSV import (preview modal + single-transaction apply) / CSV + PO
    /// export. 10-05 wired the disk + reload surface: Save▾ modes 1/2/4 (Save-in-place / loose override /
    /// Save-As / repack — mode 3 live-patch stays disabled per CF-03) via <see cref="StringTableSaveTargets"/>
    /// composing at the bytes layer, <see cref="OpenFromTreEntry"/> for the D-04 TRE Browser hand-off,
    /// the tier-(b) reload dispatch (badge stays the locked CF-05 copy), and the dirty-discard prompt.</para>
    ///
    /// <para><b>Unicode fidelity (SC4 non-behavior):</b> the Text column commits UTF-16LE verbatim —
    /// no smart-quote / ellipsis / dash / typo "fix". The editing TextBox has OS auto-complete OFF and
    /// keeps its default IME mode (so legitimate CJK composition still works); the byte-exact guarantee
    /// is proven by the 10-01/10-02 gates.</para>
    ///
    /// <para><b>Singleton hide-not-dispose:</b> <see cref="FormStringTableEditor_FormClosing"/>
    /// delegates its CloseReason decision to
    /// <see cref="SingletonFormClosePolicy.ShouldHideInsteadOfDispose"/> (Phase 9 helper, reused).</para>
    /// </summary>
    public partial class FormStringTableEditor : UtinniForm, IEditorForm
    {
        private readonly IEditorPlugin editorPlugin;
        private readonly UtINI ini;
        private readonly ToolTip toolTip = new ToolTip();

        private MutableStringTableDocument document;

        // Editor-local undo/redo controller (10-01). Null until LoadDocument binds a document; all four
        // T4 verbs route through controller.Apply so they join the editor-local stack (CF-04).
        private StringTableEditController controller;

        // True while RebindGrid is repopulating the grid programmatically, so the commit-back handlers
        // (CellEndEdit) ignore the synthetic value writes.
        private bool rebinding;

        private string displayName;

        /// <summary>
        /// Provenance descriptor for the currently loaded document. Plan 10-05's open paths set this to
        /// gate the Save▾ modes; defaults to <see cref="OpenSource.Unknown"/>.
        /// </summary>
        public OpenSource Source { get; set; }

        // ── Plan 10-04: Find/Replace pane state (D-03a) ──────────────────────
        // Matches are (gridRowIndex, columnIndex) over the CURRENT grid rows (visual). Navigation +
        // commit-back both key off the grid row's Tag entry, so a view-only sort never corrupts a write.
        private readonly List<KeyValuePair<int, int>> findMatches = new List<KeyValuePair<int, int>>();
        private int currentMatchIndex = -1;
        private bool findMatchCase;
        private bool findRegex;
        private Timer findDebounceTimer;

        // ── Plan 10-04: live filter state (D-03c) ────────────────────────────
        private Timer filterDebounceTimer;

        // Plan 10-05: save-in-flight barrier (Phase 8 MEDIUM-9 stale-bytes reload race). True while a
        // Save▾ Task is awaiting; disables Save▾ + Reload until the await completes. lastSavedPath drives
        // the reload-dispatch + the OnReloadClicked routing.
        private bool saveInFlight;
        private string lastSavedPath;

        // Save▾ drop-down items (10-05 wires the click handlers + provenance gate).
        private UtinniContextMenuStrip saveMenu;
        private ToolStripMenuItem miSaveInPlace;
        private ToolStripMenuItem miSaveLooseOverride;
        private ToolStripMenuItem miSaveAs;
        private ToolStripMenuItem miPatchLive;
        private ToolStripMenuItem miRepackTre;

        public FormStringTableEditor(IEditorPlugin editorPlugin)
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

            Width = ini.GetInt("StringTableEditor", "width");
            Height = ini.GetInt("StringTableEditor", "height");

            // Theme via Colors.*() accessors only — no raw ARGB literals.
            toolbar.BackColor = Colors.Primary();
            sep1.BackColor = Colors.Primary();
            sep2.BackColor = Colors.Primary();
            sep3.BackColor = Colors.Primary();
            sep4.BackColor = Colors.Primary();
            sep5.BackColor = Colors.Primary();
            pnlFindReplace.BackColor = Colors.Primary();
            pnlFilter.BackColor = Colors.Primary();
            pnlStatus.BackColor = Colors.Primary();
            pnlCounters.BackColor = Colors.Primary();
            lblDirty.ForeColor = Colors.Secondary(); // accent for unsaved-changes marker
            lblStatus.ForeColor = Colors.Font();
            lblReloadBadge.ForeColor = Colors.Font();

            // The read-only id column's recessed read-only cue (PrimaryShadow bg / FontDisabled fg).
            colId.DefaultCellStyle.BackColor = Colors.PrimaryShadow();
            colId.DefaultCellStyle.ForeColor = Colors.FontDisabled();

            // Default provenance is Unknown (Plan 10-05 open paths override).
            Source = OpenSource.Unknown.Instance;

            // Tooltips.
            toolTip.SetToolTip(btnSave, "Save the current string table (choose a target).");
            toolTip.SetToolTip(btnImportCsv, "Import a CSV/TSV delta (preview before apply).");
            toolTip.SetToolTip(btnExportCsv, "Export the current string table to CSV/TSV.");
            toolTip.SetToolTip(btnExportPo, "Export the current string table to PO/gettext.");
            toolTip.SetToolTip(btnFind, "Find (Ctrl+F)");
            toolTip.SetToolTip(btnReplace, "Replace (Ctrl+H)");
            toolTip.SetToolTip(btnFilter, "Filter visible rows over key + text (Ctrl+L).");
            toolTip.SetToolTip(btnReload, "Reloads on next scene change.");
            toolTip.SetToolTip(tglShowId, "Show the machine-managed id column (read-only diagnostic).");
            toolTip.SetToolTip(btnFindPrev, "Find previous (Shift+F3)");
            toolTip.SetToolTip(btnFindNext, "Find next (F3)");
            toolTip.SetToolTip(btnFindClose, "Close (Esc)");
            toolTip.SetToolTip(btnClearFilter, "Clear filter (Esc)");

            // ENABLED, functional toolbar buttons in Plan 10-03: Open… + the T4 verbs + Show id.
            btnOpen.Click += OnOpenClicked;
            btnSave.Click += OnSaveButtonClick;
            btnUndo.Click += OnUndoClicked;
            btnRedo.Click += OnRedoClicked;
            btnAddEntry.Click += OnAddEntryClicked;
            btnRemoveEntry.Click += OnRemoveEntryClicked;
            tglShowId.CheckedChanged += OnShowIdToggled;

            // Plan 10-04: Find/Replace + Filter + CSV/PO wiring (enabled on LoadDocument).
            btnFind.Click += (s, e) => ToggleFindPane(false);
            btnReplace.Click += (s, e) => ToggleFindPane(true);
            btnFindPrev.Click += (s, e) => FindPrev();
            btnFindNext.Click += (s, e) => FindNext();
            btnFindClose.Click += (s, e) => CollapseFindPane();
            btnReplaceOne.Click += OnReplaceOneClicked;
            btnReplaceAll.Click += OnReplaceAllClicked;
            tglMatchCase.CheckedChanged += (s, e) => { findMatchCase = tglMatchCase.Checked; RecomputeMatches(); };
            tglRegex.CheckedChanged += (s, e) => { findRegex = tglRegex.Checked; RecomputeMatches(); };
            findDebounceTimer = new Timer { Interval = 200 };
            findDebounceTimer.Tick += (s, e) => { findDebounceTimer.Stop(); RecomputeMatches(); };
            txtFind.TextChanged += (s, e) => { findDebounceTimer.Stop(); findDebounceTimer.Start(); };

            btnFilter.Click += (s, e) => ToggleFilterRow();
            btnClearFilter.Click += (s, e) => { txtFilter.Text = ""; ApplyFilter(); };
            filterDebounceTimer = new Timer { Interval = 250 };
            filterDebounceTimer.Tick += (s, e) => { filterDebounceTimer.Stop(); ApplyFilter(); };
            txtFilter.TextChanged += (s, e) => { filterDebounceTimer.Stop(); filterDebounceTimer.Start(); };

            btnImportCsv.Click += OnImportCsvClicked;
            btnExportCsv.Click += OnExportCsvClicked;
            btnExportPo.Click += OnExportPoClicked;

            // Plan 10-05: reload dispatch (badge stays the locked CF-05 copy).
            btnReload.Click += OnReloadClicked;

            // Grid commit-back + cell-state visuals.
            gridSurface.CellValidating += OnCellValidating;
            gridSurface.CellEndEdit += OnCellEndEdit;
            gridSurface.EditingControlShowing += OnEditingControlShowing;
            gridSurface.CellFormatting += OnCellFormatting;

            // Save▾ drop-down (items DISABLED + tooltip; wired in Plan 10-05).
            BuildSaveMenu();

            SetTitle(null);
            UpdateUndoRedoState();
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
                if (form.GetType() == typeof(FormStringTableEditor))
                {
                    form.Activate();
                    return null;
                }
            }
            FormStringTableEditor newForm = new FormStringTableEditor(plugin);
            newForm.Show();
            parentChildren.Add(newForm);
            return newForm;
        }

        // ── Settings ─────────────────────────────────────────────────────────

        private void CreateSettings()
        {
            ini.AddSetting("StringTableEditor", "width", "1000", UtINI.Value.Types.VtInt);
            ini.AddSetting("StringTableEditor", "height", "720", UtINI.Value.Types.VtInt);
            ini.AddSetting("StringTableEditor", "findReplaceVisible", "0", UtINI.Value.Types.VtBool);
            ini.AddSetting("StringTableEditor", "filterVisible", "0", UtINI.Value.Types.VtBool);
            ini.AddSetting("StringTableEditor", "showIdColumn", "0", UtINI.Value.Types.VtBool);
            ini.AddSetting("StringTableEditor", "looseOverrideDir", "", UtINI.Value.Types.VtString);
        }

        // ── Load ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Binds a <see cref="MutableStringTableDocument"/> to the grid surface and wires the editor-local
        /// undo/redo controller. Populates one grid row per entry (Key + Text [+ hidden read-only id]).
        /// </summary>
        public void LoadDocument(MutableStringTableDocument mutable, OpenSource source, string displayName)
        {
            if (mutable == null) throw new ArgumentNullException("mutable");
            this.document = mutable;
            this.Source = source ?? OpenSource.Unknown.Instance;
            this.displayName = displayName;
            this.lastSavedPath = null;

            if (controller != null) controller.EditApplied -= OnEditApplied;
            controller = new StringTableEditController(mutable);
            controller.EditApplied += OnEditApplied;

            RebindGrid();

            // The T4 verbs + Show id + the Plan 10-04 bulk features become functional once a doc is bound.
            btnAddEntry.Enabled = true;
            btnRemoveEntry.Enabled = true;
            tglShowId.Enabled = true;
            btnFind.Enabled = true;
            btnReplace.Enabled = true;
            btnFilter.Enabled = true;
            btnImportCsv.Enabled = true;
            btnExportCsv.Enabled = true;
            btnExportPo.Enabled = true;
            btnReload.Enabled = true; // Plan 10-05: reload dispatch (badge stays CF-05 locked).

            // D-03c view-only sort: the LOCKED hover tooltip on both sortable columns.
            colKey.ToolTipText = "View order only — save serializes strings by id and names alphabetically.";
            colText.ToolTipText = "View order only — save serializes strings by id and names alphabetically.";

            ResetFindState();
            txtFilter.Text = "";

            // Restore the persisted id-column visibility (setting Checked flips colId.Visible).
            tglShowId.Checked = ini.GetBool("StringTableEditor", "showIdColumn");
            colId.Visible = tglShowId.Checked;

            lblEmptyState.Visible = false;
            lblReloadBadge.Visible = true; // CF-05 locked copy from the moment a file loads.

            UpdateUndoRedoState();
            RefreshSaveMenuEnabledState();
            UpdateDirtyVisuals();
            UpdateCounters();
        }

        // ── Open… ────────────────────────────────────────────────────────────

        private void OnOpenClicked(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Open string table…";
                ofd.Filter = "String tables (*.stf)|*.stf|All files (*.*)|*.*";
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
                StringTableDocument doc = StringTableDocument.FromBytes(bytes);

                LoadDocument(doc.Mutable, new OpenSource.LooseFile(path), Path.GetFileName(path));

                lblStatus.Text = doc.Warnings.Count > 0
                    ? ("Opened " + Path.GetFileName(path) + " — " + doc.Warnings[0])
                    : ("Opened " + Path.GetFileName(path));
                lblStatus.ForeColor = Colors.Font();
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Open failed: " + ex.Message;
                lblStatus.ForeColor = Color.Red;
            }
        }

        // ── Grid bind / rebind ───────────────────────────────────────────────

        // Rebuilds every grid row from the model (full resync). Used on load, after every structural T4
        // op (add / remove), and after every undo/redo (F12 — the grid must exactly mirror the model, not
        // merely refresh dirty visuals). Each row's Tag holds its backing entry reference, so commit-back
        // + cell-state visuals never depend on a fragile row index. Runs OUTSIDE any CellEndEdit so it
        // never triggers a reentrant grid mutation.
        private void RebindGrid()
        {
            if (document == null) return;

            rebinding = true;
            gridSurface.SuspendLayout();
            try
            {
                if (gridSurface.IsCurrentCellInEditMode) gridSurface.CancelEdit();
                gridSurface.Rows.Clear();

                foreach (MutableStringTableEntry entry in document.Entries)
                {
                    int rowIndex = gridSurface.Rows.Add();
                    DataGridViewRow row = gridSurface.Rows[rowIndex];
                    row.Tag = entry;
                    row.Cells[colId.Index].Value = entry.Id;
                    row.Cells[colKey.Index].Value = entry.Name;
                    row.Cells[colText.Index].Value = entry.Text;
                }
            }
            finally
            {
                gridSurface.ResumeLayout();
                rebinding = false;
            }

            UpdateRowHeaderGlyphs();

            // A structural change invalidates the (row-index-keyed) match set; recompute against the new
            // rows if a query is active, and re-apply an active filter so hidden rows stay hidden.
            ResetFindState();
            if (pnlFindReplace.Visible && !string.IsNullOrEmpty(txtFind.Text)) RecomputeMatches();
            if (pnlFilter.Visible && !string.IsNullOrEmpty(txtFilter.Text)) ApplyFilter();
        }

        private static MutableStringTableEntry EntryForRow(DataGridViewRow row)
        {
            return row != null ? row.Tag as MutableStringTableEntry : null;
        }

        private MutableStringTableEntry CurrentEntry()
        {
            return gridSurface.CurrentRow != null ? EntryForRow(gridSurface.CurrentRow) : null;
        }

        // ── Controller event wiring ──────────────────────────────────────────

        // Raised after every Apply / Undo / Redo / MarkSaved. Cell edits already show their typed value,
        // so this only refreshes the dirty roll-up + counters + undo/redo state + cell overlays. Full row
        // resyncs for structural ops + undo/redo are done explicitly by their handlers (outside CellEndEdit).
        private void OnEditApplied(object sender, EventArgs e)
        {
            UpdateUndoRedoState();
            RefreshSaveMenuEnabledState();
            UpdateDirtyVisuals();
            UpdateCounters();
        }

        private void UpdateUndoRedoState()
        {
            btnUndo.Enabled = controller != null && controller.CanUndo;
            btnRedo.Enabled = controller != null && controller.CanRedo;
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            if (controller != null && controller.CanUndo)
            {
                controller.Undo();
                RebindGrid(); // F12: full grid row revert, not merely a dirty-visual refresh.
            }
        }

        private void OnRedoClicked(object sender, EventArgs e)
        {
            if (controller != null && controller.CanRedo)
            {
                controller.Redo();
                RebindGrid();
            }
        }

        // ── T4: Add entry / Remove entry ─────────────────────────────────────

        private void OnAddEntryClicked(object sender, EventArgs e)
        {
            if (controller == null || document == null) return;

            controller.Apply(StringTableEditCommands.AddEntry(document));
            RebindGrid();

            // Select the new row and drop its Key cell straight into edit mode so the user can rename the
            // auto {NNN}_default name immediately.
            MutableStringTableEntry added = document.Entries[document.Entries.Count - 1];
            int visual = FindVisualRow(added);
            if (visual >= 0)
            {
                try
                {
                    gridSurface.CurrentCell = gridSurface.Rows[visual].Cells[colKey.Index];
                    gridSurface.BeginEdit(true);
                }
                catch
                {
                    // Begin-edit is best-effort; the row is selected regardless.
                }
            }
        }

        private void OnRemoveEntryClicked(object sender, EventArgs e)
        {
            RemoveCurrentEntry();
        }

        private void RemoveCurrentEntry()
        {
            if (controller == null || document == null) return;
            MutableStringTableEntry entry = CurrentEntry();
            if (entry == null) return;

            controller.Apply(StringTableEditCommands.RemoveEntry(entry));
            RebindGrid();
        }

        private int FindVisualRow(MutableStringTableEntry entry)
        {
            for (int i = 0; i < gridSurface.Rows.Count; i++)
            {
                if (ReferenceEquals(gridSurface.Rows[i].Tag, entry)) return i;
            }
            return -1;
        }

        // ── Key-column name validation (F3c — DELEGATE to ValidateName) ──────

        // The Key cell-validator is the ONLY validation seam: it delegates to the framework predicate
        // MutableStringTableDocument.ValidateName (the single source of truth — the form re-implements
        // none of the charset / leading-digit / duplicate / empty rules). Invalid → red cell + red status
        // + e.Cancel (the controller never sees the bad name). The Text column has NO validation.
        private void OnCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (rebinding || document == null) return;
            if (e.ColumnIndex != colKey.Index || e.RowIndex < 0) return;

            MutableStringTableEntry entry = EntryForRow(gridSurface.Rows[e.RowIndex]);
            if (entry == null) return;

            string newName = e.FormattedValue == null ? string.Empty : e.FormattedValue.ToString();
            DataGridViewCell keyCell = gridSurface.Rows[e.RowIndex].Cells[colKey.Index];

            if (string.Equals(newName, entry.Name, StringComparison.Ordinal))
            {
                keyCell.Style.BackColor = Color.Empty; // unchanged — clear any prior red (restores zebra).
                return;
            }

            StringTableNameValidation result = document.ValidateName(newName, entry);
            if (!result.Ok)
            {
                keyCell.Style.BackColor = Color.Red;
                lblStatus.Text = result.Reason;
                lblStatus.ForeColor = Color.Red;
                e.Cancel = true; // block the commit; press Esc to revert to the current key.
                return;
            }

            keyCell.Style.BackColor = Color.Empty; // valid — clear any prior red (restores zebra/default).
        }

        // ── Commit-back (commit-on-CellEndEdit, not per-keystroke) ───────────

        private void OnCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (rebinding || controller == null || document == null) return;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            MutableStringTableEntry entry = EntryForRow(gridSurface.Rows[e.RowIndex]);
            if (entry == null) return;

            object value = gridSurface.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
            string raw = value == null ? string.Empty : value.ToString();

            if (e.ColumnIndex == colKey.Index)
            {
                // CellValidating already enforced ValidateName; a value that reaches here is either valid
                // or unchanged. Apply only a real change.
                if (!string.Equals(raw, entry.Name, StringComparison.Ordinal))
                {
                    controller.Apply(StringTableEditCommands.RenameKey(entry, raw));
                }
            }
            else if (e.ColumnIndex == colText.Index)
            {
                // SC4: the text bytes commit verbatim (no transformation). The setter no-ops an equal value.
                if (!string.Equals(raw, entry.Text, StringComparison.Ordinal))
                {
                    controller.Apply(StringTableEditCommands.EditText(entry, raw));
                }
            }
        }

        // SC4 non-behavior: the Text editing TextBox must not apply any OS auto-substitution that alters
        // committed bytes. Disable auto-complete; keep the default IME mode so legitimate CJK / accented
        // composition still works (the verbatim guarantee comes from the model + writer, proven by the
        // 10-01/10-02 byte-exact gates).
        private void OnEditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            var tb = e.Control as TextBox;
            if (tb == null) return;

            tb.AutoCompleteMode = AutoCompleteMode.None;
            tb.AutoCompleteSource = AutoCompleteSource.None;
            tb.CharacterCasing = CharacterCasing.Normal; // never upper/lower-case the typed text.

            bool isText = gridSurface.CurrentCell != null && gridSurface.CurrentCell.ColumnIndex == colText.Index;
            tb.Multiline = isText; // allow multi-line localized strings in the Text editor.
        }

        // ── Cell-state visuals ───────────────────────────────────────────────

        // Dirty (edited) + added cells render their foreground at the accent (Colors.Secondary()). The
        // invalid-key red BackColor is applied directly by the validator (it persists as the cell's own
        // style). The read-only id column keeps its recessed default style.
        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= gridSurface.Rows.Count) return;
            if (e.ColumnIndex == colId.Index) return;

            // Search-match overlay (Plan 10-04): the active match gets a full accent background; other
            // matches get an accent foreground. Takes precedence over the dirty/added tint so the user can
            // see what Find landed on.
            if (findMatches.Count > 0 && (e.ColumnIndex == colKey.Index || e.ColumnIndex == colText.Index))
            {
                bool isMatch = findMatches.Any(m => m.Key == e.RowIndex && m.Value == e.ColumnIndex);
                if (isMatch)
                {
                    bool isActive = currentMatchIndex >= 0 && currentMatchIndex < findMatches.Count
                        && findMatches[currentMatchIndex].Key == e.RowIndex
                        && findMatches[currentMatchIndex].Value == e.ColumnIndex;
                    if (isActive)
                    {
                        e.CellStyle.BackColor = Colors.Secondary();
                        e.CellStyle.ForeColor = Colors.Font();
                    }
                    else
                    {
                        e.CellStyle.ForeColor = Colors.Secondary();
                    }
                    return;
                }
            }

            MutableStringTableEntry entry = EntryForRow(gridSurface.Rows[e.RowIndex]);
            if (entry == null) return;

            if (entry.IsAdded || entry.IsDirty)
            {
                e.CellStyle.ForeColor = Colors.Secondary();
            }
        }

        // Row-header glyphs: ＋ for an added entry, ● for an edited/renamed (dirty) entry, else blank.
        private void UpdateRowHeaderGlyphs()
        {
            for (int i = 0; i < gridSurface.Rows.Count; i++)
            {
                MutableStringTableEntry entry = EntryForRow(gridSurface.Rows[i]);
                string glyph = entry == null ? "" : (entry.IsAdded ? "＋" : (entry.IsDirty ? "●" : ""));
                gridSurface.Rows[i].HeaderCell.Value = glyph;
            }
        }

        // ── Plan 10-04: Find/Replace pane (D-03a) ────────────────────────────

        private void ToggleFindPane(bool withReplace)
        {
            if (document == null) return;
            if (!pnlFindReplace.Visible) pnlFindReplace.Visible = true;
            txtReplace.Visible = withReplace;
            btnReplaceOne.Visible = withReplace;
            btnReplaceAll.Visible = withReplace;
            txtFind.Focus();
            txtFind.SelectAll();
        }

        private void CollapseFindPane()
        {
            pnlFindReplace.Visible = false;
            ResetFindState();
        }

        private void ResetFindState()
        {
            findMatches.Clear();
            currentMatchIndex = -1;
            lblFindCount.Text = "0 / 0";
            lblFindCount.ForeColor = Colors.FontDisabled();
            gridSurface.Invalidate();
        }

        // Rebuild the match set over the VISIBLE grid rows × {Key, Text}. Case toggle + regex opt-in; a
        // bad regex surfaces an "invalid pattern" count rather than throwing. A 2s match timeout guards
        // against a catastrophic pattern freezing the UI thread.
        private void RecomputeMatches()
        {
            findMatches.Clear();
            currentMatchIndex = -1;

            if (document == null || string.IsNullOrEmpty(txtFind.Text))
            {
                lblFindCount.Text = "0 / 0";
                lblFindCount.ForeColor = Colors.FontDisabled();
                gridSurface.Invalidate();
                return;
            }

            string query = txtFind.Text;
            Regex regex = null;
            if (findRegex)
            {
                try
                {
                    RegexOptions opts = findMatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                    regex = new Regex(query, opts, TimeSpan.FromSeconds(2));
                }
                catch (ArgumentException)
                {
                    lblFindCount.Text = "invalid pattern";
                    lblFindCount.ForeColor = Colors.Secondary();
                    gridSurface.Invalidate();
                    return;
                }
            }

            int[] cols = { colKey.Index, colText.Index };
            try
            {
                for (int r = 0; r < gridSurface.Rows.Count; r++)
                {
                    if (!gridSurface.Rows[r].Visible) continue;
                    foreach (int c in cols)
                    {
                        object val = gridSurface.Rows[r].Cells[c].Value;
                        string text = val == null ? string.Empty : val.ToString();
                        bool hit = regex != null
                            ? regex.IsMatch(text)
                            : text.IndexOf(query, findMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) >= 0;
                        if (hit) findMatches.Add(new KeyValuePair<int, int>(r, c));
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                findMatches.Clear();
                lblFindCount.Text = "pattern too slow";
                lblFindCount.ForeColor = Colors.Secondary();
                gridSurface.Invalidate();
                return;
            }

            if (findMatches.Count > 0)
            {
                currentMatchIndex = 0;
                JumpToCurrentMatch();
            }
            UpdateFindCount();
            gridSurface.Invalidate();
        }

        private void UpdateFindCount()
        {
            if (findMatches.Count == 0)
            {
                lblFindCount.Text = "0 / 0";
                lblFindCount.ForeColor = Colors.FontDisabled();
            }
            else
            {
                lblFindCount.Text = (currentMatchIndex + 1) + " / " + findMatches.Count;
                lblFindCount.ForeColor = Colors.Font();
            }
        }

        private void JumpToCurrentMatch()
        {
            if (currentMatchIndex < 0 || currentMatchIndex >= findMatches.Count) return;
            KeyValuePair<int, int> m = findMatches[currentMatchIndex];
            if (m.Key >= 0 && m.Key < gridSurface.Rows.Count && m.Value >= 0 && m.Value < gridSurface.Columns.Count)
            {
                gridSurface.CurrentCell = gridSurface.Rows[m.Key].Cells[m.Value];
            }
            gridSurface.Invalidate();
        }

        private void FindNext()
        {
            if (findMatches.Count == 0) return;
            currentMatchIndex = (currentMatchIndex + 1) % findMatches.Count;
            JumpToCurrentMatch();
            UpdateFindCount();
        }

        private void FindPrev()
        {
            if (findMatches.Count == 0) return;
            currentMatchIndex = (currentMatchIndex - 1 + findMatches.Count) % findMatches.Count;
            JumpToCurrentMatch();
            UpdateFindCount();
        }

        // Replace the current match's cell. A KEY-cell replace re-runs ValidateName (invalid → status red,
        // no commit); a TEXT-cell replace never fails.
        private void OnReplaceOneClicked(object sender, EventArgs e)
        {
            if (controller == null || document == null) return;
            if (currentMatchIndex < 0 || currentMatchIndex >= findMatches.Count) return;
            KeyValuePair<int, int> m = findMatches[currentMatchIndex];
            if (ReplaceMatchCell(m.Key, m.Value)) RecomputeMatches();
        }

        // Replace EVERY match. Text replacements always apply; key replacements that fail ValidateName are
        // counted + skipped. Each replacement is its own undoable edit.
        private void OnReplaceAllClicked(object sender, EventArgs e)
        {
            if (controller == null || document == null || findMatches.Count == 0) return;

            int replaced = 0, skipped = 0;
            // Snapshot the matches — applying edits + recompute would mutate the live list mid-loop.
            var snapshot = new List<KeyValuePair<int, int>>(findMatches);
            foreach (KeyValuePair<int, int> m in snapshot)
            {
                if (ReplaceMatchCell(m.Key, m.Value)) replaced++; else skipped++;
            }

            lblStatus.Text = replaced + " replaced" + (skipped > 0 ? (", " + skipped + " skipped (invalid key)") : "");
            lblStatus.ForeColor = skipped > 0 ? Color.Red : Colors.Font();
            RecomputeMatches();
        }

        // Apply the txtReplace text to one matched cell, routed through the controller (undoable). Returns
        // false when a KEY replacement fails ValidateName (no commit).
        private bool ReplaceMatchCell(int rowIndex, int colIndex)
        {
            if (rowIndex < 0 || rowIndex >= gridSurface.Rows.Count) return false;
            MutableStringTableEntry entry = EntryForRow(gridSurface.Rows[rowIndex]);
            if (entry == null) return false;

            object val = gridSurface.Rows[rowIndex].Cells[colIndex].Value;
            string current = val == null ? string.Empty : val.ToString();
            string replaced = ApplyReplace(current, txtReplace.Text);

            if (colIndex == colKey.Index)
            {
                if (string.Equals(replaced, entry.Name, StringComparison.Ordinal)) return true;
                StringTableNameValidation result = document.ValidateName(replaced, entry);
                if (!result.Ok)
                {
                    lblStatus.Text = result.Reason;
                    lblStatus.ForeColor = Color.Red;
                    return false;
                }
                controller.Apply(StringTableEditCommands.RenameKey(entry, replaced));
                gridSurface.Rows[rowIndex].Cells[colIndex].Value = replaced;
                return true;
            }

            if (colIndex == colText.Index)
            {
                if (string.Equals(replaced, entry.Text, StringComparison.Ordinal)) return true;
                controller.Apply(StringTableEditCommands.EditText(entry, replaced));
                gridSurface.Rows[rowIndex].Cells[colIndex].Value = replaced;
                return true;
            }

            return false;
        }

        // Replace occurrences of the Find query within a cell's text (regex or literal, honoring the case
        // toggle). For a regex find, the replacement honors $-group substitutions.
        private string ApplyReplace(string input, string replacement)
        {
            replacement = replacement ?? string.Empty;
            if (string.IsNullOrEmpty(txtFind.Text)) return input;

            if (findRegex)
            {
                try
                {
                    RegexOptions opts = findMatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                    return Regex.Replace(input, txtFind.Text, replacement, opts, TimeSpan.FromSeconds(2));
                }
                catch (ArgumentException) { return input; }
                catch (RegexMatchTimeoutException) { return input; }
            }

            StringComparison cmp = findMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var sb = new System.Text.StringBuilder();
            int i = 0;
            while (i < input.Length)
            {
                int hit = input.IndexOf(txtFind.Text, i, cmp);
                if (hit < 0) { sb.Append(input.Substring(i)); break; }
                sb.Append(input.Substring(i, hit - i));
                sb.Append(replacement);
                i = hit + txtFind.Text.Length;
            }
            return sb.ToString();
        }

        // ── Plan 10-04: live filter row (D-03c — view-only) ──────────────────

        private void ToggleFilterRow()
        {
            if (document == null) return;
            pnlFilter.Visible = !pnlFilter.Visible;
            ini.AddSetting("StringTableEditor", "filterVisible", pnlFilter.Visible ? "1" : "0", UtINI.Value.Types.VtBool);
            try { ini.Save(); } catch { /* persistence best-effort */ }

            if (pnlFilter.Visible) { txtFilter.Focus(); }
            else { txtFilter.Text = ""; ApplyFilter(); }
        }

        // Hide rows whose Key + Text both miss the filter query (case-insensitive). VIEW-ONLY — never
        // mutates the document or the on-disk order (D-03c). Updates the shown/total count.
        private void ApplyFilter()
        {
            if (document == null) return;
            string q = txtFilter.Text ?? string.Empty;
            int shown = 0;
            int total = gridSurface.Rows.Count;

            // CurrentCell must leave a row before it is hidden, or WinForms throws.
            if (q.Length > 0 && gridSurface.CurrentCell != null) gridSurface.CurrentCell = null;

            for (int r = 0; r < gridSurface.Rows.Count; r++)
            {
                bool visible = true;
                if (q.Length > 0)
                {
                    string key = CellText(r, colKey.Index);
                    string text = CellText(r, colText.Index);
                    visible = key.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                           || text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                gridSurface.Rows[r].Visible = visible;
                if (visible) shown++;
            }

            lblFilterCount.Text = q.Length > 0 ? (shown + " / " + total) : "";
            lblFilterCount.ForeColor = Colors.FontDisabled();
            UpdateCounters();
        }

        private string CellText(int rowIndex, int colIndex)
        {
            object v = gridSurface.Rows[rowIndex].Cells[colIndex].Value;
            return v == null ? string.Empty : v.ToString();
        }

        private bool FilterActive()
        {
            return pnlFilter.Visible && !string.IsNullOrEmpty(txtFilter.Text);
        }

        // ── Plan 10-04: CSV import / export + PO export (D-03b / D-03d) ───────

        private void OnImportCsvClicked(object sender, EventArgs e)
        {
            if (document == null || controller == null) return;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Import CSV…";
                ofd.Filter = "CSV files (*.csv)|*.csv|TSV files (*.tsv)|*.tsv|All files (*.*)|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;

                lblStatus.Text = "Importing CSV…";
                lblStatus.ForeColor = Colors.Font();
                try
                {
                    StringTableCsvImportPlan plan = StringTableCsvSerializer.LoadAndPlan(document, ofd.FileName);
                    using (var dlg = new FormStfCsvImportPreviewDialog(plan, Path.GetFileName(ofd.FileName), displayName ?? "string table"))
                    {
                        if (dlg.ShowDialog(this) != DialogResult.OK)
                        {
                            lblStatus.Text = "CSV import cancelled.";
                            lblStatus.ForeColor = Colors.Font();
                            return;
                        }
                    }

                    controller.Apply(StringTableEditCommands.ApplyCsvImport(document, plan));
                    RebindGrid();
                    lblStatus.Text = "Imported " + Path.GetFileName(ofd.FileName) + ": "
                        + plan.Changes.Count + " changed, " + plan.Added.Count + " added, "
                        + plan.Unchanged.Count + " preserved as original bytes.";
                    lblStatus.ForeColor = Colors.Font();
                }
                catch (Exception ex)
                {
                    lblStatus.Text = "CSV import failed: " + ex.Message;
                    lblStatus.ForeColor = Color.Red;
                }
            }
        }

        private void OnExportCsvClicked(object sender, EventArgs e)
        {
            if (document == null) return;
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Export CSV…";
                sfd.Filter = "CSV files (*.csv)|*.csv|TSV files (*.tsv)|*.tsv";
                sfd.FileName = !string.IsNullOrEmpty(displayName)
                    ? Path.GetFileNameWithoutExtension(displayName) + ".csv"
                    : "stringtable.csv";
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    StringTableCsvSerializer.Export(document, sfd.FileName);
                    lblStatus.Text = "Exported " + Path.GetFileName(sfd.FileName);
                    lblStatus.ForeColor = Colors.Font();
                }
                catch (Exception ex)
                {
                    lblStatus.Text = "CSV export failed: " + ex.Message;
                    lblStatus.ForeColor = Color.Red;
                }
            }
        }

        private void OnExportPoClicked(object sender, EventArgs e)
        {
            if (document == null) return;
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Export PO…";
                sfd.Filter = "PO files (*.po)|*.po|All files (*.*)|*.*";
                sfd.FileName = !string.IsNullOrEmpty(displayName)
                    ? Path.GetFileNameWithoutExtension(displayName) + ".po"
                    : "stringtable.po";
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    StringTableCsvSerializer.ExportPo(document, sfd.FileName);
                    lblStatus.Text = "Exported " + Path.GetFileName(sfd.FileName);
                    lblStatus.ForeColor = Colors.Font();
                }
                catch (Exception ex)
                {
                    lblStatus.Text = "PO export failed: " + ex.Message;
                    lblStatus.ForeColor = Color.Red;
                }
            }
        }

        // ── Dirty visuals + counters ─────────────────────────────────────────

        private const string BaseTitle = "String-table Editor";

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
            UpdateRowHeaderGlyphs();
            gridSurface.Invalidate(); // re-evaluate the dirty/added cell foreground overlays.
        }

        private void UpdateCounters()
        {
            if (document == null)
            {
                lblCounters.Text = "0 entries";
                lblCounters.ForeColor = Colors.FontDisabled();
                return;
            }

            int entries = document.Entries.Count;
            int dirty = document.Entries.Count(en => en.IsDirty || en.IsAdded);

            // When a live filter is active, append the visible-row count (D-03c view-only).
            string shownSuffix = "";
            if (FilterActive())
            {
                int shown = gridSurface.Rows.Cast<DataGridViewRow>().Count(r => r.Visible);
                shownSuffix = " · " + shown + " shown";
            }

            if (dirty == 0)
            {
                lblCounters.Text = entries + " entries" + shownSuffix;
                lblCounters.ForeColor = shownSuffix.Length > 0 ? Colors.Font() : Colors.FontDisabled();
            }
            else
            {
                lblCounters.Text = entries + " entries · " + dirty + " dirty" + shownSuffix;
                lblCounters.ForeColor = Colors.Font();
            }
        }

        // ── Show id toggle ───────────────────────────────────────────────────

        private void OnShowIdToggled(object sender, EventArgs e)
        {
            colId.Visible = tglShowId.Checked;
            ini.AddSetting("StringTableEditor", "showIdColumn", tglShowId.Checked ? "1" : "0", UtINI.Value.Types.VtBool);
            try { ini.Save(); } catch { /* persistence best-effort */ }
        }

        // ── Save▾ drop-down (Plan 10-05 — modes 1/2/4 wired; mode 3 disabled CF-03) ──

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
            miRepackTre = new ToolStripMenuItem("Repack into source .tre…");
            miRepackTre.Click += OnRepackTreClick;
            saveMenu.Items.AddRange(new ToolStripItem[]
            {
                miSaveInPlace, miSaveLooseOverride, miSaveAs,
                new ToolStripSeparator(),
                miPatchLive, miRepackTre,
            });
        }

        // Provenance gate (Phase 8 W-3; no NeedsReview cascade — .stf has no column types). Each item's
        // enabled state is provenanceAllows && !saveInFlight; the Save button enables when any mode can run.
        private void RefreshSaveMenuEnabledState()
        {
            bool hasDoc = document != null;
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
                miSaveAs.Enabled = hasDoc && !saveInFlight; // escape hatch — always enabled on a loaded doc.
                miSaveAs.ToolTipText = "Save the current edits to a path you choose.";
            }
            if (miPatchLive != null)
            {
                miPatchLive.Enabled = false; // CF-03.
                miPatchLive.ToolTipText = "Live patch requires opening from client memory — not wired in this phase.";
            }
            if (miRepackTre != null)
            {
                miRepackTre.Enabled = hasDoc && isTreArchive && !saveInFlight;
                miRepackTre.ToolTipText = isTreArchive ? "" : "Open from a packed .tre to repack the source archive.";
            }

            btnSave.Enabled = hasDoc && !saveInFlight && (isLooseFile || isTreArchive || isUnknown);
        }

        private void OnSaveButtonClick(object sender, EventArgs e)
        {
            if (saveMenu == null) return;
            saveMenu.Show(btnSave, new Point(0, btnSave.Height));
        }

        // ── Save▾ click handlers (Plan 10-05) ────────────────────────────────

        private async void OnSaveInPlaceClick(object sender, EventArgs e)
        {
            if (document == null) return;
            if (!(Source is OpenSource.LooseFile))
            {
                SetSaveFailure("Cannot save in place — file came from .tre or unknown source. Use Save as loose override or Save As.");
                return;
            }
            await DoFileSaveAsync(() => StringTableSaveTargets.SaveInPlace(document, Source), "in place");
        }

        private async void OnSaveLooseOverrideClick(object sender, EventArgs e)
        {
            if (document == null) return;
            string clientRoot = ResolveClientRoot();
            if (string.IsNullOrEmpty(clientRoot))
            {
                SetSaveFailure("Could not locate the client root — use Save As… and we'll remember the directory.");
                return;
            }
            string subDir = ini.GetString("StringTableEditor", "looseOverrideDir");
            if (string.IsNullOrEmpty(subDir))
            {
                subDir = "loose";
                lblStatus.Text = "Saving to " + subDir
                    + ". Change the loose-override directory in [StringTableEditor] looseOverrideDir if needed.";
                lblStatus.ForeColor = Colors.Font();
            }
            await DoFileSaveAsync(
                () => StringTableSaveTargets.SaveLooseOverride(document, Source, clientRoot, subDir),
                "loose override");
        }

        private async void OnSaveAsClick(object sender, EventArgs e)
        {
            if (document == null) return;
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Save string table as…";
                sfd.Filter = "String tables (*.stf)|*.stf|All files (*.*)|*.*";
                sfd.FileName = !string.IsNullOrEmpty(displayName) ? displayName : "untitled.stf";
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                string path = sfd.FileName;
                await DoFileSaveAsync(() => StringTableSaveTargets.SaveToPath(document, path), "save-as");
            }
        }

        private async void OnRepackTreClick(object sender, EventArgs e)
        {
            if (document == null) return;
            var ta = Source as OpenSource.TreArchive;
            if (ta == null)
            {
                SetSaveFailure("Open from a packed .tre to repack the source archive.");
                return;
            }

            string archiveName = Path.GetFileName(ta.TrePath ?? "");
            bool backupRequested;
            using (var dlg = new FormSaveConfirmDialog(
                heading: "Repack " + archiveName + "?",
                body: "This rewrites the entire " + archiveName + " archive on disk and replaces it "
                    + "atomically. Untouched entries are preserved byte-for-byte; only the edited string "
                    + "table recompresses. If the client holds the archive open, the repack is refused "
                    + "without a partial-write. Continue?",
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
                result = await StringTableSaveTargets.RepackIntoSourceTre(document, ta, backupRequested)
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
                    lblStatus.Text = "Client holds the archive — try Save as loose override instead.";
                    lblStatus.ForeColor = Color.Red;
                    break;

                default:
                    // Failed also covers the Phase 8 WR-06 V6000 enumerate-only refusal.
                    lblStatus.Text = "Repack failed — your edits are retained. If this is a V6000 (encrypted) archive, use Save as loose override.";
                    lblStatus.ForeColor = Color.Red;
                    break;
            }

            RefreshSaveMenuEnabledState();
            RefreshReloadButtonState();
        }

        // Shared file-save orchestration (modes 1/2): set the saveInFlight barrier, surface the
        // Saving/Saved/Save-failed copy, and on success MarkSaved() + route the reload dispatch.
        private async Task<bool> DoFileSaveAsync(Func<Task<IffSaveTargets.SaveResult>> saveOp, string modeLabel)
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
                if (controller != null) controller.MarkSaved(); // reset dirty baseline + clear ● glyph.
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

        // Routes the just-saved .stf through Phase 8's tiered reload dispatcher (tier-(b)
        // PendingNextSceneChange — the audit-trail routing; the user-facing copy stays the locked CF-05
        // wording in OnReloadClicked). rootTypeId is null because .stf is flat, not IFF.
        private void DispatchReload(string savedPath)
        {
            if (string.IsNullOrEmpty(savedPath)) return;
            try
            {
                ClientReloadDispatcher.Dispatch(savedPath, null);
            }
            catch
            {
                // Dispatch gates on Game.IsRunning internally; never let a binding hiccup tear down save-success.
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

        private void RefreshReloadButtonState()
        {
            btnReload.Enabled = document != null && !saveInFlight;
        }

        // ── Reload-in-client (CF-05 candor — tier-(b) for .stf) ──────────────

        private void OnReloadClicked(object sender, EventArgs e)
        {
            // Plan 10-05: when a save happened this session AND the client is up, route the dispatch for
            // the audit trail (it classifies .stf as PendingNextSceneChange anyway — no fresh binding).
            if (!string.IsNullOrEmpty(lastSavedPath))
            {
                bool clientUp = false;
                try { clientUp = Game.IsRunning; }
                catch { clientUp = false; }
                if (clientUp) DispatchReload(lastSavedPath);
            }

            // CF-05 LOCKED user-facing copy (10-UI-SPEC § Copywriting tier-(b)).
            lblStatus.Text = "String tables re-resolve on the next scene change. Trigger one via TJT's chat-command load.";
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

        // ── D-04 TRE Browser hand-off entry ──────────────────────────────────

        /// <summary>
        /// TRE Browser hand-off (D-04). Parses the resolved payload as a flat <c>.stf</c>, resolves the
        /// <see cref="OpenSource"/> (TreArchive via <see cref="TreRecordIndexResolver.ResolveOrUnknown"/>,
        /// or Unknown on a degraded resolve), and binds it — mirrors Phase 9's
        /// <c>FormDatatableEditor.OpenFromTreEntry</c>.
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
                StringTableDocument doc = StringTableDocument.FromBytes(payload);
                OpenSource src = TreRecordIndexResolver.ResolveOrUnknown(resolvedArchivePath, archiveLocalOffset, logicalPath);
                string name = logicalPath != null ? Path.GetFileName(logicalPath) : Path.GetFileName(resolvedArchivePath ?? "");
                LoadDocument(doc.Mutable, src, name);
                lblStatus.Text = src is OpenSource.TreArchive
                    ? "Opened " + name + " from " + Path.GetFileName(resolvedArchivePath ?? "")
                    : "Opened " + name + " — record index unresolved; use Save As to write to a chosen file.";
                lblStatus.ForeColor = Colors.Font();
            }
            catch (Exception ex)
            {
                SetSaveFailure("TRE hand-off failed: " + ex.Message);
            }
        }

        // ── Keyboard shortcuts ───────────────────────────────────────────────
        //
        // Ctrl+Z / Ctrl+Y → editor-local undo / redo (CF-04), caught at the form BEFORE the grid so they
        // never reach the scene UndoRedoManager. Ctrl+F / Ctrl+H → Find/Replace; Ctrl+L → Filter row;
        // F3 / Shift+F3 cycle matches; Esc collapses Find or clears Filter. Delete (outside cell-edit)
        // removes the current entry. Ctrl+S is deferred to 10-05 (save).
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
            if (keyData == (Keys.Control | Keys.F))
            {
                if (document != null) ToggleFindPane(false);
                return true;
            }
            if (keyData == (Keys.Control | Keys.H))
            {
                if (document != null) ToggleFindPane(true);
                return true;
            }
            if (keyData == (Keys.Control | Keys.L))
            {
                if (document != null) ToggleFilterRow();
                return true;
            }
            if (keyData == Keys.F3)
            {
                if (pnlFindReplace.Visible) FindNext();
                return true;
            }
            if (keyData == (Keys.Shift | Keys.F3))
            {
                if (pnlFindReplace.Visible) FindPrev();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                if (pnlFindReplace.Visible) { CollapseFindPane(); return true; }
                if (FilterActive()) { txtFilter.Text = ""; ApplyFilter(); return true; }
            }
            if (keyData == Keys.Delete
                && controller != null
                && gridSurface.Focused
                && !gridSurface.IsCurrentCellInEditMode
                && CurrentEntry() != null)
            {
                RemoveCurrentEntry();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ── Singleton hide-not-dispose (MANDATORY FROM COMMIT 1) ─────────────

        private void FormStringTableEditor_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                ini.AddSetting("StringTableEditor", "width", Width.ToString(), UtINI.Value.Types.VtInt);
                ini.AddSetting("StringTableEditor", "height", Height.ToString(), UtINI.Value.Types.VtInt);
                ini.Save();
            }
            catch
            {
                // Persistence is best-effort; never block close.
            }

            // Plan 10-05: now that save exists, a user-initiated close while dirty routes through the
            // inherited FormSaveConfirmDialog (Discard / Cancel). Cancel aborts the close; Discard proceeds
            // to the hide-not-dispose path (edits survive in memory regardless, since the singleton hides).
            if (e.CloseReason == CloseReason.UserClosing && controller != null && controller.IsDirty)
            {
                using (var dlg = new FormSaveConfirmDialog(
                    heading: "Discard unsaved changes?",
                    body: (displayName ?? "This string table") + " has unsaved edits. Discard them?",
                    acceptVerb: "Discard",
                    cancelVerb: "Cancel"))
                {
                    dlg.ShowDialog(this);
                    if (dlg.Outcome != FormSaveConfirmDialog.ConfirmOutcome.Accepted)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }

            // The CloseReason DECISION lives in the framework helper so the xUnit guard can exercise it
            // without instantiating the form. On user-initiated close, hide instead of disposing so a
            // subsequent Show() re-Shows this same singleton instance (Phase 8/9 idiom).
            if (SingletonFormClosePolicy.ShouldHideInsteadOfDispose(e.CloseReason))
            {
                e.Cancel = true;
                Hide();
            }
        }
    }
}
