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
// 15-01 bulk-retemplate input modal (non-destructive, fully undoable → NO red confirm, per
// 15-UI-SPEC § Destructive). Collects the new object-template path applied to the N selected
// placements via WorldSnapshotImpl.BulkRetemplate (remove + add per node).

using System;
using System.Drawing;
using System.Windows.Forms;
using UtinniCoreDotNet.UI.Controls;
using UtinniCoreDotNet.UI.Forms;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Forms
{
    /// <summary>Per-call modal collecting the new object-template for a bulk retemplate of N placements.</summary>
    public class FormSnapshotBulkRetemplateDialog : UtinniForm
    {
        private readonly UtinniTextbox txtTemplate;

        public string NewTemplate { get; private set; }

        public FormSnapshotBulkRetemplateDialog(int count)
        {
            Text = "Retemplate " + count + " placements";
            DrawName = true;
            MaximizeBox = false;
            MinimizeBox = false;
            Resizable = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 110);
            BackColor = Colors.Primary();

            var lblHeading = new UtinniLabel
            {
                Text = "New object template",
                ForeColor = Colors.Font(),
                Location = new Point(10, 10),
                AutoSize = false,
                Size = new Size(400, 18),
            };

            txtTemplate = new UtinniTextbox
            {
                Location = new Point(10, 32),
                Size = new Size(400, 22),
                Text = "object/tangible/",
            };

            var lblHint = new UtinniLabel
            {
                Text = "e.g. object/tangible/furniture/cheap/shared_armoire_s01.iff",
                ForeColor = Colors.FontDisabled(),
                Location = new Point(10, 58),
                AutoSize = false,
                Size = new Size(400, 16),
            };

            var btnApply = new UtinniButton
            {
                Text = "Apply",
                DialogResult = DialogResult.OK,
                Location = new Point(335, 80),
                Size = new Size(75, 22),
                UseVisualStyleBackColor = true,
            };
            btnApply.Click += (s, e) => NewTemplate = (txtTemplate.Text ?? "").Trim();

            var btnCancel = new UtinniButton
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(255, 80),
                Size = new Size(75, 22),
                UseVisualStyleBackColor = true,
            };

            AcceptButton = btnApply;
            CancelButton = btnCancel;

            Controls.Add(lblHeading);
            Controls.Add(txtTemplate);
            Controls.Add(lblHint);
            Controls.Add(btnApply);
            Controls.Add(btnCancel);
        }
    }
}
