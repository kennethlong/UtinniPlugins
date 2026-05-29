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

using System.Windows.Forms;
using UtinniCoreDotNet.Formats.Datatable;

namespace TJT.UI.Controls
{
    /// <summary>
    /// Maps a <see cref="DataTableColumnType"/> to the concrete <see cref="DataGridViewColumn"/>
    /// subclass that renders + edits its cells, per 09-UI-SPEC § Per-type cell widget contract
    /// (and 09-RESEARCH § Pattern 3). The DT_Int / DT_Float editor swap-in to
    /// <see cref="DatatableNumericUpDownEditingControl"/> happens at edit time via the host's
    /// <c>EditingControlShowing</c> handler — this factory returns a plain
    /// <see cref="DataGridViewTextBoxColumn"/> for those numeric columns.
    /// </summary>
    public static class DatatableColumnFactory
    {
        // PackedObjVars syntax hint surfaced as the column header tooltip (09-UI-SPEC § Per-type
        // cell widget contract Copywriting row).
        private const string PackedObjVarsHint = "Packed object-vars syntax: name|type|value|…|$|";

        // BitVector syntax hint surfaced as the column header tooltip.
        private const string BitVectorHint = "Bit-vector syntax: a list [bit0,bit2,…] or comma-separated labels.";

        // Read-only hint for DT_Unknown / cross-table-enum columns (UI-SPEC R-03).
        private const string UnknownHint = "Unknown column type — read-only.";

        /// <summary>
        /// Builds the per-type column for the given column name + parsed type. Switches on
        /// <see cref="DataTableColumnType.Type"/> (the rich type, not the on-disk basic type).
        /// </summary>
        public static DataGridViewColumn Build(string columnName, DataTableColumnType ct)
        {
            DataGridViewColumn column;
            switch (ct.Type)
            {
                case DataType.Bool:
                    column = new DataGridViewCheckBoxColumn();
                    break;

                case DataType.Enum:
                {
                    var combo = new DataGridViewComboBoxColumn();
                    foreach (var label in ct.EnumMap.Keys)
                    {
                        combo.Items.Add(label);
                    }
                    column = combo;
                    break;
                }

                case DataType.PackedObjVars:
                    column = new DataGridViewTextBoxColumn { ToolTipText = PackedObjVarsHint };
                    break;

                case DataType.BitVector:
                    column = new DataGridViewTextBoxColumn { ToolTipText = BitVectorHint };
                    break;

                case DataType.Unknown:
                    // UI-SPEC R-03 — read-only hex/text render for unsupported / cross-table-enum.
                    column = new DataGridViewTextBoxColumn { ReadOnly = true, ToolTipText = UnknownHint };
                    break;

                // DT_Int / DT_Float (NumericUpDown swap-in at edit time), DT_String, DT_Comment,
                // DT_HashString (cell text is the stored int32; floating preview wired by the host).
                default:
                    column = new DataGridViewTextBoxColumn();
                    break;
            }

            column.Name = columnName;
            column.HeaderText = columnName;
            return column;
        }
    }
}
