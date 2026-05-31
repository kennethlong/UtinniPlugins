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
// Object-template editor host (Phase 11, CF-06). Designer cloned in shape from
// FormDatatableEditor.Designer.cs — the four-column effective-inheritance grid (Field · Effective
// value · Origin · Type), the ancestor breadcrumb header, the toolbar, the Find/Replace pane, and the
// status strip. Object-template layout/inheritance semantics were studied from swg-client-v2 only
// (no code, comments, identifier names, or test fixtures copied from any reference source).

namespace TJT.UI.Forms
{
    partial class FormObjectTemplateEditor
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
            this.btnPromote = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnRevert = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.sep3 = new System.Windows.Forms.Panel();
            this.btnFind = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnReplace = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.tglShowInherited = new UtinniCoreDotNet.UI.Controls.UtinniToggleButton();
            this.sep4 = new System.Windows.Forms.Panel();
            this.btnReload = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.lblReloadBadge = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.lblDirty = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.pnlBreadcrumb = new System.Windows.Forms.Panel();
            this.lblBreadcrumb = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.lblBreadcrumbLead = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.pnlFindReplace = new System.Windows.Forms.Panel();
            this.txtFind = new UtinniCoreDotNet.UI.Controls.UtinniTextbox();
            this.lblFindCount = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.btnFindPrev = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnFindNext = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.txtReplace = new UtinniCoreDotNet.UI.Controls.UtinniTextbox();
            this.btnReplaceOne = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnReplaceAll = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.tglMatchCase = new UtinniCoreDotNet.UI.Controls.UtinniToggleButton();
            this.tglRegex = new UtinniCoreDotNet.UI.Controls.UtinniToggleButton();
            this.btnFindClose = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.gridSurface = new TJT.UI.Controls.ThemedDataGridView();
            this.lblEmptyState = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.pnlStatus = new System.Windows.Forms.Panel();
            this.lblStatus = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.pnlCounters = new System.Windows.Forms.Panel();
            this.lblCounters = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            ((System.ComponentModel.ISupportInitialize)(this.gridSurface)).BeginInit();
            this.toolbar.SuspendLayout();
            this.pnlBreadcrumb.SuspendLayout();
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
            // btnSave (Save ▾ — items wired Plan 04; DISABLED until a document is bound)
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
            // btnUndo (DISABLED until a document is bound)
            //
            this.btnUndo.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnUndo.Width = 60;
            this.btnUndo.Name = "btnUndo";
            this.btnUndo.Text = "Undo";
            this.btnUndo.Enabled = false;
            this.btnUndo.UseDisableColor = true;
            this.btnUndo.UseVisualStyleBackColor = true;
            //
            // btnRedo (DISABLED until a document is bound)
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
            // btnPromote (Promote to override — mutation bodies wired Plan 04; DISABLED here)
            //
            this.btnPromote.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnPromote.Width = 130;
            this.btnPromote.Name = "btnPromote";
            this.btnPromote.Text = "Promote to override";
            this.btnPromote.Enabled = false;
            this.btnPromote.UseDisableColor = true;
            this.btnPromote.UseVisualStyleBackColor = true;
            //
            // btnRevert (Revert to inherited — mutation bodies wired Plan 04; DISABLED here)
            //
            this.btnRevert.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnRevert.Width = 130;
            this.btnRevert.Name = "btnRevert";
            this.btnRevert.Text = "Revert to inherited";
            this.btnRevert.Enabled = false;
            this.btnRevert.UseDisableColor = true;
            this.btnRevert.UseVisualStyleBackColor = true;
            //
            // sep3
            //
            this.sep3.Dock = System.Windows.Forms.DockStyle.Left;
            this.sep3.Width = 4;
            this.sep3.Name = "sep3";
            //
            // btnFind (DISABLED until a document is bound)
            //
            this.btnFind.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnFind.Width = 50;
            this.btnFind.Name = "btnFind";
            this.btnFind.Text = "Find";
            this.btnFind.Enabled = false;
            this.btnFind.UseDisableColor = true;
            this.btnFind.UseVisualStyleBackColor = true;
            //
            // btnReplace (DISABLED until a document is bound)
            //
            this.btnReplace.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnReplace.Width = 60;
            this.btnReplace.Name = "btnReplace";
            this.btnReplace.Text = "Replace";
            this.btnReplace.Enabled = false;
            this.btnReplace.UseDisableColor = true;
            this.btnReplace.UseVisualStyleBackColor = true;
            //
            // tglShowInherited (default ON — view-only inherited-row visibility toggle)
            //
            this.tglShowInherited.Dock = System.Windows.Forms.DockStyle.Left;
            this.tglShowInherited.Width = 110;
            this.tglShowInherited.Name = "tglShowInherited";
            this.tglShowInherited.Text = "Show inherited";
            this.tglShowInherited.Checked = true;
            this.tglShowInherited.Enabled = false;
            this.tglShowInherited.UseVisualStyleBackColor = true;
            //
            // sep4
            //
            this.sep4.Dock = System.Windows.Forms.DockStyle.Left;
            this.sep4.Width = 4;
            this.sep4.Name = "sep4";
            //
            // btnReload (Reload in client — body wired Plan 04; DISABLED here)
            //
            this.btnReload.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnReload.Width = 130;
            this.btnReload.Name = "btnReload";
            this.btnReload.Text = "Reload in client";
            this.btnReload.Enabled = false;
            this.btnReload.UseDisableColor = true;
            this.btnReload.UseVisualStyleBackColor = true;
            //
            // lblReloadBadge (Right cluster — CF-05 locked text; hidden until a document is open)
            //
            this.lblReloadBadge.Dock = System.Windows.Forms.DockStyle.Right;
            this.lblReloadBadge.AutoSize = false;
            this.lblReloadBadge.Width = 220;
            this.lblReloadBadge.Name = "lblReloadBadge";
            this.lblReloadBadge.Text = "Reloads on next scene change (relog to guarantee).";
            this.lblReloadBadge.Visible = false;
            this.lblReloadBadge.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
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
            // visual order so the first-added is innermost (rightmost of the left cluster) and the
            // last-added (btnOpen) is the leftmost. Mirrors FormDatatableEditor toolbar add-order.
            //
            this.toolbar.Controls.Add(this.lblReloadBadge);  // Right
            this.toolbar.Controls.Add(this.lblDirty);         // Right
            this.toolbar.Controls.Add(this.btnReload);        // Left (rightmost of left cluster)
            this.toolbar.Controls.Add(this.sep4);
            this.toolbar.Controls.Add(this.tglShowInherited);
            this.toolbar.Controls.Add(this.btnReplace);
            this.toolbar.Controls.Add(this.btnFind);
            this.toolbar.Controls.Add(this.sep3);
            this.toolbar.Controls.Add(this.btnRevert);
            this.toolbar.Controls.Add(this.btnPromote);
            this.toolbar.Controls.Add(this.sep2);
            this.toolbar.Controls.Add(this.btnRedo);
            this.toolbar.Controls.Add(this.btnUndo);
            this.toolbar.Controls.Add(this.sep1);
            this.toolbar.Controls.Add(this.btnSave);
            this.toolbar.Controls.Add(this.btnOpen);          // leftmost
            //
            // pnlBreadcrumb (Dock.Top, 24px — ancestor chain header; hidden in the empty state)
            //
            this.pnlBreadcrumb.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlBreadcrumb.Height = 24;
            this.pnlBreadcrumb.Visible = false;
            this.pnlBreadcrumb.Name = "pnlBreadcrumb";
            //
            // lblBreadcrumbLead ("Inherits:" leading label — docks left)
            //
            this.lblBreadcrumbLead.Dock = System.Windows.Forms.DockStyle.Left;
            this.lblBreadcrumbLead.AutoSize = false;
            this.lblBreadcrumbLead.Width = 56;
            this.lblBreadcrumbLead.Name = "lblBreadcrumbLead";
            this.lblBreadcrumbLead.Text = "Inherits:";
            this.lblBreadcrumbLead.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // lblBreadcrumb (Dock.Fill, AutoEllipsis — the chain root → … → this)
            //
            this.lblBreadcrumb.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblBreadcrumb.AutoSize = false;
            this.lblBreadcrumb.AutoEllipsis = true;
            this.lblBreadcrumb.Name = "lblBreadcrumb";
            this.lblBreadcrumb.Text = "";
            this.lblBreadcrumb.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // Add Fill child FIRST so it stays front-most, then the docked lead label.
            this.pnlBreadcrumb.Controls.Add(this.lblBreadcrumb);
            this.pnlBreadcrumb.Controls.Add(this.lblBreadcrumbLead);
            //
            // pnlFindReplace (Dock.Top, 32px, hidden by default)
            //
            this.pnlFindReplace.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlFindReplace.Height = 32;
            this.pnlFindReplace.Visible = false;
            this.pnlFindReplace.Name = "pnlFindReplace";
            //
            // txtFind
            //
            this.txtFind.Location = new System.Drawing.Point(6, 5);
            this.txtFind.Size = new System.Drawing.Size(240, 22);
            this.txtFind.Name = "txtFind";
            //
            // lblFindCount
            //
            this.lblFindCount.Location = new System.Drawing.Point(252, 8);
            this.lblFindCount.AutoSize = false;
            this.lblFindCount.Size = new System.Drawing.Size(60, 18);
            this.lblFindCount.Name = "lblFindCount";
            this.lblFindCount.Text = "0 / 0";
            //
            // btnFindPrev
            //
            this.btnFindPrev.Location = new System.Drawing.Point(318, 5);
            this.btnFindPrev.Size = new System.Drawing.Size(28, 22);
            this.btnFindPrev.Name = "btnFindPrev";
            this.btnFindPrev.Text = "◀";
            this.btnFindPrev.UseVisualStyleBackColor = true;
            //
            // btnFindNext
            //
            this.btnFindNext.Location = new System.Drawing.Point(348, 5);
            this.btnFindNext.Size = new System.Drawing.Size(28, 22);
            this.btnFindNext.Name = "btnFindNext";
            this.btnFindNext.Text = "▶";
            this.btnFindNext.UseVisualStyleBackColor = true;
            //
            // txtReplace (hidden until Replace pane is toggled)
            //
            this.txtReplace.Location = new System.Drawing.Point(384, 5);
            this.txtReplace.Size = new System.Drawing.Size(240, 22);
            this.txtReplace.Name = "txtReplace";
            this.txtReplace.Visible = false;
            //
            // btnReplaceOne
            //
            this.btnReplaceOne.Location = new System.Drawing.Point(628, 5);
            this.btnReplaceOne.Size = new System.Drawing.Size(70, 22);
            this.btnReplaceOne.Name = "btnReplaceOne";
            this.btnReplaceOne.Text = "Replace";
            this.btnReplaceOne.Visible = false;
            this.btnReplaceOne.UseVisualStyleBackColor = true;
            //
            // btnReplaceAll
            //
            this.btnReplaceAll.Location = new System.Drawing.Point(700, 5);
            this.btnReplaceAll.Size = new System.Drawing.Size(80, 22);
            this.btnReplaceAll.Name = "btnReplaceAll";
            this.btnReplaceAll.Text = "Replace all";
            this.btnReplaceAll.Visible = false;
            this.btnReplaceAll.UseVisualStyleBackColor = true;
            //
            // tglMatchCase
            //
            this.tglMatchCase.Location = new System.Drawing.Point(790, 6);
            this.tglMatchCase.Size = new System.Drawing.Size(36, 20);
            this.tglMatchCase.Name = "tglMatchCase";
            this.tglMatchCase.Text = "Aa";
            this.tglMatchCase.UseVisualStyleBackColor = true;
            //
            // tglRegex
            //
            this.tglRegex.Location = new System.Drawing.Point(828, 6);
            this.tglRegex.Size = new System.Drawing.Size(36, 20);
            this.tglRegex.Name = "tglRegex";
            this.tglRegex.Text = ".*";
            this.tglRegex.UseVisualStyleBackColor = true;
            //
            // btnFindClose
            //
            this.btnFindClose.Location = new System.Drawing.Point(912, 5);
            this.btnFindClose.Size = new System.Drawing.Size(28, 22);
            this.btnFindClose.Name = "btnFindClose";
            this.btnFindClose.Text = "✕";
            this.btnFindClose.UseVisualStyleBackColor = true;
            //
            this.pnlFindReplace.Controls.Add(this.txtFind);
            this.pnlFindReplace.Controls.Add(this.lblFindCount);
            this.pnlFindReplace.Controls.Add(this.btnFindPrev);
            this.pnlFindReplace.Controls.Add(this.btnFindNext);
            this.pnlFindReplace.Controls.Add(this.txtReplace);
            this.pnlFindReplace.Controls.Add(this.btnReplaceOne);
            this.pnlFindReplace.Controls.Add(this.btnReplaceAll);
            this.pnlFindReplace.Controls.Add(this.tglMatchCase);
            this.pnlFindReplace.Controls.Add(this.tglRegex);
            this.pnlFindReplace.Controls.Add(this.btnFindClose);
            //
            // gridSurface (Dock.Fill — ThemedDataGridView; lblEmptyState overlays it)
            //
            this.gridSurface.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridSurface.Name = "gridSurface";
            this.gridSurface.TabIndex = 0;
            this.gridSurface.Controls.Add(this.lblEmptyState);
            //
            // lblEmptyState (centered dimmed empty-state copy on top of the grid)
            //
            this.lblEmptyState.AutoSize = false;
            this.lblEmptyState.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblEmptyState.ForeColor = UtinniCoreDotNet.UI.Theme.Colors.FontDisabled();
            this.lblEmptyState.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblEmptyState.Name = "lblEmptyState";
            this.lblEmptyState.Text = "Open an object template from the toolbar, or use \"Open in Object Template Editor\" from the TRE Browser.";
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
            // pnlCounters (Dock.Right, 320px)
            //
            this.pnlCounters.Dock = System.Windows.Forms.DockStyle.Right;
            this.pnlCounters.Width = 320;
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
            this.lblCounters.Text = "0 fields";
            //
            // FormObjectTemplateEditor
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1200, 760);
            this.MinimumSize = new System.Drawing.Size(900, 560);
            // CF-09 Controls.Add ORDER (winforms-dockfill-zorder): grid Fill added FIRST so it stays
            // front-most, then pnlStatus (Bottom), then pnlFindReplace (Top hidden), then pnlBreadcrumb
            // (Top), then toolbar (Top) added LAST = outermost top. DO NOT reorder; DO NOT SendToBack
            // the grid.
            this.Controls.Add(this.gridSurface);      // Fill — front-most
            this.Controls.Add(this.pnlStatus);        // Bottom
            this.Controls.Add(this.pnlFindReplace);   // Top (collapsed by default)
            this.Controls.Add(this.pnlBreadcrumb);    // Top
            this.Controls.Add(this.toolbar);          // Top — added LAST → outermost
            this.DrawName = true;
            this.Name = "FormObjectTemplateEditor";
            this.Text = "Object Template Editor";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormObjectTemplateEditor_FormClosing);
            ((System.ComponentModel.ISupportInitialize)(this.gridSurface)).EndInit();
            this.toolbar.ResumeLayout(false);
            this.pnlBreadcrumb.ResumeLayout(false);
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
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnPromote;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnRevert;
        private System.Windows.Forms.Panel sep3;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnFind;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnReplace;
        private UtinniCoreDotNet.UI.Controls.UtinniToggleButton tglShowInherited;
        private System.Windows.Forms.Panel sep4;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnReload;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblReloadBadge;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblDirty;
        private System.Windows.Forms.Panel pnlBreadcrumb;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblBreadcrumb;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblBreadcrumbLead;
        private System.Windows.Forms.Panel pnlFindReplace;
        private UtinniCoreDotNet.UI.Controls.UtinniTextbox txtFind;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblFindCount;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnFindPrev;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnFindNext;
        private UtinniCoreDotNet.UI.Controls.UtinniTextbox txtReplace;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnReplaceOne;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnReplaceAll;
        private UtinniCoreDotNet.UI.Controls.UtinniToggleButton tglMatchCase;
        private UtinniCoreDotNet.UI.Controls.UtinniToggleButton tglRegex;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnFindClose;
        private TJT.UI.Controls.ThemedDataGridView gridSurface;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblEmptyState;
        private System.Windows.Forms.Panel pnlStatus;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblStatus;
        private System.Windows.Forms.Panel pnlCounters;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblCounters;
    }
}
