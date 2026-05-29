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
    partial class FormTypeChangeCascadeDialog
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
            this.lblBanner = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.gridCells = new TJT.UI.Controls.ThemedDataGridView();
            this.pnlFooter = new System.Windows.Forms.Panel();
            this.btnRevert = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnDone = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            ((System.ComponentModel.ISupportInitialize)(this.gridCells)).BeginInit();
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
            // lblBanner (Dock.Top, below heading)
            //
            this.lblBanner.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblBanner.AutoSize = false;
            this.lblBanner.Height = 24;
            this.lblBanner.Name = "lblBanner";
            this.lblBanner.Text = "";
            //
            // gridCells (Dock.Fill — embedded ThemedDataGridView listing affected cells)
            //
            this.gridCells.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridCells.Name = "gridCells";
            this.gridCells.TabIndex = 0;
            //
            // btnRevert
            //
            this.btnRevert.DrawOutline = false;
            this.btnRevert.Location = new System.Drawing.Point(8, 8);
            this.btnRevert.Size = new System.Drawing.Size(150, 26);
            this.btnRevert.Name = "btnRevert";
            this.btnRevert.Text = "Revert type change";
            this.btnRevert.TabIndex = 2;
            this.btnRevert.UseDisableColor = true;
            this.btnRevert.UseVisualStyleBackColor = true;
            //
            // btnDone
            //
            this.btnDone.DrawOutline = false;
            this.btnDone.Location = new System.Drawing.Point(394, 8);
            this.btnDone.Size = new System.Drawing.Size(90, 26);
            this.btnDone.Name = "btnDone";
            this.btnDone.Text = "Done";
            this.btnDone.TabIndex = 1;
            this.btnDone.UseDisableColor = true;
            this.btnDone.UseVisualStyleBackColor = true;
            //
            // pnlFooter (Dock.Bottom)
            //
            this.pnlFooter.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlFooter.Height = 42;
            this.pnlFooter.Name = "pnlFooter";
            this.pnlFooter.Controls.Add(this.btnRevert);
            this.pnlFooter.Controls.Add(this.btnDone);
            //
            // FormTypeChangeCascadeDialog
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(492, 360);
            this.MinimumSize = new System.Drawing.Size(420, 280);
            // Add Fill child FIRST so it stays front-most, then docked footer/top labels.
            this.Controls.Add(this.gridCells);   // Fill — front-most
            this.Controls.Add(this.pnlFooter);   // Bottom
            this.Controls.Add(this.lblBanner);   // Top
            this.Controls.Add(this.lblHeading);  // Top — added LAST → outermost top
            this.DrawName = true;
            this.Name = "FormTypeChangeCascadeDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Resolve type-change cascade";
            ((System.ComponentModel.ISupportInitialize)(this.gridCells)).EndInit();
            this.pnlFooter.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblHeading;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblBanner;
        private TJT.UI.Controls.ThemedDataGridView gridCells;
        private System.Windows.Forms.Panel pnlFooter;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnRevert;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnDone;
    }
}
