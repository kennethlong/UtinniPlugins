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
using UtinniCoreDotNet.UI.Forms;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Forms
{
    /// <summary>
    /// Per-call modal preview for a CSV/TSV delta-import (Plan 09-06 D-08, UI-SPEC assumption #7).
    /// Shows the locked D-08 summary copy + a per-column diff grid (column name / DT_Type / rows
    /// touched) + a red invalid-rows list, with Import / Cancel buttons. Apply happens in the caller
    /// (<see cref="TJT.Saving.DatatableCsvSerializer.ImportAsync"/>) as a single transaction.
    ///
    /// <para><b>Per-call modal lifecycle (RESEARCH § Pitfall 6 — NOT hide-not-dispose):</b>
    /// <c>using (var dlg = new FormCsvImportPreviewDialog(plan, csv, tab)) { dlg.ShowDialog(parent); }</c>.
    /// Default WinForms dispose-on-close is CORRECT here (not a singleton GetForms() instance).</para>
    /// </summary>
    public partial class FormCsvImportPreviewDialog : UtinniForm
    {
        private readonly CsvImportPlan plan;
        private readonly string csvFileName;
        private readonly string tabFileName;

        public FormCsvImportPreviewDialog(CsvImportPlan plan, string csvFileName, string tabFileName)
        {
            InitializeComponent();

            this.plan = plan ?? throw new ArgumentNullException("plan");
            this.csvFileName = csvFileName ?? "CSV";
            this.tabFileName = tabFileName ?? "datatable";

            this.BackColor = Colors.Primary();
            pnlFooter.BackColor = Colors.Primary();
            lblHeading.ForeColor = Colors.Font();
            lblBody.ForeColor = Colors.Font();

            ApplyHeading();
            ApplyBody();
            BuildPerColumnGrid();
            BuildInvalidList();

            btnImport.Click += (s, e) => { this.DialogResult = DialogResult.OK; this.Close(); };
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
        }

        private void ApplyHeading()
        {
            // LOCKED copy (UI-SPEC § Copywriting): "Import {csvFileName} into {tabFileName}?"
            lblHeading.Text = "Import " + csvFileName + " into " + tabFileName + "?";
        }

        private void ApplyBody()
        {
            // LOCKED D-08 wording (UI-SPEC § Copywriting / § Destructive).
            int n = plan.Changes.Count;
            int m = plan.Unchanged.Count;
            int k = plan.Invalid.Count;
            lblBody.Text = n + " cells will change. " + m + " cells will stay as original bytes. "
                + k + " cells in the CSV would be type-invalid and will be skipped (see list below).";
        }

        private void BuildPerColumnGrid()
        {
            gridPerColumnDiff.AllowUserToAddRows = false;
            gridPerColumnDiff.AllowUserToDeleteRows = false;
            gridPerColumnDiff.ReadOnly = true;
            gridPerColumnDiff.RowHeadersVisible = false;

            gridPerColumnDiff.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Column", Name = "colName", Width = 220 });
            gridPerColumnDiff.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Type", Name = "colType", Width = 140 });
            gridPerColumnDiff.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Rows touched", Name = "colTouched", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

            // Aggregate Changes by column → rows-touched count + carry a display name/type.
            var byColumn = new Dictionary<int, int>();
            var colName = new Dictionary<int, string>();
            var colType = new Dictionary<int, string>();
            for (int i = 0; i < plan.Changes.Count; i++)
            {
                EditCellPatch p = plan.Changes[i];
                int touched;
                byColumn.TryGetValue(p.Col, out touched);
                byColumn[p.Col] = touched + 1;
                if (!colName.ContainsKey(p.Col)) colName[p.Col] = "col " + p.Col;
                if (!colType.ContainsKey(p.Col)) colType[p.Col] = "";
            }

            // Per-column row copy (UI-SPEC): "{columnName} ({DT_Type}): {touched} rows touched".
            foreach (KeyValuePair<int, int> kv in byColumn)
            {
                string name = colName.ContainsKey(kv.Key) ? colName[kv.Key] : "col " + kv.Key;
                string type = colType.ContainsKey(kv.Key) ? colType[kv.Key] : "";
                gridPerColumnDiff.Rows.Add(name, type, kv.Value + " rows touched");
            }
        }

        private void BuildInvalidList()
        {
            lvInvalidRows.View = View.Details;
            lvInvalidRows.FullRowSelect = true;
            lvInvalidRows.Columns.Add("Invalid rows (skipped)", lvInvalidRows.Width - 24);
            lvInvalidRows.ForeColor = Color.Red;

            for (int i = 0; i < plan.Invalid.Count; i++)
            {
                InvalidCellEntry e = plan.Invalid[i];
                // LOCKED copy: Row {rowIndex}, column {columnName}: "{csvValue}" cannot convert to {DT_Type}
                string line = "Row " + e.Row + ", column " + e.ColumnName + ": \"" + e.CsvValue
                    + "\" cannot convert to " + e.Reason;
                lvInvalidRows.Items.Add(new ListViewItem(line));
            }
        }
    }
}
