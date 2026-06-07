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
    partial class FormSnapshotPlacements
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
            this.components = new System.ComponentModel.Container();
            this.toolbar = new System.Windows.Forms.Panel();
            this.btnMove = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnDelete = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnRetemplate = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.sep1 = new System.Windows.Forms.Panel();
            this.btnRefresh = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.lblReloadBadge = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.lblSelCount = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.filterRow = new System.Windows.Forms.Panel();
            this.lblFilter = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.txtFilter = new UtinniCoreDotNet.UI.Controls.UtinniTextbox();
            this.lblRowCount = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.grid = new TJT.UI.Controls.ThemedDataGridView();
            this.lblEmptyState = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.pnlStatus = new System.Windows.Forms.Panel();
            this.lblStatus = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.pnlFooter = new System.Windows.Forms.Panel();
            this.lblFooter = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            ((System.ComponentModel.ISupportInitialize)(this.grid)).BeginInit();
            this.toolbar.SuspendLayout();
            this.filterRow.SuspendLayout();
            this.pnlStatus.SuspendLayout();
            this.pnlFooter.SuspendLayout();
            this.SuspendLayout();
            //
            // toolbar (Dock.Top, 28px)
            //
            this.toolbar.Dock = System.Windows.Forms.DockStyle.Top;
            this.toolbar.Height = 28;
            this.toolbar.Name = "toolbar";
            //
            // btnMove
            //
            this.btnMove.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnMove.Width = 110;
            this.btnMove.Name = "btnMove";
            this.btnMove.Text = "Move selected…";
            this.btnMove.UseDisableColor = true;
            this.btnMove.UseVisualStyleBackColor = true;
            //
            // btnDelete
            //
            this.btnDelete.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnDelete.Width = 110;
            this.btnDelete.Name = "btnDelete";
            this.btnDelete.Text = "Delete selected";
            this.btnDelete.UseDisableColor = true;
            this.btnDelete.UseVisualStyleBackColor = true;
            //
            // btnRetemplate
            //
            this.btnRetemplate.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnRetemplate.Width = 130;
            this.btnRetemplate.Name = "btnRetemplate";
            this.btnRetemplate.Text = "Retemplate selected…";
            this.btnRetemplate.UseDisableColor = true;
            this.btnRetemplate.UseVisualStyleBackColor = true;
            //
            // sep1
            //
            this.sep1.Dock = System.Windows.Forms.DockStyle.Left;
            this.sep1.Width = 4;
            this.sep1.Name = "sep1";
            //
            // btnRefresh
            //
            this.btnRefresh.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnRefresh.Width = 70;
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Text = "Refresh";
            this.btnRefresh.UseDisableColor = true;
            this.btnRefresh.UseVisualStyleBackColor = true;
            //
            // lblReloadBadge (Right cluster — LOCKED candor copy, set at runtime)
            //
            this.lblReloadBadge.Dock = System.Windows.Forms.DockStyle.Right;
            this.lblReloadBadge.AutoSize = false;
            this.lblReloadBadge.Width = 240;
            this.lblReloadBadge.Name = "lblReloadBadge";
            this.lblReloadBadge.Text = "";
            this.lblReloadBadge.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // lblSelCount (Right cluster)
            //
            this.lblSelCount.Dock = System.Windows.Forms.DockStyle.Right;
            this.lblSelCount.AutoSize = false;
            this.lblSelCount.Width = 120;
            this.lblSelCount.Name = "lblSelCount";
            this.lblSelCount.Text = "0 selected";
            this.lblSelCount.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // toolbar.Controls — right cluster first (docks right), then left cluster in REVERSE
            // visual order so btnMove ends up leftmost (mirrors FormDatatableEditor add-order).
            //
            this.toolbar.Controls.Add(this.lblReloadBadge);  // Right
            this.toolbar.Controls.Add(this.lblSelCount);      // Right
            this.toolbar.Controls.Add(this.btnRefresh);       // Left (rightmost of left cluster)
            this.toolbar.Controls.Add(this.sep1);
            this.toolbar.Controls.Add(this.btnRetemplate);
            this.toolbar.Controls.Add(this.btnDelete);
            this.toolbar.Controls.Add(this.btnMove);          // leftmost
            //
            // filterRow (Dock.Top, 28px)
            //
            this.filterRow.Dock = System.Windows.Forms.DockStyle.Top;
            this.filterRow.Height = 28;
            this.filterRow.Name = "filterRow";
            //
            // lblFilter
            //
            this.lblFilter.Location = new System.Drawing.Point(6, 6);
            this.lblFilter.AutoSize = false;
            this.lblFilter.Size = new System.Drawing.Size(42, 18);
            this.lblFilter.Name = "lblFilter";
            this.lblFilter.Text = "Filter:";
            //
            // txtFilter
            //
            this.txtFilter.Location = new System.Drawing.Point(50, 4);
            this.txtFilter.Size = new System.Drawing.Size(280, 22);
            this.txtFilter.Name = "txtFilter";
            //
            // lblRowCount
            //
            this.lblRowCount.Location = new System.Drawing.Point(340, 6);
            this.lblRowCount.AutoSize = false;
            this.lblRowCount.Size = new System.Drawing.Size(160, 18);
            this.lblRowCount.Name = "lblRowCount";
            this.lblRowCount.Text = "0 / 0 nodes";
            //
            this.filterRow.Controls.Add(this.txtFilter);
            this.filterRow.Controls.Add(this.lblFilter);
            this.filterRow.Controls.Add(this.lblRowCount);
            //
            // grid (Dock.Fill — ThemedDataGridView; lblEmptyState overlays it)
            //
            this.grid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grid.MultiSelect = true;
            this.grid.ReadOnly = true;
            this.grid.AllowUserToAddRows = false;
            this.grid.AllowUserToDeleteRows = false;
            this.grid.Name = "grid";
            this.grid.TabIndex = 0;
            this.grid.Controls.Add(this.lblEmptyState);
            //
            // lblEmptyState (centered dimmed empty-state copy over the grid)
            //
            this.lblEmptyState.AutoSize = false;
            this.lblEmptyState.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblEmptyState.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblEmptyState.Name = "lblEmptyState";
            this.lblEmptyState.Text = "No snapshot loaded";
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
            // pnlFooter (Dock.Bottom, 20px — DEC-A3 D-10 preview-vs-author dimmed line)
            //
            this.pnlFooter.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlFooter.Height = 20;
            this.pnlFooter.Padding = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.pnlFooter.Name = "pnlFooter";
            this.pnlFooter.Controls.Add(this.lblFooter);
            //
            // lblFooter (Dock.Fill, dimmed)
            //
            this.lblFooter.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblFooter.AutoSize = false;
            this.lblFooter.Name = "lblFooter";
            this.lblFooter.Text = "";
            //
            // FormSnapshotPlacements
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(900, 600);
            this.MinimumSize = new System.Drawing.Size(720, 420);
            // CF-09 Controls.Add ORDER (winforms-dockfill-zorder): grid Fill added FIRST so it stays
            // front-most, then the Bottom panels, then the Top rows added LAST = outermost top. DO
            // NOT reorder; DO NOT SendToBack the grid.
            this.Controls.Add(this.grid);          // Fill — front-most
            this.Controls.Add(this.pnlStatus);     // Bottom
            this.Controls.Add(this.pnlFooter);     // Bottom (footer below status)
            this.Controls.Add(this.filterRow);     // Top
            this.Controls.Add(this.toolbar);       // Top — added LAST → outermost
            this.DrawName = true;
            this.Name = "FormSnapshotPlacements";
            this.Text = "Snapshot Placements";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormSnapshotPlacements_FormClosing);
            ((System.ComponentModel.ISupportInitialize)(this.grid)).EndInit();
            this.toolbar.ResumeLayout(false);
            this.filterRow.ResumeLayout(false);
            this.pnlStatus.ResumeLayout(false);
            this.pnlFooter.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel toolbar;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnMove;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnDelete;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnRetemplate;
        private System.Windows.Forms.Panel sep1;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnRefresh;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblReloadBadge;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblSelCount;
        private System.Windows.Forms.Panel filterRow;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblFilter;
        private UtinniCoreDotNet.UI.Controls.UtinniTextbox txtFilter;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblRowCount;
        private TJT.UI.Controls.ThemedDataGridView grid;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblEmptyState;
        private System.Windows.Forms.Panel pnlStatus;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblStatus;
        private System.Windows.Forms.Panel pnlFooter;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblFooter;
    }
}
