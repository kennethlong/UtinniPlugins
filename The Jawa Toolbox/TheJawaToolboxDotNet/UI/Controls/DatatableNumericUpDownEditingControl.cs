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
using System.Globalization;
using System.Windows.Forms;
using UtinniCoreDotNet.UI.Controls;

namespace TJT.UI.Controls
{
    /// <summary>
    /// A <see cref="UtinniNumericUpDown"/> adapted to the BCL <see cref="IDataGridViewEditingControl"/>
    /// contract so it can be swapped in as the cell editor for DT_Int / DT_Float columns via the
    /// host's <c>DataGridView.EditingControlShowing</c> handler. The standard
    /// <see cref="IDataGridViewEditingControl"/> member implementation is documented in the BCL
    /// (see <c>DataGridViewNumericUpDownEditingControl</c> sample). The host sets
    /// <see cref="NumericUpDown.Minimum"/> / <see cref="NumericUpDown.Maximum"/> /
    /// <see cref="NumericUpDown.DecimalPlaces"/> / <see cref="NumericUpDown.Increment"/> per the
    /// column's <c>DataTableColumnType.Type</c> (DT_Int → DecimalPlaces 0; DT_Float → DecimalPlaces 6).
    /// </summary>
    public sealed class DatatableNumericUpDownEditingControl : UtinniNumericUpDown, IDataGridViewEditingControl
    {
        private DataGridView dataGridView;
        private bool valueChanged;
        private int rowIndex;

        public DatatableNumericUpDownEditingControl()
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
}
