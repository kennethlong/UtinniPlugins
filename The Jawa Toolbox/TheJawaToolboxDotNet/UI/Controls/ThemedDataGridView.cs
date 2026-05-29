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
        }

        /// <summary>The document currently bound to the grid (null before the first bind).</summary>
        public MutableDataTableDocument BoundDocument
        {
            get { return boundDocument; }
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
                Rows.Clear();
                Columns.Clear();

                var colArray = new DataGridViewColumn[columns.Count];
                for (int i = 0; i < columns.Count; i++)
                {
                    colArray[i] = columns[i];
                }
                Columns.AddRange(colArray);

                for (int r = 0; r < doc.Rows.Count; r++)
                {
                    int rowIndex = Rows.Add();
                    DataGridViewRow gridRow = Rows[rowIndex];
                    MutableDataTableRow modelRow = doc.Rows[r];
                    for (int c = 0; c < doc.Columns.Count && c < gridRow.Cells.Count; c++)
                    {
                        gridRow.Cells[c].Value = ProjectCellValue(modelRow.Cells[c], doc.Columns[c]);
                    }
                }
            }
            finally
            {
                ResumeLayout();
            }
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
            if (e.RowIndex < 0 || e.RowIndex >= boundDocument.Rows.Count) return;
            if (e.ColumnIndex < 0 || e.ColumnIndex >= boundDocument.Columns.Count) return;

            MutableDataTableRow row = boundDocument.Rows[e.RowIndex];
            MutableDataTableCell cell = row.Cells[e.ColumnIndex];

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
