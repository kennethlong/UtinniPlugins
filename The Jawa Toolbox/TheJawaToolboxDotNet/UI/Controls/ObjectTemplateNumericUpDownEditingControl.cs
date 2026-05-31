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
// Numeric cell editor for the Object Template Editor's int/float SINGLE params (Phase 11, D-02).
// The object-template analog of DatatableNumericUpDownEditingControl — a UtinniNumericUpDown adapted
// to the BCL IDataGridViewEditingControl contract so it swaps in for int/float value cells via the
// host's DataGridView.EditingControlShowing handler. Implementation original to Utinni under MIT.

using System;
using System.Globalization;
using System.Windows.Forms;
using UtinniCoreDotNet.UI.Controls;

namespace TJT.UI.Controls
{
    /// <summary>
    /// A <see cref="UtinniNumericUpDown"/> adapted to the BCL <see cref="IDataGridViewEditingControl"/>
    /// contract so it can be swapped in as the cell editor for an object-template int / float SINGLE
    /// param via the host's <c>DataGridView.EditingControlShowing</c> handler. The host sets
    /// <see cref="NumericUpDown.Minimum"/> / <see cref="NumericUpDown.Maximum"/> /
    /// <see cref="NumericUpDown.DecimalPlaces"/> / <see cref="NumericUpDown.Increment"/> per the decoded
    /// param type (int → DecimalPlaces 0; float → DecimalPlaces 6). Mirrors the Phase 9
    /// <c>DatatableNumericUpDownEditingControl</c>.
    /// </summary>
    public sealed class ObjectTemplateNumericUpDownEditingControl : UtinniNumericUpDown, IDataGridViewEditingControl
    {
        private DataGridView dataGridView;
        private bool valueChanged;
        private int rowIndex;

        public ObjectTemplateNumericUpDownEditingControl()
        {
            ValueChanged += OnValueChanged;
        }

        private void OnValueChanged(object sender, EventArgs e)
        {
            valueChanged = true;
            if (dataGridView != null)
            {
                dataGridView.NotifyCurrentCellDirty(true);
            }
        }

        // ── IDataGridViewEditingControl ──────────────────────────────────────

        public DataGridView EditingControlDataGridView
        {
            get { return dataGridView; }
            set { dataGridView = value; }
        }

        public object EditingControlFormattedValue
        {
            get { return Value.ToString(CultureInfo.CurrentCulture); }
            set
            {
                string text = value as string;
                if (text == null) return;
                decimal parsed;
                if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out parsed))
                {
                    Value = ClampToRange(parsed);
                }
            }
        }

        public int EditingControlRowIndex
        {
            get { return rowIndex; }
            set { rowIndex = value; }
        }

        public bool EditingControlValueChanged
        {
            get { return valueChanged; }
            set { valueChanged = value; }
        }

        public Cursor EditingPanelCursor
        {
            get { return base.Cursor; }
        }

        public bool RepositionEditingControlOnValueChange
        {
            get { return false; }
        }

        public object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context)
        {
            return Value.ToString(CultureInfo.CurrentCulture);
        }

        public void ApplyCellStyleToEditingControl(DataGridViewCellStyle dataGridViewCellStyle)
        {
            Font = dataGridViewCellStyle.Font;
        }

        public bool EditingControlWantsInputKey(Keys keyData, bool dataGridViewWantsInputKey)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                case Keys.Delete:
                    return true;
                default:
                    return !dataGridViewWantsInputKey;
            }
        }

        public void PrepareEditingControlForEdit(bool selectAll)
        {
            if (selectAll)
            {
                Select(0, Text.Length);
            }
        }

        private decimal ClampToRange(decimal candidate)
        {
            if (candidate < Minimum) return Minimum;
            if (candidate > Maximum) return Maximum;
            return candidate;
        }
    }

    /// <summary>
    /// A <see cref="DataGridViewTextBoxCell"/> whose <see cref="EditType"/> is the
    /// <see cref="ObjectTemplateNumericUpDownEditingControl"/> — assigned per-row to int / float
    /// Effective-value cells so the numeric widget swaps in at edit time (the host's
    /// <c>EditingControlShowing</c> handler then tunes DecimalPlaces / range per the decoded type).
    /// </summary>
    public sealed class ObjectTemplateNumericCell : System.Windows.Forms.DataGridViewTextBoxCell
    {
        public override Type EditType
        {
            get { return typeof(ObjectTemplateNumericUpDownEditingControl); }
        }

        public override Type ValueType
        {
            get { return typeof(string); }
        }

        public override object DefaultNewRowValue
        {
            get { return string.Empty; }
        }
    }
}
