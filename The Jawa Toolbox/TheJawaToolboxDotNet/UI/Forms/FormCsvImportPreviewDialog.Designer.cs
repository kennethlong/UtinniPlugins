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
    partial class FormCsvImportPreviewDialog
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
            this.lblHeading = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.lblBody = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.gridPerColumnDiff = new TJT.UI.Controls.ThemedDataGridView();
            this.lvInvalidRows = new System.Windows.Forms.ListView();
            this.pnlFooter = new System.Windows.Forms.Panel();
            this.btnImport = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnCancel = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            ((System.ComponentModel.ISupportInitialize)(this.gridPerColumnDiff)).BeginInit();
            this.pnlFooter.SuspendLayout();
            this.SuspendLayout();
            //
            // lblHeading (Dock.Top)
            //
            this.lblHeading.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblHeading.AutoSize = false;
            this.lblHeading.Height = 28;
            this.lblHeading.Name = "lblHeading";
            this.lblHeading.Text = "";
            //
            // lblBody (Dock.Top, below heading)
            //
            this.lblBody.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblBody.AutoSize = false;
            this.lblBody.Height = 40;
            this.lblBody.Name = "lblBody";
            this.lblBody.Text = "";
            //
            // gridPerColumnDiff (Dock.Top, 200px — per-column diff)
            //
            this.gridPerColumnDiff.Dock = System.Windows.Forms.DockStyle.Top;
            this.gridPerColumnDiff.Height = 200;
            this.gridPerColumnDiff.Name = "gridPerColumnDiff";
            this.gridPerColumnDiff.TabIndex = 1;
            //
            // lvInvalidRows (Dock.Fill — red invalid-rows list)
            //
            this.lvInvalidRows.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lvInvalidRows.Name = "lvInvalidRows";
            this.lvInvalidRows.TabIndex = 2;
            this.lvInvalidRows.HideSelection = false;
            //
            // btnImport
            //
            this.btnImport.DrawOutline = false;
            this.btnImport.Location = new System.Drawing.Point(620, 8);
            this.btnImport.Size = new System.Drawing.Size(80, 26);
            this.btnImport.Name = "btnImport";
            this.btnImport.Text = "Import";
            this.btnImport.TabIndex = 3;
            this.btnImport.UseDisableColor = true;
            this.btnImport.UseVisualStyleBackColor = true;
            //
            // btnCancel
            //
            this.btnCancel.DrawOutline = false;
            this.btnCancel.Location = new System.Drawing.Point(708, 8);
            this.btnCancel.Size = new System.Drawing.Size(80, 26);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Text = "Cancel";
            this.btnCancel.TabIndex = 4;
            this.btnCancel.UseDisableColor = true;
            this.btnCancel.UseVisualStyleBackColor = true;
            //
            // pnlFooter (Dock.Bottom)
            //
            this.pnlFooter.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlFooter.Height = 42;
            this.pnlFooter.Name = "pnlFooter";
            this.pnlFooter.Controls.Add(this.btnImport);
            this.pnlFooter.Controls.Add(this.btnCancel);
            //
            // FormCsvImportPreviewDialog
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 600);
            this.MinimumSize = new System.Drawing.Size(560, 400);
            // Add Fill child FIRST so it stays front-most, then docked footer/top controls
            // (winforms-dockfill-zorder: Fill must be front-most).
            this.Controls.Add(this.lvInvalidRows);     // Fill — front-most
            this.Controls.Add(this.gridPerColumnDiff);  // Top
            this.Controls.Add(this.pnlFooter);          // Bottom
            this.Controls.Add(this.lblBody);            // Top
            this.Controls.Add(this.lblHeading);         // Top — added LAST → outermost top
            this.DrawName = true;
            this.Name = "FormCsvImportPreviewDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Import CSV";
            ((System.ComponentModel.ISupportInitialize)(this.gridPerColumnDiff)).EndInit();
            this.pnlFooter.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblHeading;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblBody;
        private TJT.UI.Controls.ThemedDataGridView gridPerColumnDiff;
        private System.Windows.Forms.ListView lvInvalidRows;
        private System.Windows.Forms.Panel pnlFooter;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnImport;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnCancel;
    }
}
