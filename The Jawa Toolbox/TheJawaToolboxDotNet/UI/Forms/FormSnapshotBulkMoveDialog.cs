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
// 15-01 bulk-move input modal (non-destructive, fully undoable → NO red confirm, per 15-UI-SPEC
// § Destructive). Δ X/Y/Z deltas applied to the N selected placements via WorldSnapshotImpl.BulkMove.

using System;
using System.Drawing;
using System.Windows.Forms;
using UtinniCoreDotNet.UI.Controls;
using UtinniCoreDotNet.UI.Forms;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Forms
{
    /// <summary>Per-call modal collecting the Δ X/Y/Z move delta for a bulk move of N placements.</summary>
    public class FormSnapshotBulkMoveDialog : UtinniForm
    {
        private readonly UtinniNumericUpDown nudX;
        private readonly UtinniNumericUpDown nudY;
        private readonly UtinniNumericUpDown nudZ;

        public float DeltaX { get; private set; }
        public float DeltaY { get; private set; }
        public float DeltaZ { get; private set; }

        public FormSnapshotBulkMoveDialog(int count)
        {
            Text = "Move " + count + " placements";
            DrawName = true;
            MaximizeBox = false;
            MinimizeBox = false;
            Resizable = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(280, 130);
            BackColor = Colors.Primary();

            nudX = MakeNud(70);
            nudY = MakeNud(70);
            nudZ = MakeNud(70);

            var lblX = MakeLabel("Δ X", 10, 35);
            var lblY = MakeLabel("Δ Y", 10, 62);
            var lblZ = MakeLabel("Δ Z", 10, 89);

            nudX.Location = new Point(50, 33);
            nudY.Location = new Point(50, 60);
            nudZ.Location = new Point(50, 87);

            var lblHeading = MakeLabel("Move the selected placements by:", 10, 10);
            lblHeading.AutoSize = false;
            lblHeading.Size = new Size(260, 18);

            var btnApply = new UtinniButton
            {
                Text = "Apply",
                DialogResult = DialogResult.OK,
                Location = new Point(195, 100),
                Size = new Size(75, 22),
                UseVisualStyleBackColor = true,
            };
            btnApply.Click += (s, e) =>
            {
                DeltaX = (float)nudX.Value;
                DeltaY = (float)nudY.Value;
                DeltaZ = (float)nudZ.Value;
            };

            var btnCancel = new UtinniButton
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(115, 100),
                Size = new Size(75, 22),
                UseVisualStyleBackColor = true,
            };

            AcceptButton = btnApply;
            CancelButton = btnCancel;

            Controls.Add(lblHeading);
            Controls.Add(lblX);
            Controls.Add(lblY);
            Controls.Add(lblZ);
            Controls.Add(nudX);
            Controls.Add(nudY);
            Controls.Add(nudZ);
            Controls.Add(btnApply);
            Controls.Add(btnCancel);
        }

        private static UtinniNumericUpDown MakeNud(int width)
        {
            return new UtinniNumericUpDown
            {
                DecimalPlaces = 2,
                Minimum = -100000,
                Maximum = 100000,
                Increment = 1,
                Size = new Size(width, 20),
            };
        }

        private static UtinniLabel MakeLabel(string text, int x, int y)
        {
            return new UtinniLabel
            {
                Text = text,
                ForeColor = Colors.Font(),
                Location = new Point(x, y),
                AutoSize = true,
            };
        }
    }
}
