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
// Particle editor host (15-06, PROD-W2-PRT). Designer cloned in shape from
// FormObjectTemplateEditor.Designer.cs — the toolbar, the Fill main split (emitter tree left + param
// grid / AI-assist pane right), the status strip, and the DEC-A3 preview-vs-author footer line.
// Particle layout/semantics were studied from swg-client-v2 only (no code, comments, identifier names,
// or test fixtures copied from any reference source).

namespace TJT.UI.Forms
{
    partial class FormParticleEditor
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
            this.btnExplain = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.sep3 = new System.Windows.Forms.Panel();
            this.btnPreview = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnReload = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.lblReloadBadge = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.lblDirty = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.mainSplit = new System.Windows.Forms.SplitContainer();
            this.emitterTree = new TJT.UI.Controls.IffChunkTree();
            this.rightSplit = new System.Windows.Forms.SplitContainer();
            this.gridSurface = new TJT.UI.Controls.ThemedDataGridView();
            this.lblEmptyState = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.txtAiResults = new UtinniCoreDotNet.UI.Controls.UtinniTextbox();
            this.pnlFooter = new System.Windows.Forms.Panel();
            this.lblBoundary = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.pnlStatus = new System.Windows.Forms.Panel();
            this.lblStatus = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.pnlCounters = new System.Windows.Forms.Panel();
            this.lblCounters = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            ((System.ComponentModel.ISupportInitialize)(this.gridSurface)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.mainSplit)).BeginInit();
            this.mainSplit.Panel1.SuspendLayout();
            this.mainSplit.Panel2.SuspendLayout();
            this.mainSplit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.rightSplit)).BeginInit();
            this.rightSplit.Panel1.SuspendLayout();
            this.rightSplit.Panel2.SuspendLayout();
            this.rightSplit.SuspendLayout();
            this.toolbar.SuspendLayout();
            this.pnlFooter.SuspendLayout();
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
            // btnSave (Save ▾ — DISABLED until a document is bound)
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
            // btnExplain (AI read-assist — DISABLED until a document is bound)
            //
            this.btnExplain.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnExplain.Width = 100;
            this.btnExplain.Name = "btnExplain";
            this.btnExplain.Text = "Explain effect";
            this.btnExplain.Enabled = false;
            this.btnExplain.UseDisableColor = true;
            this.btnExplain.UseVisualStyleBackColor = true;
            //
            // sep3
            //
            this.sep3.Dock = System.Windows.Forms.DockStyle.Left;
            this.sep3.Width = 4;
            this.sep3.Name = "sep3";
            //
            // btnPreview (D-09 hot-retrigger — DISABLED unless Game.IsRunning + hook reachable)
            //
            this.btnPreview.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnPreview.Width = 110;
            this.btnPreview.Name = "btnPreview";
            this.btnPreview.Text = "Preview in client";
            this.btnPreview.Enabled = false;
            this.btnPreview.UseDisableColor = true;
            this.btnPreview.UseVisualStyleBackColor = true;
            //
            // btnReload (state-encoded — DISABLED until a document is bound + live client)
            //
            this.btnReload.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnReload.Width = 110;
            this.btnReload.Name = "btnReload";
            this.btnReload.Text = "Reload in client";
            this.btnReload.Enabled = false;
            this.btnReload.UseDisableColor = true;
            this.btnReload.UseVisualStyleBackColor = true;
            //
            // lblReloadBadge (Right cluster — LOCKED Particle candor copy; hidden until a doc is open)
            //
            this.lblReloadBadge.Dock = System.Windows.Forms.DockStyle.Right;
            this.lblReloadBadge.AutoSize = false;
            this.lblReloadBadge.Width = 240;
            this.lblReloadBadge.Name = "lblReloadBadge";
            this.lblReloadBadge.Text = "Reloads on next scene change or relog.";
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
            // last-added (btnOpen) is the leftmost.
            //
            this.toolbar.Controls.Add(this.lblReloadBadge);  // Right
            this.toolbar.Controls.Add(this.lblDirty);         // Right
            this.toolbar.Controls.Add(this.btnReload);        // Left (rightmost of left cluster)
            this.toolbar.Controls.Add(this.btnPreview);
            this.toolbar.Controls.Add(this.sep3);
            this.toolbar.Controls.Add(this.btnExplain);
            this.toolbar.Controls.Add(this.sep2);
            this.toolbar.Controls.Add(this.btnRedo);
            this.toolbar.Controls.Add(this.btnUndo);
            this.toolbar.Controls.Add(this.sep1);
            this.toolbar.Controls.Add(this.btnSave);
            this.toolbar.Controls.Add(this.btnOpen);          // leftmost
            //
            // mainSplit (Dock.Fill — vertical: emitter tree | right pane). Size set before SplitterDistance.
            //
            this.mainSplit.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainSplit.Orientation = System.Windows.Forms.Orientation.Vertical;
            this.mainSplit.Name = "mainSplit";
            this.mainSplit.Size = new System.Drawing.Size(1100, 700);
            this.mainSplit.SplitterDistance = 280;
            //
            // mainSplit.Panel1 — emitter tree (≈280px)
            //
            this.emitterTree.Dock = System.Windows.Forms.DockStyle.Fill;
            this.emitterTree.Name = "emitterTree";
            this.mainSplit.Panel1.Controls.Add(this.emitterTree);
            //
            // rightSplit (horizontal: param grid on top, AI-assist pane below)
            //
            this.rightSplit.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rightSplit.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.rightSplit.Name = "rightSplit";
            this.rightSplit.Size = new System.Drawing.Size(816, 700);
            this.rightSplit.SplitterDistance = 470;
            //
            // gridSurface (rightSplit.Panel1 Fill — ThemedDataGridView; lblEmptyState overlays it)
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
            this.lblEmptyState.Text = "No particle effect open\r\nOpen a .prt file from the toolbar, or use \"Open in Particle Editor\" from the TRE Browser.";
            //
            // txtAiResults (rightSplit.Panel2 Fill — read-only AI results pane, Consolas 9pt)
            //
            this.txtAiResults.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtAiResults.Multiline = true;
            this.txtAiResults.ReadOnly = true;
            this.txtAiResults.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtAiResults.WordWrap = false;
            this.txtAiResults.Name = "txtAiResults";
            this.txtAiResults.Text = "";
            //
            this.rightSplit.Panel1.Controls.Add(this.gridSurface);
            this.rightSplit.Panel2.Controls.Add(this.txtAiResults);
            this.mainSplit.Panel2.Controls.Add(this.rightSplit);
            //
            // pnlFooter (Dock.Bottom, 22px — DEC-A3 preview-vs-author boundary, dimmed)
            //
            this.pnlFooter.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlFooter.Height = 22;
            this.pnlFooter.Padding = new System.Windows.Forms.Padding(6, 2, 6, 2);
            this.pnlFooter.Name = "pnlFooter";
            this.pnlFooter.Controls.Add(this.lblBoundary);
            //
            // lblBoundary (Dock.Fill — DEC-A3 D-11 sentence, FontDisabled())
            //
            this.lblBoundary.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblBoundary.AutoSize = false;
            this.lblBoundary.ForeColor = UtinniCoreDotNet.UI.Theme.Colors.FontDisabled();
            this.lblBoundary.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblBoundary.Name = "lblBoundary";
            this.lblBoundary.Text = "Utinni edits emitter, timing, and color parameters and swaps texture/mesh references — authoring the referenced meshes or textures stays in Blender.";
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
            // pnlCounters (Dock.Right, 360px)
            //
            this.pnlCounters.Dock = System.Windows.Forms.DockStyle.Right;
            this.pnlCounters.Width = 360;
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
            this.lblCounters.Text = "0 groups · 0 emitters · 0 raw-preserved";
            //
            // FormParticleEditor
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1100, 760);
            this.MinimumSize = new System.Drawing.Size(880, 560);
            // CF-09 Controls.Add ORDER (winforms-dockfill-zorder): the Fill main split added FIRST so it
            // stays front-most, then the docked panels (Bottom footer, Bottom status), then the toolbar
            // (Top) added LAST = outermost top. DO NOT reorder; DO NOT SendToBack the split.
            this.Controls.Add(this.mainSplit);        // Fill — front-most
            this.Controls.Add(this.pnlFooter);        // Bottom
            this.Controls.Add(this.pnlStatus);        // Bottom
            this.Controls.Add(this.toolbar);          // Top — added LAST → outermost
            this.DrawName = true;
            this.Name = "FormParticleEditor";
            this.Text = "Particle Editor";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormParticleEditor_FormClosing);
            ((System.ComponentModel.ISupportInitialize)(this.gridSurface)).EndInit();
            this.mainSplit.Panel1.ResumeLayout(false);
            this.mainSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.mainSplit)).EndInit();
            this.mainSplit.ResumeLayout(false);
            this.rightSplit.Panel1.ResumeLayout(false);
            this.rightSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.rightSplit)).EndInit();
            this.rightSplit.ResumeLayout(false);
            this.toolbar.ResumeLayout(false);
            this.pnlFooter.ResumeLayout(false);
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
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnExplain;
        private System.Windows.Forms.Panel sep3;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnPreview;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnReload;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblReloadBadge;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblDirty;
        private System.Windows.Forms.SplitContainer mainSplit;
        private TJT.UI.Controls.IffChunkTree emitterTree;
        private System.Windows.Forms.SplitContainer rightSplit;
        private TJT.UI.Controls.ThemedDataGridView gridSurface;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblEmptyState;
        private UtinniCoreDotNet.UI.Controls.UtinniTextbox txtAiResults;
        private System.Windows.Forms.Panel pnlFooter;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblBoundary;
        private System.Windows.Forms.Panel pnlStatus;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblStatus;
        private System.Windows.Forms.Panel pnlCounters;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblCounters;
    }
}
