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
// Per-call modal hex/text leaf sub-editor for complex object-template params (Phase 11, D-02
// fallback). Hosts the same Consolas 9pt hex-editing surface the Phase 8 IFF Editor uses for raw
// leaf payloads, wrapped as a small modal so any param that the generic scalar decoder routes to
// raw-bytes-hex remains editable. Object-template layout/inheritance semantics were studied from
// swg-client-v2 only (no code, comments, identifier names, or test fixtures copied from any
// reference source).

namespace TJT.UI.Forms
{
    partial class FormParamHexEditor
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.txtHex = new UtinniCoreDotNet.UI.Controls.UtinniTextbox();
            this.pnlButtons = new System.Windows.Forms.Panel();
            this.btnCancel = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnOk = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.pnlStatus = new System.Windows.Forms.Panel();
            this.lblStatus = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.pnlButtons.SuspendLayout();
            this.pnlStatus.SuspendLayout();
            this.SuspendLayout();
            //
            // txtHex (Dock.Fill, multiline, Consolas 9pt — the raw-bytes hex surface)
            //
            this.txtHex.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtHex.Multiline = true;
            this.txtHex.AcceptsReturn = true;
            this.txtHex.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtHex.WordWrap = false;
            this.txtHex.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtHex.Name = "txtHex";
            //
            // pnlButtons (Dock.Bottom, 36px)
            //
            this.pnlButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlButtons.Height = 36;
            this.pnlButtons.Name = "pnlButtons";
            //
            // btnOk (Dock.Right)
            //
            this.btnOk.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnOk.Width = 90;
            this.btnOk.Name = "btnOk";
            this.btnOk.Text = "OK";
            this.btnOk.UseDisableColor = true;
            this.btnOk.UseVisualStyleBackColor = true;
            //
            // btnCancel (Dock.Right)
            //
            this.btnCancel.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnCancel.Width = 90;
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseDisableColor = true;
            this.btnCancel.UseVisualStyleBackColor = true;
            //
            this.pnlButtons.Controls.Add(this.btnOk);
            this.pnlButtons.Controls.Add(this.btnCancel);
            //
            // pnlStatus (Dock.Bottom, 22px)
            //
            this.pnlStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlStatus.Height = 22;
            this.pnlStatus.Padding = new System.Windows.Forms.Padding(3, 3, 3, 3);
            this.pnlStatus.Name = "pnlStatus";
            this.pnlStatus.Controls.Add(this.lblStatus);
            //
            // lblStatus (Dock.Fill)
            //
            this.lblStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblStatus.AutoSize = false;
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Text = "";
            //
            // FormParamHexEditor
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(560, 360);
            this.MinimumSize = new System.Drawing.Size(360, 220);
            // Fill added FIRST so it stays front-most (winforms-dockfill-zorder), then the docked panels.
            this.Controls.Add(this.txtHex);
            this.Controls.Add(this.pnlStatus);
            this.Controls.Add(this.pnlButtons);
            this.DrawName = true;
            this.Name = "FormParamHexEditor";
            this.Text = "Edit raw bytes";
            this.pnlButtons.ResumeLayout(false);
            this.pnlStatus.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private UtinniCoreDotNet.UI.Controls.UtinniTextbox txtHex;
        private System.Windows.Forms.Panel pnlButtons;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnCancel;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnOk;
        private System.Windows.Forms.Panel pnlStatus;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblStatus;
    }
}
