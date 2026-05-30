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
using System.Windows.Forms;
using UtinniCoreDotNet.Formats.Datatable;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Controls
{
    /// <summary>
    /// TJT-side themed wrapper around <see cref="System.Windows.Forms.DataGridView"/>. The
    /// constructor applies the 09-UI-SPEC § ThemedDataGridView token map VERBATIM (every property
    /// value traces to a <see cref="Colors"/> accessor — NO raw ARGB color literals).
    /// Lives alongside <see cref="IffChunkTree"/> in <c>TJT.UI.Controls</c>; Phases 10/11 inherit
    /// this control.
    ///
    /// <remarks>
    /// <para><b>Grid↔model binding contract (iter-2 item 1, 09-UI-SPEC § Grid surface):</b>
    /// <see cref="BindMutable"/> populates the grid in NON-VIRTUAL mode — after
    /// <c>Columns.AddRange(columns)</c> it appends one <see cref="DataGridViewRow"/> per
    /// <see cref="MutableDataTableRow"/> and sets each cell's <c>.Value</c> from the backing
    /// <see cref="MutableDataTableCell.Value"/> projected to the column's display representation
    /// (text columns ← string; <see cref="DataGridViewCheckBoxColumn"/> ← bool;
    /// <see cref="DataGridViewComboBoxColumn"/> ← enum label). This method does NOT set
    /// <c>VirtualMode = true</c> and does NOT rely on <c>CellValueNeeded</c> — that handler is
    /// reserved for the conditional Plan 09-06 Task 4 VirtualMode branch only.</para>
    ///
    /// <para><b>Cell-state visual overlays (09-UI-SPEC § Cell-state visual overlays):</b> the
    /// <c>CellFormatting</c> handler paints the dirty (ForeColor = <see cref="Colors.Secondary"/>),
    /// added-row (＋ glyph + accent foreground) and needs-review (<see cref="Color.Red"/> background)
    /// states. Search-match overlay and the frozen-DT_Comment-row toggle are deferred to Plan 09-06;
    /// the formatter has hooks but does nothing for those states until then. This is the SAME
    /// representative overlay the Task 3 perf probe attaches to a plain DataGridView to measure the
    /// production bind path.</para>
    /// </remarks>
    /// </summary>
    public partial class ThemedDataGridView : DataGridView
    {
        private MutableDataTableDocument boundDocument;

        // Plan 09-06: search-match overlay state. The host (FormDatatableEditor) sets the current
        // match set + the active match coords; CellFormatting paints them. A null/empty set = no
        // search overlay (the formatter falls through to the dirty/added/needs-review states).
        private HashSet<long> searchMatchKeys;
        private long activeMatchKey = -1;

        // Plan 09-06: index of the frozen DT_Comment header row (-1 when none / editable). When set,
        // CellFormatting renders that row with the dimmed header treatment.
        private int frozenCommentRowIndex = -1;

        private static long CellKey(int row, int col)
        {
            return ((long)row << 32) | (uint)col;
        }

        /// <summary>
        /// Sets the current Find search-match overlay (Plan 09-06). <paramref name="matches"/> is the
        /// full match set; <paramref name="activeRow"/>/<paramref name="activeCol"/> is the currently-
        /// focused match (rendered with a stronger accent). Pass an empty list to clear.
        /// </summary>
        public void SetSearchMatches(IEnumerable<KeyValuePair<int, int>> matches, int activeRow, int activeCol)
        {
            searchMatchKeys = new HashSet<long>();
            if (matches != null)
            {
                foreach (KeyValuePair<int, int> m in matches)
                {
                    searchMatchKeys.Add(CellKey(m.Key, m.Value));
                }
            }
            activeMatchKey = (activeRow >= 0 && activeCol >= 0) ? CellKey(activeRow, activeCol) : -1;
            Invalidate();
        }

        /// <summary>
        /// Sets the frozen DT_Comment header row index (Plan 09-06). Pass -1 to clear (editable). The
        /// host also flips the underlying <see cref="DataGridViewRow.Frozen"/> + ReadOnly state; this
        /// drives the dimmed CellFormatting treatment.
        /// </summary>
        public void SetFrozenCommentRow(int rowIndex)
        {
            frozenCommentRowIndex = rowIndex;
            Invalidate();
        }

        public ThemedDataGridView()
        {
            // 09-UI-SPEC § ThemedDataGridView token map — verbatim, all values via Colors.*().
            BackgroundColor = Colors.PrimaryHighlight();
            GridColor = Colors.ControlBorder();
            BorderStyle = BorderStyle.None;
            EnableHeadersVisualStyles = false;

            ColumnHeadersDefaultCellStyle.BackColor = Colors.PrimaryShadow();
            ColumnHeadersDefaultCellStyle.ForeColor = Colors.Font();
            ColumnHeadersDefaultCellStyle.SelectionBackColor = Colors.PrimaryShadow();
            ColumnHeadersDefaultCellStyle.SelectionForeColor = Colors.Font();
            ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Regular);
            ColumnHeadersHeight = 24;

            RowHeadersDefaultCellStyle.BackColor = Colors.PrimaryShadow();
            RowHeadersDefaultCellStyle.ForeColor = Colors.FontDisabled();
            RowHeadersWidth = 40;

            DefaultCellStyle.BackColor = Colors.PrimaryHighlight();
            DefaultCellStyle.ForeColor = Colors.Font();
            DefaultCellStyle.SelectionBackColor = Colors.Secondary();
            DefaultCellStyle.SelectionForeColor = Colors.Font();

            AlternatingRowsDefaultCellStyle.BackColor = Colors.Primary();
            AlternatingRowsDefaultCellStyle.ForeColor = Colors.Font();

            RowTemplate.Height = 22;

            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;

            AllowUserToAddRows = false;
            AllowUserToDeleteRows = false;
            AllowUserToOrderColumns = true;
            AllowUserToResizeColumns = true;

            MultiSelect = true;
            SelectionMode = DataGridViewSelectionMode.CellSelect;
            ScrollBars = ScrollBars.Both;
            Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Regular);

            CellFormatting += OnCellFormatting;
            CellValueNeeded += OnCellValueNeeded;
            CellValuePushed += OnCellValuePushed;
        }

        /// <summary>The document currently bound to the grid (null before the first bind).</summary>
        public MutableDataTableDocument BoundDocument
        {
            get { return boundDocument; }
        }

        // ── Plan 09-06 Task 4: VirtualMode fallback (09-03 measured > 100 ms) ──
        //
        // The 09-03 bind-latency probe measured 265.63 ms cold / 121.93 ms typical for the 200×30
        // combat-scale table on the production CellFormatting path — ABOVE the 100 ms threshold. So
        // Plan 09-06 ships a VirtualMode fallback that engages for tables whose row count exceeds
        // VirtualRowThreshold: instead of materializing a DataGridViewRow per model row (the cost the
        // probe measured), it sets VirtualMode = true + RowCount and serves cell values on demand via
        // CellValueNeeded / writes them back via CellValuePushed. Small tables keep the non-virtual
        // path so the per-type editor widgets (CheckBox / ComboBox / NumericUpDown) work unchanged.

        /// <summary>
        /// Row-count threshold at/above which <see cref="BindMutable"/> uses VirtualMode (Plan 09-06
        /// Task 4 — the 09-03 measurement showed the non-virtual bind exceeds 100 ms at combat scale).
        /// </summary>
        public const int VirtualRowThreshold = 150;

        /// <summary>True iff the current binding is using VirtualMode (large-table fallback).</summary>
        public bool IsVirtual { get; private set; }

        // Callback the host installs so a VirtualMode CellValuePushed routes the edit through the
        // editor-local undo controller (controller.Apply(EditCellValue(...))). Null in non-virtual mode.
        private Action<int, int, string> virtualCommitCallback;

        /// <summary>
        /// Installs the commit-back callback for VirtualMode edits (Plan 09-06 Task 4). The host passes
        /// a delegate that routes <c>(rowIndex, columnIndex, rawValue)</c> through
        /// <c>controller.Apply(DatatableEditCommands.EditCellValue(...))</c>.
        /// </summary>
        public void SetVirtualCommitCallback(Action<int, int, string> callback)
        {
            virtualCommitCallback = callback;
        }

        /// <summary>
        /// Populates the grid NON-VIRTUAL from the given typed document and pre-built per-type
        /// columns (see <see cref="DatatableColumnFactory.Build"/>). After <c>Columns.AddRange</c>,
        /// appends one row per <see cref="MutableDataTableRow"/> and sets each cell's
        /// <c>.Value</c> from the backing model projected to the column's display representation.
        /// Does NOT set <c>VirtualMode</c>; <c>CellValueNeeded</c> is reserved for the Plan 09-06
        /// VirtualMode branch only.
        /// </summary>
        public void BindMutable(MutableDataTableDocument doc, IReadOnlyList<DataGridViewColumn> columns)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            if (columns == null) throw new ArgumentNullException("columns");

            boundDocument = doc;

            SuspendLayout();
            try
            {
                // Reset state from any previous bind.
                VirtualMode = false;
                RowCount = 0;
                Rows.Clear();
                Columns.Clear();

                var colArray = new DataGridViewColumn[columns.Count];
                for (int i = 0; i < columns.Count; i++)
                {
                    colArray[i] = columns[i];
                }
                Columns.AddRange(colArray);

                if (doc.Rows.Count >= VirtualRowThreshold)
                {
                    // Plan 09-06 Task 4: VirtualMode fallback for large tables (the 09-03 measurement).
                    // Do NOT materialize a row per model row — set RowCount and serve values on demand.
                    IsVirtual = true;
                    VirtualMode = true;
                    RowCount = doc.Rows.Count;
                }
                else
                {
                    IsVirtual = false;
                    for (int r = 0; r < doc.Rows.Count; r++)
                    {
                        int rowIndex = Rows.Add();
                        DataGridViewRow gridRow = Rows[rowIndex];
                        // CR-01: stamp the backing model row index onto each grid row. The view-only
                        // sort (SortMode.Automatic on a non-databound grid) PHYSICALLY reorders the
                        // DataGridViewRow collection, so the visual index diverges from the model
                        // index after a header-click sort. Every model access translates the visual
                        // index back through this Tag (see ToModelRowIndex) so an edit/remove/move can
                        // never land on the wrong model row.
                        gridRow.Tag = r;
                        MutableDataTableRow modelRow = doc.Rows[r];
                        for (int c = 0; c < doc.Columns.Count && c < gridRow.Cells.Count; c++)
                        {
                            gridRow.Cells[c].Value = ProjectCellValue(modelRow.Cells[c], doc.Columns[c]);
                        }
                    }
                }
            }
            finally
            {
                ResumeLayout();
            }
        }

        /// <summary>
        /// Translates a VISUAL grid row index to its backing MODEL row index (CR-01). With the
        /// non-virtual view-only sort, <c>SortMode.Automatic</c> physically reorders the rows, so the
        /// visual index no longer equals the model index — each row carries its model index in
        /// <see cref="DataGridViewRow.Tag"/> (set in <see cref="BindMutable"/>). VirtualMode is never
        /// auto-sorted (DataGridView does not sort virtual rows without a manual handler), so the
        /// identity mapping holds there. Returns the input unchanged if no tag is present (defensive).
        /// </summary>
        public int ToModelRowIndex(int visualRowIndex)
        {
            if (visualRowIndex < 0) return visualRowIndex;
            if (IsVirtual) return visualRowIndex;
            if (visualRowIndex < Rows.Count && Rows[visualRowIndex].Tag is int modelIndex) return modelIndex;
            return visualRowIndex;
        }

        /// <summary>
        /// Inverse of <see cref="ToModelRowIndex"/>: finds the current VISUAL grid row index showing the
        /// given MODEL row (CR-01). Used by navigation paths (Find-jump, focus-cell, frozen-comment row)
        /// that hold a model index and need to select/style the row at its post-sort visual position.
        /// Returns -1 when the model row is not currently materialized.
        /// </summary>
        public int ToVisualRowIndex(int modelRowIndex)
        {
            if (modelRowIndex < 0) return modelRowIndex;
            if (IsVirtual) return modelRowIndex;
            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].Tag is int t && t == modelRowIndex) return i;
            }
            return -1;
        }

        // VirtualMode read: serve the cell's display value from the backing model on demand (only
        // invoked when VirtualMode == true; the non-virtual path sets Value directly).
        private void OnCellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (boundDocument == null) return;
            if (e.RowIndex < 0 || e.RowIndex >= boundDocument.Rows.Count) return;
            if (e.ColumnIndex < 0 || e.ColumnIndex >= boundDocument.Columns.Count) return;

            MutableDataTableCell cell = boundDocument.Rows[e.RowIndex].Cells[e.ColumnIndex];
            e.Value = ProjectCellValue(cell, boundDocument.Columns[e.ColumnIndex]);
        }

        // VirtualMode write: route the edited value back through the host's controller-backed commit
        // callback so the edit joins the undo/redo stack (parity with the non-virtual CommitCell path).
        private void OnCellValuePushed(object sender, DataGridViewCellValueEventArgs e)
        {
            if (boundDocument == null || virtualCommitCallback == null) return;
            if (e.RowIndex < 0 || e.RowIndex >= boundDocument.Rows.Count) return;
            if (e.ColumnIndex < 0 || e.ColumnIndex >= boundDocument.Columns.Count) return;

            string raw = e.Value == null ? string.Empty : e.Value.ToString();
            virtualCommitCallback(e.RowIndex, e.ColumnIndex, raw);
        }

        /// <summary>
        /// Re-renders cell-state visuals after a model edit (Plan 09-04 calls this from
        /// <c>FormDatatableEditor.OnEditApplied</c>). A full <see cref="Control.Invalidate()"/> is
        /// sufficient for V1 non-virtual mode — the underlying cell values are already set by the
        /// commit-back path; this forces the <c>CellFormatting</c> overlays to re-evaluate.
        /// </summary>
        public void RefreshMutable(MutableDataTableDocument doc)
        {
            boundDocument = doc;
            Invalidate();
        }

        /// <summary>
        /// Projects a backing cell value to the column's display representation: bool for a
        /// checkbox column, the enum label resolved against <see cref="DataTableColumnType.EnumMap"/>
        /// for a combo column, the stored int32 for DT_HashString (item 5 — the cell text is the
        /// int32 by default), otherwise the value's string form.
        /// </summary>
        private static object ProjectCellValue(MutableDataTableCell cell, MutableDataTableColumn column)
        {
            DataTableColumnType ct = column.ColumnType;
            switch (ct.Type)
            {
                case DataType.Bool:
                    return cell.Value.ToString() == "1";
                case DataType.Enum:
                    return EnumLabelForValue(ct, cell.Value.ToString());
                default:
                    return cell.Value.ToString();
            }
        }

        // Resolve the enum label whose mapped int equals the stored value; fall back to the raw
        // stored value if no label matches (so a malformed cell still renders text).
        private static string EnumLabelForValue(DataTableColumnType ct, string storedValue)
        {
            int stored;
            if (int.TryParse(storedValue, out stored))
            {
                foreach (KeyValuePair<string, int> kv in ct.EnumMap)
                {
                    if (kv.Value == stored)
                    {
                        return kv.Key;
                    }
                }
            }
            return storedValue;
        }

        // ── Cell-state visual overlays (09-UI-SPEC § Cell-state visual overlays) ──
        //
        // Dirty / added-row / needs-review overlays are painted here. Search-match (Plan 09-06)
        // and frozen-DT_Comment-row visuals (Plan 09-06) are intentionally NOT wired yet — the
        // formatter has hooks for them but takes no action until those plans land.
        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (boundDocument == null) return;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (e.ColumnIndex >= boundDocument.Columns.Count) return;

            // CR-01: the overlays (dirty / needs-review / search / frozen-comment) are keyed by MODEL
            // row index, but CellFormatting reports the VISUAL row index — translate through the row
            // Tag so a sorted view styles the correct backing cell.
            int modelRowIndex = ToModelRowIndex(e.RowIndex);
            if (modelRowIndex < 0 || modelRowIndex >= boundDocument.Rows.Count) return;

            MutableDataTableRow row = boundDocument.Rows[modelRowIndex];
            MutableDataTableCell cell = row.Cells[e.ColumnIndex];

            // Frozen DT_Comment header row (Plan 09-06): dimmed treatment, takes precedence over the
            // dirty/added accents (a frozen comment row is not user-edited).
            if (frozenCommentRowIndex >= 0 && modelRowIndex == frozenCommentRowIndex)
            {
                e.CellStyle.BackColor = Colors.PrimaryShadow();
                e.CellStyle.ForeColor = Colors.FontDisabled();
                return;
            }

            // Search-match overlay (Plan 09-06): accent background for matched cells; a stronger accent
            // for the active match. Takes precedence over the dirty/added foreground tints so the user
            // can see what Find landed on.
            if (searchMatchKeys != null && searchMatchKeys.Count > 0)
            {
                long key = CellKey(modelRowIndex, e.ColumnIndex);
                if (key == activeMatchKey)
                {
                    e.CellStyle.BackColor = Colors.Secondary();
                    e.CellStyle.ForeColor = Colors.Font();
                    return;
                }
                if (searchMatchKeys.Contains(key))
                {
                    e.CellStyle.BackColor = Colors.PrimaryHighlight();
                    e.CellStyle.SelectionBackColor = Colors.Secondary();
                    e.CellStyle.ForeColor = Colors.Secondary();
                    return;
                }
            }

            if (cell.NeedsReview)
            {
                // Needs review (type-change cascade, Plan 09-04): destructive-color background.
                e.CellStyle.BackColor = Color.Red;
                e.CellStyle.ForeColor = Colors.Font();
                return;
            }

            if (row.IsAdded)
            {
                // Added row: accent foreground (＋ row-header glyph is applied by the host).
                e.CellStyle.ForeColor = Colors.Secondary();
                return;
            }

            if (cell.IsDirty)
            {
                // Dirty (edited): accent foreground (● row-header glyph is applied by the host).
                e.CellStyle.ForeColor = Colors.Secondary();
            }
        }
    }
}
