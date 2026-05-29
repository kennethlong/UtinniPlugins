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
using UtinniCoreDotNet.Formats.Datatable;
using UtinniCoreDotNet.UI.Controls;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Controls
{
    /// <summary>
    /// Floating hash-preview overlay anchored to a DT_HashString cell while it is in edit mode.
    /// NEW pattern — no in-repo analog. 09-UI-SPEC § Per-type cell widget contract row
    /// <c>DT_HashString</c> is the authoritative spec.
    ///
    /// <para><b>Int-vs-source UX (iter-2 item 5):</b> a loaded <c>.tab</c> stores only the int32
    /// hash on disk, so the cell DISPLAYS the stored int32 by default. While editing, the user may
    /// type a SOURCE STRING; the floating label shows <c>{source} → 0x{hash:X8}</c> so the
    /// int-vs-source distinction is explicit. On commit, the host writes the int32 hash of the typed
    /// source string. The source string itself is NOT persisted (only the int32 is on disk); the
    /// editor never pretends to recover a source string for an already-hashed loaded value.</para>
    ///
    /// <remarks>
    /// <b>Deterministic floating-label placement policy (iter-1 fix #7):</b> anchor the label to the
    /// RIGHT of the editing cell rect by default with a 4px gap (<c>label.Left = cellRect.Right +
    /// 4</c>). If <c>cellRect.Right + labelWidth + 4 &gt; gridSurface.ClientRectangle.Right</c>,
    /// anchor BELOW the cell rect with a 2px gap (<c>label.Top = cellRect.Bottom + 2</c>). Clip
    /// <c>label.Left</c> to <c>Math.Min(label.Left, gridSurface.ClientRectangle.Right - labelWidth -
    /// 4)</c> to keep the label visible. Never anchor LEFT or ABOVE the cell. Z-order:
    /// <c>label.BringToFront()</c> so it overlays cell-state overlays.
    /// </remarks>
    /// </summary>
    public sealed class DatatableHashStringEditor : IDisposable
    {
        private const int LabelWidth = 220;
        private const int LabelHeight = 18;

        private readonly Control parent;
        private readonly UtinniLabel label;
        private bool disposed;

        /// <summary>
        /// Creates the floating preview anchored to the given cell rect. <paramref name="host"/> is
        /// the grid (used for client-bounds clipping); <paramref name="parent"/> is the control the
        /// label is added to as a sibling (typically the grid itself or its container);
        /// <paramref name="cellRect"/> is the editing cell's display rectangle in
        /// <paramref name="parent"/>-relative coordinates.
        /// </summary>
        public DatatableHashStringEditor(Control host, Control parent, Rectangle cellRect)
        {
            if (host == null) throw new ArgumentNullException("host");
            if (parent == null) throw new ArgumentNullException("parent");
            this.parent = parent;

            label = new UtinniLabel
            {
                AutoSize = false,
                Width = LabelWidth,
                Height = LabelHeight,
                Font = new Font("Consolas", 9F),
                ForeColor = Colors.FontDisabled(),
                BackColor = Colors.PrimaryShadow(),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "→ 0x00000000"
            };

            PlaceLabel(host, cellRect);

            parent.Controls.Add(label);
            label.BringToFront();
        }

        // Deterministic placement per the iter-1 fix #7 policy documented in the class remarks.
        private void PlaceLabel(Control host, Rectangle cellRect)
        {
            Rectangle clientBounds = host.ClientRectangle;

            int left = cellRect.Right + 4;
            int top = cellRect.Top;

            if (cellRect.Right + LabelWidth + 4 > clientBounds.Right)
            {
                // Not enough horizontal room — anchor below the cell rect.
                top = cellRect.Bottom + 2;
                left = cellRect.Left;
            }

            // Clip so the label stays visible within the host client area.
            left = Math.Min(left, clientBounds.Right - LabelWidth - 4);
            if (left < clientBounds.Left) left = clientBounds.Left;

            label.Location = new Point(left, top);
        }

        /// <summary>
        /// Recomputes the preview from the entered SOURCE STRING — subscribe this to the
        /// editing control's text-changed event (the host wires it in <c>CellBeginEdit</c>).
        /// </summary>
        public void Update(string sourceText)
        {
            if (disposed) return;
            uint hash = DataTableHashCrc.Compute(sourceText ?? string.Empty);
            string src = string.IsNullOrEmpty(sourceText) ? string.Empty : sourceText + " ";
            label.Text = src + "→ 0x" + hash.ToString("X8");
        }

        /// <summary>Removes the floating label from its parent and disposes it.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (parent != null && !parent.IsDisposed)
            {
                parent.Controls.Remove(label);
            }
            label.Dispose();
        }
    }
}
