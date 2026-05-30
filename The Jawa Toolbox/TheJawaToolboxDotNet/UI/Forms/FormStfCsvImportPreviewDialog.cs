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
using System.Drawing;
using System.Windows.Forms;
using UtinniCoreDotNet.Formats.StringTable;
using UtinniCoreDotNet.UI.Forms;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Forms
{
    /// <summary>
    /// Per-call modal preview for a string-table CSV/TSV delta-import (Plan 10-04 D-03b). Shows the
    /// LOCKED D-03b summary copy + a grid of changed/added entries + a red invalid-key list (F8); the
    /// Import button is DISABLED when <see cref="StringTableCsvImportPlan.HasBlockingErrors"/> (the user
    /// fixes the CSV and re-imports). Apply happens in the caller
    /// (<see cref="TJT.UI.Forms.FormStringTableEditor"/>) as a single transaction.
    ///
    /// <para><b>Per-call modal lifecycle (NOT hide-not-dispose):</b>
    /// <c>using (var dlg = new FormStfCsvImportPreviewDialog(plan, csv, stf)) { dlg.ShowDialog(parent); }</c>.
    /// Default WinForms dispose-on-close is correct (not a singleton GetForms() instance).</para>
    /// </summary>
    public partial class FormStfCsvImportPreviewDialog : UtinniForm
    {
        private readonly StringTableCsvImportPlan plan;
        private readonly string csvFileName;
        private readonly string stfFileName;

        public FormStfCsvImportPreviewDialog(StringTableCsvImportPlan plan, string csvFileName, string stfFileName)
        {
            InitializeComponent();

            this.plan = plan ?? throw new ArgumentNullException("plan");
            this.csvFileName = csvFileName ?? "CSV";
            this.stfFileName = stfFileName ?? "string table";

            this.BackColor = Colors.Primary();
            pnlFooter.BackColor = Colors.Primary();
            lblHeading.ForeColor = Colors.Font();
            lblBody.ForeColor = Colors.Font();
            lblInvalidNote.ForeColor = Color.Red;

            ApplyHeading();
            ApplyBody();
            BuildChangesGrid();
            BuildInvalidList();

            // F8: block Import entirely when any CSV key is invalid — only Cancel is available.
            if (plan.HasBlockingErrors)
            {
                btnImport.Enabled = false;
                lblInvalidNote.Visible = true;
                lblInvalidNote.Text = "Fix the " + plan.Invalid.Count + " invalid key(s) in the CSV, then re-import.";
            }

            btnImport.Click += (s, e) => { this.DialogResult = DialogResult.OK; this.Close(); };
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
        }

        private void ApplyHeading()
        {
            // LOCKED copy (10-UI-SPEC § Copywriting): "Import {csvFileName} into {stfFileName}?"
            lblHeading.Text = "Import " + csvFileName + " into " + stfFileName + "?";
        }

        private void ApplyBody()
        {
            // LOCKED D-03b wording (10-UI-SPEC § Copywriting / § Destructive).
            int n = plan.Changes.Count + plan.Added.Count;
            int m = plan.Unchanged.Count;
            lblBody.Text = n + " entries will change. " + m + " entries will stay as original bytes. "
                + "New keys in the CSV will be added; missing keys are left untouched.";
        }

        private void BuildChangesGrid()
        {
            gridChanges.AllowUserToAddRows = false;
            gridChanges.AllowUserToDeleteRows = false;
            gridChanges.ReadOnly = true;
            gridChanges.RowHeadersVisible = false;

            gridChanges.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Key", Name = "colKey", Width = 240 });
            gridChanges.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Change", Name = "colChange", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

            foreach (StringTableEditPatch p in plan.Changes)
            {
                gridChanges.Rows.Add(p.Entry.Name, "text changes");
            }
            foreach (StringTableAddedEntry a in plan.Added)
            {
                gridChanges.Rows.Add(a.Key, "new entry");
            }
        }

        private void BuildInvalidList()
        {
            lvInvalidRows.View = View.Details;
            lvInvalidRows.FullRowSelect = true;
            lvInvalidRows.Columns.Add("Invalid keys (must fix before import)", lvInvalidRows.Width - 24);
            lvInvalidRows.ForeColor = Color.Red;

            foreach (StringTableInvalidRow r in plan.Invalid)
            {
                string line = "Row " + r.RowIndex + ": \"" + r.Key + "\" — " + r.Reason;
                lvInvalidRows.Items.Add(new ListViewItem(line));
            }
        }
    }
}
