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
using System.Windows.Forms;
using UtinniCore.Utinni;
using UtinniCoreDotNet.Editing;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.Formats.StringTable;
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
    /// <para><b>Plan 10-03 staging:</b> this plan ships the host + grid + T4 mutation + the
    /// <c>Open…</c> entry point + the read-only <c>id</c>-column toggle only. The Save▾ menu items +
    /// Find / Replace / Filter / CSV / PO / Reload toolbar buttons render DISABLED with explanatory
    /// tooltips (NOT throwing). Subsequent plans wire them: 10-04 (Find/Replace + Filter + CSV + PO),
    /// 10-05 (save targets + reload dispatch + TRE Browser hand-off).</para>
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

        // Save▾ drop-down items (DISABLED stubs in Plan 10-03; 10-05 wires them).
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

            // Tooltips for the deferred-feature buttons (rendered disabled, NOT throwing).
            toolTip.SetToolTip(btnSave, "Save the current string table (choose a target).");
            toolTip.SetToolTip(btnImportCsv, "Import a CSV/TSV delta (preview before apply).");
            toolTip.SetToolTip(btnExportCsv, "Export the current string table to CSV/TSV.");
            toolTip.SetToolTip(btnExportPo, "Export the current string table to PO/gettext.");
            toolTip.SetToolTip(btnFind, "Find (Ctrl+F)");
            toolTip.SetToolTip(btnReplace, "Replace (Ctrl+H)");
            toolTip.SetToolTip(btnFilter, "Filter visible rows over key + text.");
            toolTip.SetToolTip(btnReload, "Reloads on next scene change.");
            toolTip.SetToolTip(tglShowId, "Show the machine-managed id column (read-only diagnostic).");

            // ENABLED, functional toolbar buttons in Plan 10-03: Open… + the T4 verbs + Show id.
            btnOpen.Click += OnOpenClicked;
            btnSave.Click += OnSaveButtonClick;
            btnUndo.Click += OnUndoClicked;
            btnRedo.Click += OnRedoClicked;
            btnAddEntry.Click += OnAddEntryClicked;
            btnRemoveEntry.Click += OnRemoveEntryClicked;
            tglShowId.CheckedChanged += OnShowIdToggled;

            // Grid commit-back + cell-state visuals.
            gridSurface.CellValidating += OnCellValidating;
            gridSurface.CellEndEdit += OnCellEndEdit;
            gridSurface.EditingControlShowing += OnEditingControlShowing;
            gridSurface.CellFormatting += OnCellFormatting;

            // Save▾ drop-down (items DISABLED + tooltip; wired in Plan 10-05).
            BuildSaveMenu();

            SetTitle(null);
            UpdateUndoRedoState();
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

            if (controller != null) controller.EditApplied -= OnEditApplied;
            controller = new StringTableEditController(mutable);
            controller.EditApplied += OnEditApplied;

            RebindGrid();

            // The T4 verbs + Show id become functional once a document is bound.
            btnAddEntry.Enabled = true;
            btnRemoveEntry.Enabled = true;
            tglShowId.Enabled = true;

            // Restore the persisted id-column visibility (setting Checked flips colId.Visible).
            tglShowId.Checked = ini.GetBool("StringTableEditor", "showIdColumn");
            colId.Visible = tglShowId.Checked;

            lblEmptyState.Visible = false;
            lblReloadBadge.Visible = true; // CF-05 locked copy from the moment a file loads.

            UpdateUndoRedoState();
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

            if (dirty == 0)
            {
                lblCounters.Text = entries + " entries";
                lblCounters.ForeColor = Colors.FontDisabled();
            }
            else
            {
                lblCounters.Text = entries + " entries · " + dirty + " dirty";
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

        // ── Save▾ drop-down (DISABLED stubs in Plan 10-03; 10-05 wires) ──────

        private void BuildSaveMenu()
        {
            saveMenu = new UtinniContextMenuStrip();
            miSaveInPlace = new ToolStripMenuItem("Save (in place)");
            miSaveInPlace.Enabled = false;
            miSaveInPlace.ToolTipText = "Saving is wired in a later plan.";
            miSaveLooseOverride = new ToolStripMenuItem("Save as loose override");
            miSaveLooseOverride.Enabled = false;
            miSaveLooseOverride.ToolTipText = "Saving is wired in a later plan.";
            miSaveAs = new ToolStripMenuItem("Save As…");
            miSaveAs.Enabled = false;
            miSaveAs.ToolTipText = "Saving is wired in a later plan.";
            miPatchLive = new ToolStripMenuItem("Patch live client (in memory)");
            miPatchLive.Enabled = false; // CF-03 — Mode 3 disabled.
            miPatchLive.ToolTipText = "Live patch requires opening from client memory — not wired in this phase.";
            miRepackTre = new ToolStripMenuItem("Repack into source .tre…");
            miRepackTre.Enabled = false;
            miRepackTre.ToolTipText = "Open from a packed .tre to repack the source archive.";
            saveMenu.Items.AddRange(new ToolStripItem[]
            {
                miSaveInPlace, miSaveLooseOverride, miSaveAs,
                new ToolStripSeparator(),
                miPatchLive, miRepackTre,
            });
        }

        private void OnSaveButtonClick(object sender, EventArgs e)
        {
            // btnSave is disabled in Plan 10-03; the menu is anchored here for 10-05.
            if (saveMenu == null) return;
            saveMenu.Show(btnSave, new Point(0, btnSave.Height));
        }

        // ── Keyboard shortcuts ───────────────────────────────────────────────
        //
        // Ctrl+Z / Ctrl+Y → editor-local undo / redo (CF-04), caught at the form BEFORE the grid so they
        // never reach the scene UndoRedoManager. Delete (outside cell-edit) removes the current entry.
        // Ctrl+S + Find/Replace/Filter chords are deferred (save = 10-05; Find/Replace/Filter = 10-04).
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
