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
    partial class FormIffEditor
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
            this.btnReload = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.lblDirty = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.iffChunkTree = new TJT.UI.Controls.IffChunkTree();
            this.pnlLeafEditor = new System.Windows.Forms.Panel();
            this.pnlLeafHeader = new System.Windows.Forms.Panel();
            this.lblLeafHeader = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            this.btnHexMode = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.btnTextMode = new UtinniCoreDotNet.UI.Controls.UtinniButton();
            this.txtHex = new System.Windows.Forms.TextBox();
            this.txtText = new UtinniCoreDotNet.UI.Controls.UtinniTextbox();
            this.pnlStatus = new System.Windows.Forms.Panel();
            this.lblStatus = new UtinniCoreDotNet.UI.Controls.UtinniLabel();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.Panel1.SuspendLayout();
            this.splitContainer.Panel2.SuspendLayout();
            this.splitContainer.SuspendLayout();
            this.toolbar.SuspendLayout();
            this.pnlLeafEditor.SuspendLayout();
            this.pnlLeafHeader.SuspendLayout();
            this.pnlStatus.SuspendLayout();
            this.SuspendLayout();
            //
            // toolbar (Dock.Top, 28px) — Open · Save · sep · Undo · Redo · sep · Reload · dirty label
            //
            this.toolbar.Dock = System.Windows.Forms.DockStyle.Top;
            this.toolbar.Height = 28;
            this.toolbar.Name = "toolbar";
            //
            // btnOpen
            //
            this.btnOpen.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnOpen.Width = 60;
            this.btnOpen.Name = "btnOpen";
            this.btnOpen.Text = "Open…";
            this.btnOpen.UseDisableColor = true;
            this.btnOpen.UseVisualStyleBackColor = true;
            //
            // btnSave (Save▾ split/drop-down; the drop-down is wired by 08-05 — Task 2 leaves it placeholder)
            //
            this.btnSave.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnSave.Width = 80;
            this.btnSave.Name = "btnSave";
            this.btnSave.Text = "Save ▾";
            this.btnSave.Enabled = false;
            this.btnSave.UseDisableColor = true;
            this.btnSave.UseVisualStyleBackColor = true;
            //
            // sep1 (xs=4px gap separator)
            //
            this.sep1.Dock = System.Windows.Forms.DockStyle.Left;
            this.sep1.Width = 4;
            this.sep1.Name = "sep1";
            //
            // btnUndo
            //
            this.btnUndo.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnUndo.Width = 60;
            this.btnUndo.Name = "btnUndo";
            this.btnUndo.Text = "Undo";
            this.btnUndo.Enabled = false;
            this.btnUndo.UseDisableColor = true;
            this.btnUndo.UseVisualStyleBackColor = true;
            //
            // btnRedo
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
            // btnReload (08-05/08-06 wires the actual reload — Task 2 leaves placeholder)
            //
            this.btnReload.Dock = System.Windows.Forms.DockStyle.Left;
            this.btnReload.Width = 130;
            this.btnReload.Name = "btnReload";
            this.btnReload.Text = "Reload in client";
            this.btnReload.Enabled = false;
            this.btnReload.UseDisableColor = true;
            this.btnReload.UseVisualStyleBackColor = true;
            //
            // lblDirty — right-aligned dirty indicator
            //
            this.lblDirty.Dock = System.Windows.Forms.DockStyle.Right;
            this.lblDirty.AutoSize = false;
            this.lblDirty.Width = 140;
            this.lblDirty.Name = "lblDirty";
            this.lblDirty.Text = "";
            this.lblDirty.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // toolbar.Controls order — Fill first (none here), then docked siblings
            // We add the right-aligned dirty label, then the left buttons in reverse-visual order
            // so left-most renders left-most. Add LEFT children in REVERSE so the first-added becomes
            // the inner-most (rightmost of the left cluster) and the last-added becomes the leftmost.
            this.toolbar.Controls.Add(this.lblDirty);     // Right
            this.toolbar.Controls.Add(this.btnReload);    // Left (rightmost of left cluster)
            this.toolbar.Controls.Add(this.sep2);
            this.toolbar.Controls.Add(this.btnRedo);
            this.toolbar.Controls.Add(this.btnUndo);
            this.toolbar.Controls.Add(this.sep1);
            this.toolbar.Controls.Add(this.btnSave);
            this.toolbar.Controls.Add(this.btnOpen);      // leftmost
            //
            // splitContainer
            //
            this.splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer.Orientation = System.Windows.Forms.Orientation.Vertical;
            this.splitContainer.Location = new System.Drawing.Point(0, 0);
            this.splitContainer.Name = "splitContainer";
            // Size MUST be set before SplitterDistance: a fresh SplitContainer is 150px wide, and
            // setting SplitterDistance=360 at that width throws InvalidOperationException (which,
            // thrown from the ctor, would fail the whole plugin's MEF load). Mirrors FormTreBrowser.
            this.splitContainer.Size = new System.Drawing.Size(1100, 700);
            this.splitContainer.SplitterWidth = 4;
            this.splitContainer.SplitterDistance = 360;
            this.splitContainer.Panel1MinSize = 240;
            this.splitContainer.Panel2MinSize = 420;
            this.splitContainer.TabIndex = 0;
            // Panel1 (left): IffChunkTree
            this.splitContainer.Panel1.Controls.Add(this.iffChunkTree);
            // Panel2 (right): leaf editor pane
            this.splitContainer.Panel2.Controls.Add(this.pnlLeafEditor);
            //
            // iffChunkTree (Dock.Fill — Fill added first, see UI gotcha memory)
            //
            this.iffChunkTree.Dock = System.Windows.Forms.DockStyle.Fill;
            this.iffChunkTree.Name = "iffChunkTree";
            this.iffChunkTree.TabIndex = 0;
            //
            // pnlLeafEditor (Dock.Fill) — header strip + editor surfaces
            //
            this.pnlLeafEditor.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlLeafEditor.Name = "pnlLeafEditor";
            // Add Fill child (txtHex) BEFORE docked siblings so Fill renders front-most
            this.pnlLeafEditor.Controls.Add(this.txtHex);
            this.pnlLeafEditor.Controls.Add(this.txtText);
            this.pnlLeafEditor.Controls.Add(this.pnlLeafHeader);
            //
            // pnlLeafHeader (Dock.Top, 22px)
            //
            this.pnlLeafHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlLeafHeader.Height = 22;
            this.pnlLeafHeader.Name = "pnlLeafHeader";
            this.pnlLeafHeader.Controls.Add(this.btnTextMode);
            this.pnlLeafHeader.Controls.Add(this.btnHexMode);
            this.pnlLeafHeader.Controls.Add(this.lblLeafHeader);
            //
            // lblLeafHeader (Dock.Fill)
            //
            this.lblLeafHeader.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblLeafHeader.AutoSize = false;
            this.lblLeafHeader.Name = "lblLeafHeader";
            this.lblLeafHeader.Text = "Select a chunk in the tree to edit its bytes.";
            //
            // btnHexMode (Dock.Right) — toggles to Hex view
            //
            this.btnHexMode.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnHexMode.Width = 50;
            this.btnHexMode.Name = "btnHexMode";
            this.btnHexMode.Text = "Hex";
            this.btnHexMode.Visible = false;
            this.btnHexMode.UseDisableColor = true;
            this.btnHexMode.UseVisualStyleBackColor = true;
            //
            // btnTextMode (Dock.Right) — toggles to Text view
            //
            this.btnTextMode.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnTextMode.Width = 50;
            this.btnTextMode.Name = "btnTextMode";
            this.btnTextMode.Text = "Text";
            this.btnTextMode.Visible = false;
            this.btnTextMode.UseDisableColor = true;
            this.btnTextMode.UseVisualStyleBackColor = true;
            //
            // txtHex — editable hex view (Consolas 9pt monospace; ShortcutsEnabled = false)
            //
            this.txtHex.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtHex.Multiline = true;
            this.txtHex.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtHex.WordWrap = false;
            this.txtHex.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtHex.Font = new System.Drawing.Font("Consolas", 9F);
            // ShortcutsEnabled = false kills the WinForms TextBox built-in Ctrl+Z that would compete
            // with the IffEditController undo stack (08-REVIEWS MEDIUM-6). The Form's ProcessCmdKey
            // override catches Ctrl+Z/Y/S regardless of focused control.
            this.txtHex.ShortcutsEnabled = false;
            this.txtHex.ReadOnly = true;
            this.txtHex.Visible = false;
            this.txtHex.Name = "txtHex";
            //
            // txtText — inline text edit for ASCII-ish leaves; same ShortcutsEnabled = false rule.
            //
            this.txtText.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtText.Multiline = true;
            this.txtText.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtText.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtText.ShortcutsEnabled = false;
            this.txtText.Visible = false;
            this.txtText.Name = "txtText";
            //
            // pnlStatus (Dock.Bottom, 22px) — inset 3px status strip
            //
            this.pnlStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlStatus.Height = 22;
            this.pnlStatus.Padding = new System.Windows.Forms.Padding(3, 3, 3, 3);
            this.pnlStatus.Name = "pnlStatus";
            this.pnlStatus.Controls.Add(this.lblStatus);
            //
            // lblStatus
            //
            this.lblStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblStatus.AutoSize = false;
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Text = "";
            //
            // FormIffEditor
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1100, 760);
            this.MinimumSize = new System.Drawing.Size(820, 560);
            // Add controls in Dock-precedence order (Fill last so it renders innermost; per
            // [WinForms Dock.Fill must be front-most] this means add Fill FIRST among siblings).
            this.Controls.Add(this.splitContainer); // Fill — front-most
            this.Controls.Add(this.pnlStatus);      // Bottom
            this.Controls.Add(this.toolbar);        // Top
            this.DrawName = true;
            this.Name = "FormIffEditor";
            this.Text = "IFF Editor";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormIffEditor_FormClosing);
            this.splitContainer.Panel1.ResumeLayout(false);
            this.splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).EndInit();
            this.splitContainer.ResumeLayout(false);
            this.toolbar.ResumeLayout(false);
            this.pnlLeafEditor.ResumeLayout(false);
            this.pnlLeafEditor.PerformLayout();
            this.pnlLeafHeader.ResumeLayout(false);
            this.pnlStatus.ResumeLayout(false);
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
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnReload;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblDirty;
        private System.Windows.Forms.SplitContainer splitContainer;
        private TJT.UI.Controls.IffChunkTree iffChunkTree;
        private System.Windows.Forms.Panel pnlLeafEditor;
        private System.Windows.Forms.Panel pnlLeafHeader;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblLeafHeader;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnHexMode;
        private UtinniCoreDotNet.UI.Controls.UtinniButton btnTextMode;
        private System.Windows.Forms.TextBox txtHex;
        private UtinniCoreDotNet.UI.Controls.UtinniTextbox txtText;
        private System.Windows.Forms.Panel pnlStatus;
        private UtinniCoreDotNet.UI.Controls.UtinniLabel lblStatus;
    }
}
