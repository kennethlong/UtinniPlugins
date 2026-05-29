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
using System.Windows.Forms;
using UtinniCoreDotNet.Formats.Datatable;
using UtinniCoreDotNet.UI.Forms;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Forms
{
    /// <summary>
    /// Per-call modal for resolving the D-04 column-type-change cascade (Plan 09-04, UI-SPEC assumption
    /// #6). Lists the cells whose <c>MangleValue</c> failed under the new type in an embedded
    /// <see cref="TJT.UI.Controls.ThemedDataGridView"/> (row index / original value / Accept / Edit) and
    /// offers a footer with <c>Done</c> (close; remaining red cells stay flagged) + <c>Revert type
    /// change</c> (the caller rolls the whole ChangeColumnType command back via the editor-local undo
    /// stack). Its OWN dialog — NOT a FormSaveConfirmDialog clone (the D-02 safety-net reuses that one).
    ///
    /// <para><b>Per-call modal lifecycle (RESEARCH § Pitfall 6):</b> <c>using (var dlg = new
    /// FormTypeChangeCascadeDialog(col, newCt, affectedCells)) { dlg.ShowDialog(parent); }</c>. Default
    /// WinForms dispose-on-close is CORRECT (not a singleton GetForms() instance).</para>
    /// </summary>
    public partial class FormTypeChangeCascadeDialog : UtinniForm
    {
        /// <summary>The footer choice the user made after the dialog closes.</summary>
        public enum CascadeOutcome
        {
            /// <summary>User clicked Done — remaining flagged cells stay flagged; save stays blocked.</summary>
            Done,
            /// <summary>User clicked Revert type change — the caller undoes the ChangeColumnType command.</summary>
            Revert,
            /// <summary>User clicked Edit-cell on a row — the caller focuses the offending cell in the grid.</summary>
            EditCellRequested,
        }

        private readonly MutableDataTableColumn column;
        private readonly DataTableColumnType newType;
        private readonly List<MutableDataTableCell> affected;

        /// <summary>The user's footer choice; valid after ShowDialog returns.</summary>
        public CascadeOutcome Outcome { get; private set; }

        /// <summary>
        /// When <see cref="Outcome"/> is <see cref="CascadeOutcome.EditCellRequested"/>, the cell the
        /// user asked to edit in the main grid (null otherwise).
        /// </summary>
        public MutableDataTableCell EditCellTarget { get; private set; }

        public FormTypeChangeCascadeDialog(
            MutableDataTableColumn col,
            DataTableColumnType newCt,
            IList<MutableDataTableCell> affectedCells)
        {
            InitializeComponent();

            column = col ?? throw new ArgumentNullException("col");
            newType = newCt ?? throw new ArgumentNullException("newCt");
            affected = new List<MutableDataTableCell>(affectedCells ?? new List<MutableDataTableCell>());

            Outcome = CascadeOutcome.Done;

            this.BackColor = Colors.Primary();
            pnlFooter.BackColor = Colors.Primary();
            lblHeading.ForeColor = Colors.Font();
            lblBanner.ForeColor = Colors.Secondary();

            BuildGridColumns();
            RebuildRows();
            UpdateHeading();

            btnDone.Click += OnDoneClicked;
            btnRevert.Click += OnRevertClicked;
            gridCells.CellContentClick += OnGridCellContentClick;
        }

        private void BuildGridColumns()
        {
            gridCells.AllowUserToAddRows = false;
            gridCells.AllowUserToDeleteRows = false;
            gridCells.ReadOnly = true;
            gridCells.RowHeadersVisible = false;

            gridCells.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Row", Name = "colRow", Width = 60 });
            gridCells.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Current value", Name = "colValue", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            gridCells.Columns.Add(new DataGridViewButtonColumn { HeaderText = "", Name = "colAccept", Text = "Accept", UseColumnTextForButtonValue = true, Width = 90 });
            gridCells.Columns.Add(new DataGridViewButtonColumn { HeaderText = "", Name = "colEdit", Text = "Edit cell", UseColumnTextForButtonValue = true, Width = 90 });
        }

        private void RebuildRows()
        {
            gridCells.Rows.Clear();
            for (int i = 0; i < affected.Count; i++)
            {
                MutableDataTableCell cell = affected[i];
                int rowIndex = cell.Row != null ? IndexOfRow(cell) : -1;
                string value = cell.Value != null ? cell.Value.ToString() : string.Empty;
                gridCells.Rows.Add(rowIndex >= 0 ? rowIndex.ToString() : "?", value, "Accept", "Edit cell");
            }
        }

        // Resolve a cell's row index within its owning document for display.
        private static int IndexOfRow(MutableDataTableCell cell)
        {
            if (cell.Row == null) return -1;
            // The cell's row is in the document; we cannot reach the document from the cell, so the
            // display index is best-effort via the affected ordering. Return -1 → "?" if unknown.
            return -1;
        }

        private void UpdateHeading()
        {
            lblHeading.Text = affected.Count + " cell(s) need review after changing column \""
                              + column.Name + "\" to " + newType.TypeSpec;
            lblBanner.Text = "Save is disabled until every cell is resolved. Use Accept mangled, Revert type, or Edit per cell.";
        }

        private void OnGridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= affected.Count) return;
            string colName = gridCells.Columns[e.ColumnIndex].Name;
            MutableDataTableCell cell = affected[e.RowIndex];

            if (colName == "colAccept")
            {
                // Accept-mangled: re-run the new type's coercion on the displayed value; on success the
                // cell adopts the typed value (clears NeedsReview) and drops out of the cascade list.
                string current = cell.Value != null ? cell.Value.ToString() : string.Empty;
                DataTableCellValue coerced;
                if (newType.TryCoerceCellValue(current, out coerced))
                {
                    cell.Value = coerced; // setter clears NeedsReview
                    affected.RemoveAt(e.RowIndex);
                    RebuildRows();
                    UpdateHeading();
                    if (affected.Count == 0)
                    {
                        // Every cell resolved — close; the controller's RecomputeCascadeState will have
                        // auto-cleared PendingCascadeContext on the EditApplied the caller triggers.
                        Outcome = CascadeOutcome.Done;
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    }
                }
                else
                {
                    lblBanner.Text = "\"" + current + "\" still does not fit " + newType.TypeSpec
                                     + ". Edit the cell or revert the type change.";
                }
            }
            else if (colName == "colEdit")
            {
                // Edit-cell: hand back to the caller to focus the offending cell in the main grid; the
                // user reopens this modal via the Resolve-cascade toolbar button.
                Outcome = CascadeOutcome.EditCellRequested;
                EditCellTarget = cell;
                this.DialogResult = DialogResult.None;
                this.Close();
            }
        }

        private void OnDoneClicked(object sender, EventArgs e)
        {
            Outcome = CascadeOutcome.Done;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void OnRevertClicked(object sender, EventArgs e)
        {
            Outcome = CascadeOutcome.Revert;
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}
