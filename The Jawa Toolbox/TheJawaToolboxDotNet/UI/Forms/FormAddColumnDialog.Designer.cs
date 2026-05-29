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
    partial class FormAddColumnDialog
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

        private void InitializeComponent()
        {
            this.lblName = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.txtColumnName = new UtinniCoreDotNet.UI.Controls.UtinniTextbox();
            this.lblType = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.cmbColumnType = new UtinniCoreDotNet.UI.Controls.UtinniComboBox();
            this.lblStatus = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.btnCancel = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnAdd = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.SuspendLayout();
            //
            // lblName
            //
            this.lblName.AutoSize = false;
            this.lblName.Location = new System.Drawing.Point(8, 30);
            this.lblName.Size = new System.Drawing.Size(90, 20);
            this.lblName.Name = "lblName";
            this.lblName.Text = "Column name";
            //
            // txtColumnName
            //
            this.txtColumnName.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtColumnName.Location = new System.Drawing.Point(104, 30);
            this.txtColumnName.Size = new System.Drawing.Size(216, 22);
            this.txtColumnName.Name = "txtColumnName";
            this.txtColumnName.TabIndex = 0;
            //
            // lblType
            //
            this.lblType.AutoSize = false;
            this.lblType.Location = new System.Drawing.Point(8, 60);
            this.lblType.Size = new System.Drawing.Size(90, 20);
            this.lblType.Name = "lblType";
            this.lblType.Text = "Type";
            //
            // cmbColumnType
            //
            this.cmbColumnType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbColumnType.Location = new System.Drawing.Point(104, 60);
            this.cmbColumnType.Size = new System.Drawing.Size(216, 22);
            this.cmbColumnType.Name = "cmbColumnType";
            this.cmbColumnType.TabIndex = 1;
            //
            // lblStatus
            //
            this.lblStatus.AutoSize = false;
            this.lblStatus.Location = new System.Drawing.Point(8, 90);
            this.lblStatus.Size = new System.Drawing.Size(312, 20);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Text = "";
            //
            // btnCancel
            //
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.DrawOutline = false;
            this.btnCancel.Location = new System.Drawing.Point(8, 118);
            this.btnCancel.Size = new System.Drawing.Size(75, 24);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Text = "Cancel";
            this.btnCancel.TabIndex = 3;
            this.btnCancel.UseDisableColor = true;
            this.btnCancel.UseVisualStyleBackColor = true;
            //
            // btnAdd
            //
            this.btnAdd.DrawOutline = false;
            this.btnAdd.Location = new System.Drawing.Point(220, 118);
            this.btnAdd.Size = new System.Drawing.Size(100, 24);
            this.btnAdd.Name = "btnAdd";
            this.btnAdd.Text = "Add column";
            this.btnAdd.TabIndex = 2;
            this.btnAdd.UseDisableColor = true;
            this.btnAdd.UseVisualStyleBackColor = true;
            //
            // FormAddColumnDialog
            //
            this.AcceptButton = this.btnAdd;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(330, 152);
            this.Controls.Add(this.btnAdd);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.cmbColumnType);
            this.Controls.Add(this.lblType);
            this.Controls.Add(this.txtColumnName);
            this.Controls.Add(this.lblName);
            this.DrawName = true;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FormAddColumnDialog";
            this.Resizable = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Add column";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblName;
        private UtinniCoreDotNet.UI.Controls.UtinniTextbox txtColumnName;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblType;
        private UtinniCoreDotNet.UI.Controls.UtinniComboBox cmbColumnType;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblStatus;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnCancel;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnAdd;
    }
}
