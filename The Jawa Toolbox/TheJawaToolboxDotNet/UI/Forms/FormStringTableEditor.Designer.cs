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
    partial class FormStringTableEditor
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
            this.btnOpen = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnSave = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.sep1 = new System.Windows.Forms.Panel();
            this.btnUndo = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnRedo = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.sep2 = new System.Windows.Forms.Panel();
            this.btnAddEntry = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnRemoveEntry = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.sep3 = new System.Windows.Forms.Panel();
            this.btnImportCsv = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnExportCsv = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnExportPo = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.sep4 = new System.Windows.Forms.Panel();
            this.btnFind = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnReplace = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnFilter = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.tglShowId = new UtinniCoreDotNet.UI.Controls.UtinniToggleButton();
            this.sep5 = new System.Windows.Forms.Panel();
            this.btnReload = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.lblReloadBadge = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.lblDirty = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.pnlFindReplace = new System.Windows.Forms.Panel();
            this.pnlFilter = new System.Windows.Forms.Panel();
            this.gridSurface = new TJT.UI.Controls.ThemedDataGridView();
            this.colId = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colKey = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colText = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.lblEmptyState = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.pnlStatus = new System.Windows.Forms.Panel();
            this.lblStatus = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.pnlCounters = new System.Windows.Forms.Panel();
            this.lblCounters = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            ((System.ComponentModel.ISupportInitialize)(this.gridSurface)).BeginInit();
            this.toolbar.SuspendLayout();
            this.pnlStatus.SuspendLayout();
            this.pnlCounters.SuspendLayout();
            this.SuspendLayout();
            //
            // toolbar (Dock.Top, 28px)
            //
            this.toolbar.Dock = System.Windows.Forms.DockStyle.Top;
            this.toolbar.Height = 28;
            this.toolbar.Name = "toolbar";
            //
            // btnOpen (ENABLED, functional)
            //
            this.btnOpen.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnOpen.Width = 60;
            this.btnOpen.Name = "btnOpen";
            this.btnOpen.Text = "Open…";
            this.btnOpen.UseDisableColor = true;
            this.btnOpen.UseVisualStyleBackColor = true;
            //
            // btnSave (Save ▾ — DISABLED; menu items wired by Plan 10-05)
            //
            this.btnSave.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnSave.Width = 80;
            this.btnSave.Name = "btnSave";
            this.btnSave.Text = "Save ▾";
            this.btnSave.Enabled = false;
            this.btnSave.UseDisableColor = true;
            this.btnSave.UseVisualStyleBackColor = true;
            //
            // sep1
            //
            this.sep1.Dock = System.Windows.Forms.DockStyle.Left;
            this.sep1.Width = 4;
            this.sep1.Name = "sep1";
            //
            // btnUndo (ENABLED on LoadDocument — editor-local undo, CF-04)
            //
            this.btnUndo.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnUndo.Width = 60;
            this.btnUndo.Name = "btnUndo";
            this.btnUndo.Text = "Undo";
            this.btnUndo.Enabled = false;
            this.btnUndo.UseDisableColor = true;
            this.btnUndo.UseVisualStyleBackColor = true;
            //
            // btnRedo (ENABLED on LoadDocument — editor-local redo, CF-04)
            //
            this.btnRedo.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnRedo.Width = 60;
            this.btnRedo.Name = "btnRedo";
            this.btnRedo.Text = "Redo";
            this.btnRedo.Enabled = false;
            this.btnRedo.UseDisableColor = true;
            this.btnRedo.UseVisualStyleBackColor = true;
            //
            // sep2
            //
            this.sep2.Dock = System.Windows.Forms.DockStyle.Left;
            this.sep2.Width = 4;
            this.sep2.Name = "sep2";
            //
            // btnAddEntry (ENABLED on LoadDocument — T4 add)
            //
            this.btnAddEntry.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnAddEntry.Width = 80;
            this.btnAddEntry.Name = "btnAddEntry";
            this.btnAddEntry.Text = "Add entry";
            this.btnAddEntry.Enabled = false;
            this.btnAddEntry.UseDisableColor = true;
            this.btnAddEntry.UseVisualStyleBackColor = true;
            //
            // btnRemoveEntry (ENABLED on LoadDocument — T4 remove)
            //
            this.btnRemoveEntry.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnRemoveEntry.Width = 100;
            this.btnRemoveEntry.Name = "btnRemoveEntry";
            this.btnRemoveEntry.Text = "Remove entry";
            this.btnRemoveEntry.Enabled = false;
            this.btnRemoveEntry.UseDisableColor = true;
            this.btnRemoveEntry.UseVisualStyleBackColor = true;
            //
            // sep3
            //
            this.sep3.Dock = System.Windows.Forms.DockStyle.Left;
            this.sep3.Width = 4;
            this.sep3.Name = "sep3";
            //
            // btnImportCsv (DISABLED — CSV import arrives in Plan 10-04)
            //
            this.btnImportCsv.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnImportCsv.Width = 90;
            this.btnImportCsv.Name = "btnImportCsv";
            this.btnImportCsv.Text = "Import CSV…";
            this.btnImportCsv.Enabled = false;
            this.btnImportCsv.UseDisableColor = true;
            this.btnImportCsv.UseVisualStyleBackColor = true;
            //
            // btnExportCsv (DISABLED — CSV export arrives in Plan 10-04)
            //
            this.btnExportCsv.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnExportCsv.Width = 90;
            this.btnExportCsv.Name = "btnExportCsv";
            this.btnExportCsv.Text = "Export CSV…";
            this.btnExportCsv.Enabled = false;
            this.btnExportCsv.UseDisableColor = true;
            this.btnExportCsv.UseVisualStyleBackColor = true;
            //
            // btnExportPo (DISABLED — PO export arrives in Plan 10-04)
            //
            this.btnExportPo.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnExportPo.Width = 90;
            this.btnExportPo.Name = "btnExportPo";
            this.btnExportPo.Text = "Export PO…";
            this.btnExportPo.Enabled = false;
            this.btnExportPo.UseDisableColor = true;
            this.btnExportPo.UseVisualStyleBackColor = true;
            //
            // sep4
            //
            this.sep4.Dock = System.Windows.Forms.DockStyle.Left;
            this.sep4.Width = 4;
            this.sep4.Name = "sep4";
            //
            // btnFind (DISABLED — Find pane arrives in Plan 10-04)
            //
            this.btnFind.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnFind.Width = 50;
            this.btnFind.Name = "btnFind";
            this.btnFind.Text = "Find";
            this.btnFind.Enabled = false;
            this.btnFind.UseDisableColor = true;
            this.btnFind.UseVisualStyleBackColor = true;
            //
            // btnReplace (DISABLED — Replace pane arrives in Plan 10-04)
            //
            this.btnReplace.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnReplace.Width = 60;
            this.btnReplace.Name = "btnReplace";
            this.btnReplace.Text = "Replace";
            this.btnReplace.Enabled = false;
            this.btnReplace.UseDisableColor = true;
            this.btnReplace.UseVisualStyleBackColor = true;
            //
            // btnFilter (DISABLED — live filter arrives in Plan 10-04)
            //
            this.btnFilter.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnFilter.Width = 50;
            this.btnFilter.Name = "btnFilter";
            this.btnFilter.Text = "Filter";
            this.btnFilter.Enabled = false;
            this.btnFilter.UseDisableColor = true;
            this.btnFilter.UseVisualStyleBackColor = true;
            //
            // tglShowId (ENABLED on LoadDocument — toggles the read-only id column)
            //
            this.tglShowId.Dock = System.Windows.Forms.DockStyle.Left;
            this.tglShowId.Width = 70;
            this.tglShowId.Name = "tglShowId";
            this.tglShowId.Text = "Show id";
            this.tglShowId.Enabled = false;
            this.tglShowId.UseVisualStyleBackColor = true;
            //
            // sep5
            //
            this.sep5.Dock = System.Windows.Forms.DockStyle.Left;
            this.sep5.Width = 4;
            this.sep5.Name = "sep5";
            //
            // btnReload (DISABLED — reload dispatch arrives in Plan 10-05; badge shows CF-05 copy)
            //
            this.btnReload.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnReload.Width = 130;
            this.btnReload.Name = "btnReload";
            this.btnReload.Text = "Reload in client";
            this.btnReload.Enabled = false;
            this.btnReload.UseDisableColor = true;
            this.btnReload.UseVisualStyleBackColor = true;
            //
            // lblReloadBadge (Right cluster — CF-05 locked text; hidden until a file loads)
            //
            this.lblReloadBadge.Dock = System.Windows.Forms.DockStyle.Right;
            this.lblReloadBadge.AutoSize = false;
            this.lblReloadBadge.Width = 220;
            this.lblReloadBadge.Name = "lblReloadBadge";
            this.lblReloadBadge.Text = "Reloads on next scene change.";
            this.lblReloadBadge.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblReloadBadge.Visible = false;
            //
            // lblDirty (Right cluster — Phase 8 locked text, set at runtime)
            //
            this.lblDirty.Dock = System.Windows.Forms.DockStyle.Right;
            this.lblDirty.AutoSize = false;
            this.lblDirty.Width = 140;
            this.lblDirty.Name = "lblDirty";
            this.lblDirty.Text = "";
            this.lblDirty.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // toolbar.Controls — right cluster first (docks right), then left cluster in REVERSE
            // visual order so the first-added becomes innermost (rightmost of the left cluster) and
            // the last-added (btnOpen) becomes the leftmost. Mirrors FormDatatableEditor toolbar add-order.
            //
            this.toolbar.Controls.Add(this.lblReloadBadge);   // Right
            this.toolbar.Controls.Add(this.lblDirty);          // Right
            this.toolbar.Controls.Add(this.btnReload);         // Left (rightmost of left cluster)
            this.toolbar.Controls.Add(this.sep5);
            this.toolbar.Controls.Add(this.tglShowId);
            this.toolbar.Controls.Add(this.btnFilter);
            this.toolbar.Controls.Add(this.btnReplace);
            this.toolbar.Controls.Add(this.btnFind);
            this.toolbar.Controls.Add(this.sep4);
            this.toolbar.Controls.Add(this.btnExportPo);
            this.toolbar.Controls.Add(this.btnExportCsv);
            this.toolbar.Controls.Add(this.btnImportCsv);
            this.toolbar.Controls.Add(this.sep3);
            this.toolbar.Controls.Add(this.btnRemoveEntry);
            this.toolbar.Controls.Add(this.btnAddEntry);
            this.toolbar.Controls.Add(this.sep2);
            this.toolbar.Controls.Add(this.btnRedo);
            this.toolbar.Controls.Add(this.btnUndo);
            this.toolbar.Controls.Add(this.sep1);
            this.toolbar.Controls.Add(this.btnSave);
            this.toolbar.Controls.Add(this.btnOpen);           // leftmost
            //
            // pnlFindReplace (Dock.Top, 32px, hidden — Plan 10-04 fills + toggles it)
            //
            this.pnlFindReplace.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlFindReplace.Height = 32;
            this.pnlFindReplace.Visible = false;
            this.pnlFindReplace.Name = "pnlFindReplace";
            //
            // pnlFilter (Dock.Top, 28px, hidden — Plan 10-04 fills + toggles it)
            //
            this.pnlFilter.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlFilter.Height = 28;
            this.pnlFilter.Visible = false;
            this.pnlFilter.Name = "pnlFilter";
            //
            // gridSurface (Dock.Fill — ThemedDataGridView; lblEmptyState overlays it)
            //
            this.gridSurface.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridSurface.Name = "gridSurface";
            this.gridSurface.TabIndex = 0;
            this.gridSurface.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.gridSurface.AutoSizeRowsMode = System.Windows.Forms.DataGridViewAutoSizeRowsMode.DisplayedCells;
            this.gridSurface.AllowUserToOrderColumns = false;
            this.gridSurface.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
                this.colId, this.colKey, this.colText });
            this.gridSurface.Controls.Add(this.lblEmptyState);
            //
            // colId (read-only machine-managed diagnostic; HIDDEN by default)
            //
            this.colId.HeaderText = "id";
            this.colId.Name = "colId";
            this.colId.ReadOnly = true;
            this.colId.Visible = false;
            this.colId.Width = 64;
            this.colId.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colId.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // colKey (editable; name-validated on commit — delegates to ValidateName)
            //
            this.colKey.HeaderText = "Key";
            this.colKey.Name = "colKey";
            this.colKey.FillWeight = 35F;
            this.colKey.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.colKey.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // colText (editable; UTF-16LE verbatim, multi-line — no validation/transformation)
            //
            this.colText.HeaderText = "Text";
            this.colText.Name = "colText";
            this.colText.FillWeight = 65F;
            this.colText.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.colText.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.colText.DefaultCellStyle.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            //
            // lblEmptyState (centered dimmed empty-state copy on top of the grid)
            //
            this.lblEmptyState.AutoSize = false;
            this.lblEmptyState.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblEmptyState.ForeColor = UtinniCoreDotNet.UI.Theme.Colors.FontDisabled();
            this.lblEmptyState.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblEmptyState.Name = "lblEmptyState";
            this.lblEmptyState.Text = "Open a .stf file from the toolbar, or use \"Open in String-table Editor\" from the TRE Browser.";
            //
            // pnlStatus (Dock.Bottom, 22px)
            //
            this.pnlStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlStatus.Height = 22;
            this.pnlStatus.Padding = new System.Windows.Forms.Padding(3, 3, 3, 3);
            this.pnlStatus.Name = "pnlStatus";
            // Add Fill child (lblStatus) FIRST so it renders front-most, then the docked counters.
            this.pnlStatus.Controls.Add(this.lblStatus);
            this.pnlStatus.Controls.Add(this.pnlCounters);
            //
            // lblStatus (Dock.Fill)
            //
            this.lblStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblStatus.AutoSize = false;
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Text = "";
            //
            // pnlCounters (Dock.Right, 240px)
            //
            this.pnlCounters.Dock = System.Windows.Forms.DockStyle.Right;
            this.pnlCounters.Width = 240;
            this.pnlCounters.Name = "pnlCounters";
            this.pnlCounters.Controls.Add(this.lblCounters);
            //
            // lblCounters (Dock.Fill, right-aligned text)
            //
            this.lblCounters.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCounters.AutoSize = false;
            this.lblCounters.ForeColor = UtinniCoreDotNet.UI.Theme.Colors.FontDisabled();
            this.lblCounters.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblCounters.Name = "lblCounters";
            this.lblCounters.Text = "0 entries";
            //
            // FormStringTableEditor
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1000, 720);
            this.MinimumSize = new System.Drawing.Size(760, 520);
            // CF-09 Controls.Add ORDER (winforms-dockfill-zorder): grid Fill added FIRST so it stays
            // front-most, then pnlStatus (Bottom), then pnlFilter (Top hidden), then pnlFindReplace
            // (Top hidden), then toolbar (Top) added LAST = outermost top. DO NOT reorder; DO NOT
            // SendToBack the grid.
            this.Controls.Add(this.gridSurface);      // Fill — front-most
            this.Controls.Add(this.pnlStatus);        // Bottom
            this.Controls.Add(this.pnlFilter);        // Top (collapsed by default)
            this.Controls.Add(this.pnlFindReplace);   // Top (collapsed by default)
            this.Controls.Add(this.toolbar);          // Top — added LAST → outermost
            this.DrawName = true;
            this.Name = "FormStringTableEditor";
            this.Text = "String-table Editor";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormStringTableEditor_FormClosing);
            ((System.ComponentModel.ISupportInitialize)(this.gridSurface)).EndInit();
            this.toolbar.ResumeLayout(false);
            this.pnlStatus.ResumeLayout(false);
            this.pnlCounters.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel toolbar;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnOpen;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnSave;
        private System.Windows.Forms.Panel sep1;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnUndo;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnRedo;
        private System.Windows.Forms.Panel sep2;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnAddEntry;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnRemoveEntry;
        private System.Windows.Forms.Panel sep3;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnImportCsv;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnExportCsv;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnExportPo;
        private System.Windows.Forms.Panel sep4;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnFind;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnReplace;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnFilter;
        private UtinniCoreDotNet.UI.Controls.UtinniToggleButton tglShowId;
        private System.Windows.Forms.Panel sep5;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnReload;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblReloadBadge;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblDirty;
        private System.Windows.Forms.Panel pnlFindReplace;
        private System.Windows.Forms.Panel pnlFilter;
        private TJT.UI.Controls.ThemedDataGridView gridSurface;
        private System.Windows.Forms.DataGridViewTextBoxColumn colId;
        private System.Windows.Forms.DataGridViewTextBoxColumn colKey;
        private System.Windows.Forms.DataGridViewTextBoxColumn colText;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblEmptyState;
        private System.Windows.Forms.Panel pnlStatus;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblStatus;
        private System.Windows.Forms.Panel pnlCounters;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblCounters;
    }
}
