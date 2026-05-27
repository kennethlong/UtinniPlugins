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
    partial class FormTreBrowser
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
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.txtFilter = new UtinniCoreDotNet.UI.Controls.UtinniTextbox();
            this.cbTypeFacet = new UtinniCoreDotNet.UI.Controls.UtinniComboBox();
            this.tvTre = new System.Windows.Forms.TreeView();
            this.lvFiltered = new System.Windows.Forms.ListView();
            this.colPath = new System.Windows.Forms.ColumnHeader();
            this.lblStatus = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.lblLegend = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.pnlDetail = new System.Windows.Forms.Panel();
            this.dbFilter = new System.Windows.Forms.Timer(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.Panel1.SuspendLayout();
            this.splitContainer.Panel2.SuspendLayout();
            this.splitContainer.SuspendLayout();
            this.SuspendLayout();
            //
            // splitContainer
            //
            this.splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer.Orientation = System.Windows.Forms.Orientation.Vertical;
            this.splitContainer.Location = new System.Drawing.Point(0, 0);
            this.splitContainer.Name = "splitContainer";
            this.splitContainer.SplitterDistance = 360;
            this.splitContainer.SplitterWidth = 4;
            this.splitContainer.Panel1MinSize = 240;
            this.splitContainer.Panel2MinSize = 420;
            this.splitContainer.TabIndex = 0;
            // Panel1 (left): filter + tree + results list + status/legend
            this.splitContainer.Panel1.Controls.Add(this.tvTre);
            this.splitContainer.Panel1.Controls.Add(this.lvFiltered);
            this.splitContainer.Panel1.Controls.Add(this.cbTypeFacet);
            this.splitContainer.Panel1.Controls.Add(this.txtFilter);
            this.splitContainer.Panel1.Controls.Add(this.lblStatus);
            this.splitContainer.Panel1.Controls.Add(this.lblLegend);
            // Panel2 (right): detail pane (filled by plan 03)
            this.splitContainer.Panel2.Controls.Add(this.pnlDetail);
            //
            // txtFilter
            //
            this.txtFilter.Dock = System.Windows.Forms.DockStyle.Top;
            this.txtFilter.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtFilter.Name = "txtFilter";
            this.txtFilter.Size = new System.Drawing.Size(360, 22);
            this.txtFilter.TabIndex = 0;
            this.txtFilter.TextChanged += new System.EventHandler(this.txtFilter_TextChanged);
            //
            // cbTypeFacet
            //
            this.cbTypeFacet.Dock = System.Windows.Forms.DockStyle.Top;
            this.cbTypeFacet.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            this.cbTypeFacet.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbTypeFacet.FormattingEnabled = true;
            this.cbTypeFacet.Items.AddRange(new object[] { "All types" });
            this.cbTypeFacet.Name = "cbTypeFacet";
            this.cbTypeFacet.Size = new System.Drawing.Size(360, 21);
            this.cbTypeFacet.TabIndex = 1;
            //
            // tvTre
            //
            this.tvTre.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tvTre.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.tvTre.HideSelection = false;
            this.tvTre.ShowLines = true;
            this.tvTre.Name = "tvTre";
            this.tvTre.TabIndex = 2;
            this.tvTre.BeforeExpand += new System.Windows.Forms.TreeViewCancelEventHandler(this.tvTre_BeforeExpand);
            this.tvTre.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.tvTre_AfterSelect);
            //
            // lvFiltered (broad-filter results mode — hidden by default)
            //
            this.lvFiltered.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lvFiltered.View = System.Windows.Forms.View.Details;
            this.lvFiltered.FullRowSelect = true;
            this.lvFiltered.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.lvFiltered.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
            this.lvFiltered.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { this.colPath });
            this.lvFiltered.Name = "lvFiltered";
            this.lvFiltered.TabIndex = 3;
            this.lvFiltered.Visible = false;
            //
            // colPath
            //
            this.colPath.Text = "Path";
            this.colPath.Width = 340;
            //
            // lblStatus
            //
            this.lblStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.lblStatus.AutoSize = false;
            this.lblStatus.Size = new System.Drawing.Size(360, 18);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.TabIndex = 4;
            this.lblStatus.Text = "";
            //
            // lblLegend
            //
            this.lblLegend.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.lblLegend.AutoSize = false;
            this.lblLegend.Size = new System.Drawing.Size(360, 18);
            this.lblLegend.Name = "lblLegend";
            this.lblLegend.TabIndex = 5;
            this.lblLegend.Text = "";
            //
            // pnlDetail
            //
            this.pnlDetail.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlDetail.AutoScroll = true;
            this.pnlDetail.Name = "pnlDetail";
            this.pnlDetail.TabIndex = 0;
            //
            // dbFilter (250ms debounce)
            //
            this.dbFilter.Interval = 250;
            this.dbFilter.Tick += new System.EventHandler(this.dbFilter_Tick);
            //
            // FormTreBrowser
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1100, 700);
            this.MinimumSize = new System.Drawing.Size(760, 480);
            this.Controls.Add(this.splitContainer);
            this.DrawName = true;
            this.Name = "FormTreBrowser";
            this.Text = "TRE Browser";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormTreBrowser_FormClosing);
            this.splitContainer.Panel1.ResumeLayout(false);
            this.splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).EndInit();
            this.splitContainer.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.SplitContainer splitContainer;
        private UtinniCoreDotNet.UI.Controls.UtinniTextbox txtFilter;
        private UtinniCoreDotNet.UI.Controls.UtinniComboBox cbTypeFacet;
        private System.Windows.Forms.TreeView tvTre;
        private System.Windows.Forms.ListView lvFiltered;
        private System.Windows.Forms.ColumnHeader colPath;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblStatus;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblLegend;
        private System.Windows.Forms.Panel pnlDetail;
        private System.Windows.Forms.Timer dbFilter;
    }
}
