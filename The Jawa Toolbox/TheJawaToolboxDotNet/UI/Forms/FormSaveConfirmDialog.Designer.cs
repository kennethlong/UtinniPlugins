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

namespace TJT.UI.Forms
{
    partial class FormSaveConfirmDialog
    {
        /// <summary>Required designer variable.</summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>Clean up any resources being used.</summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.lblHeading = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.lblBody = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.chkBackupFirst = new System.Windows.Forms.CheckBox();
            this.pnlButtons = new System.Windows.Forms.Panel();
            this.btnAccept = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnCancel = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.pnlButtons.SuspendLayout();
            this.SuspendLayout();
            //
            // lblHeading
            //
            this.lblHeading.AutoSize = false;
            this.lblHeading.Location = new System.Drawing.Point(12, 40);
            this.lblHeading.Name = "lblHeading";
            this.lblHeading.Size = new System.Drawing.Size(456, 22);
            this.lblHeading.TabIndex = 0;
            this.lblHeading.Text = "";
            //
            // lblBody
            //
            this.lblBody.AutoSize = false;
            this.lblBody.Location = new System.Drawing.Point(12, 68);
            this.lblBody.Name = "lblBody";
            this.lblBody.Size = new System.Drawing.Size(456, 84);
            this.lblBody.TabIndex = 1;
            this.lblBody.Text = "";
            //
            // chkBackupFirst
            //
            this.chkBackupFirst.AutoSize = true;
            this.chkBackupFirst.Location = new System.Drawing.Point(12, 156);
            this.chkBackupFirst.Name = "chkBackupFirst";
            this.chkBackupFirst.Size = new System.Drawing.Size(141, 17);
            this.chkBackupFirst.TabIndex = 2;
            this.chkBackupFirst.Text = "Back up the source first";
            this.chkBackupFirst.UseVisualStyleBackColor = false;
            this.chkBackupFirst.Visible = false;
            //
            // pnlButtons
            //
            this.pnlButtons.Controls.Add(this.btnAccept);
            this.pnlButtons.Controls.Add(this.btnCancel);
            this.pnlButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlButtons.Location = new System.Drawing.Point(0, 184);
            this.pnlButtons.Name = "pnlButtons";
            this.pnlButtons.Size = new System.Drawing.Size(480, 36);
            this.pnlButtons.TabIndex = 3;
            //
            // btnAccept
            //
            this.btnAccept.DrawOutline = false;
            this.btnAccept.Location = new System.Drawing.Point(312, 8);
            this.btnAccept.Name = "btnAccept";
            this.btnAccept.Size = new System.Drawing.Size(75, 20);
            this.btnAccept.TabIndex = 0;
            this.btnAccept.Text = "Accept";
            this.btnAccept.UseDisableColor = true;
            this.btnAccept.UseVisualStyleBackColor = true;
            //
            // btnCancel
            //
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.DrawOutline = false;
            this.btnCancel.Location = new System.Drawing.Point(393, 8);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 20);
            this.btnCancel.TabIndex = 1;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseDisableColor = true;
            this.btnCancel.UseVisualStyleBackColor = true;
            //
            // FormSaveConfirmDialog
            //
            this.AcceptButton = this.btnAccept;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(480, 220);
            this.Controls.Add(this.pnlButtons);
            this.Controls.Add(this.chkBackupFirst);
            this.Controls.Add(this.lblBody);
            this.Controls.Add(this.lblHeading);
            this.DrawName = true;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FormSaveConfirmDialog";
            this.Resizable = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Confirm";
            this.pnlButtons.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblHeading;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblBody;
        private System.Windows.Forms.CheckBox chkBackupFirst;
        private System.Windows.Forms.Panel pnlButtons;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnAccept;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnCancel;
    }
}
