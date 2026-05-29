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
    /// Small per-call modal for adding a column to a datatable (Plan 09-04). Collects a column NAME +
    /// a DT_* TYPE and returns them via <see cref="Result"/> on Apply. Mirrors the small-input-modal
    /// shape of <see cref="FormFourCcDialog"/> (one input + Add/Cancel buttons), extended with a type
    /// combo and a validation status label.
    ///
    /// <para><b>Per-call modal lifecycle (RESEARCH § Pitfall 6):</b> instantiate per call site via
    /// <c>using (var dlg = new FormAddColumnDialog(existingNames)) { if (dlg.ShowDialog(parent) ==
    /// DialogResult.OK) ... }</c>. Default WinForms dispose-on-close is CORRECT here — this dialog is
    /// NOT registered with the plugin host's GetForms() list, so the 08-05 hide-not-dispose singleton
    /// pattern does NOT apply.</para>
    /// </summary>
    public partial class FormAddColumnDialog : UtinniForm
    {
        /// <summary>The (name, type) result the user committed; valid only after a DialogResult.OK return.</summary>
        public sealed class AddColumnResult
        {
            /// <summary>The new column's name.</summary>
            public string Name { get; internal set; }

            /// <summary>The new column's parsed type.</summary>
            public DataTableColumnType ColumnType { get; internal set; }
        }

        // Maps each combo entry to the canonical default type-spec the user can refine later via the
        // grid's Change-column-type action (richer type-spec authoring is V2 — UI-SPEC R-03).
        private static readonly KeyValuePair<string, string>[] TypeChoices =
        {
            new KeyValuePair<string, string>("DT_Int", "i"),
            new KeyValuePair<string, string>("DT_Float", "f"),
            new KeyValuePair<string, string>("DT_String", "s"),
            new KeyValuePair<string, string>("DT_Bool", "b"),
            new KeyValuePair<string, string>("DT_Enum", "e(value=0)[value]"),
            new KeyValuePair<string, string>("DT_HashString", "h"),
            new KeyValuePair<string, string>("DT_PackedObjVars", "p"),
            new KeyValuePair<string, string>("DT_BitVector", "v(bit=1)[NONE]"),
            new KeyValuePair<string, string>("DT_Comment", "c"),
            new KeyValuePair<string, string>("DT_Unknown", "z"),
        };

        private readonly HashSet<string> existingNames;

        /// <summary>The committed result; null until Apply succeeds.</summary>
        public AddColumnResult Result { get; private set; }

        public FormAddColumnDialog(IEnumerable<string> existingColumnNames)
        {
            InitializeComponent();

            existingNames = new HashSet<string>(StringComparer.Ordinal);
            if (existingColumnNames != null)
            {
                foreach (string n in existingColumnNames)
                {
                    if (n != null) existingNames.Add(n);
                }
            }

            base.Text = "Add column";

            foreach (KeyValuePair<string, string> choice in TypeChoices)
            {
                cmbColumnType.Items.Add(choice.Key);
            }
            cmbColumnType.SelectedIndex = 0;

            // Themed surfaces via Colors.*() accessors only — no ARGB literals.
            this.BackColor = Colors.Primary();
            lblStatus.ForeColor = Colors.Secondary();

            btnAdd.Click += OnAddClicked;
        }

        private void OnAddClicked(object sender, EventArgs e)
        {
            string name = txtColumnName.Text != null ? txtColumnName.Text.Trim() : string.Empty;

            if (name.Length == 0)
            {
                ShowError("Column name must be non-empty.");
                return;
            }

            if (existingNames.Contains(name))
            {
                ShowError("A column named \"" + name + "\" already exists. Pick another.");
                return;
            }

            string spec = TypeChoices[cmbColumnType.SelectedIndex >= 0 ? cmbColumnType.SelectedIndex : 0].Value;
            DataTableColumnType ct;
            try
            {
                ct = new DataTableColumnType(spec);
            }
            catch (Exception ex)
            {
                ShowError("Could not build column type: " + ex.Message);
                return;
            }

            Result = new AddColumnResult { Name = name, ColumnType = ct };
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void ShowError(string message)
        {
            lblStatus.Text = message;
            lblStatus.ForeColor = Color.Red;
        }
    }
}
