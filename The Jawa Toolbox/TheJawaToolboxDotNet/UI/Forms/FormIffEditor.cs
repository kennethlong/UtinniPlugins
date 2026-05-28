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

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using TJT.UI.Controls;
using UtinniCore.Utinni;
using UtinniCoreDotNet.Editing;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.PluginFramework;
using UtinniCoreDotNet.UI.Controls;
using UtinniCoreDotNet.UI.Forms;
using UtinniCoreDotNet.UI.Theme;
using UtinniCoreDotNet.Utility;

namespace TJT.UI.Forms
{
    /// <summary>
    /// Editable IFF editor window — a resizable <see cref="UtinniForm"/> registered via the plugin's
    /// <c>GetForms()</c> list (per the Phase 7 precedent, Wave-1 editors are forms, not the fixed
    /// 417px SubPanel). Hosts the shared <see cref="IffChunkTree"/> bound to a
    /// <see cref="MutableIffDocument"/> + the editor-local undo/redo controller
    /// <see cref="IffEditController"/> (D-08, INDEPENDENT of the scene-level undo plumbing).
    ///
    /// <para><b>Cross-task Task 2 vs Task 3 scope:</b> Task 2 ships the shell (toolbar + tree + leaf
    /// editor pane skeleton + Source property + ProcessCmdKey shortcuts) and binds Undo/Redo to the
    /// controller. Task 3 wires leaf payload editing (hex/text/replace-from-file), the structural-op
    /// context menus (D-03), and dirty-state visuals. Save▾ + Reload are placeholders for 08-05/06.</para>
    ///
    /// <para><b>Keyboard shortcuts (08-REVIEWS MEDIUM-6):</b> Ctrl+Z / Ctrl+Y / Ctrl+S are captured
    /// in <see cref="ProcessCmdKey"/> regardless of focused control, which kills any chance of the
    /// WinForms TextBox built-in Ctrl+Z competing with the IffEditController stack. The hex / text
    /// TextBoxes additionally set <c>ShortcutsEnabled = false</c> for redundant defense.</para>
    /// </summary>
    public partial class FormIffEditor : UtinniForm, IEditorForm
    {
        private readonly IEditorPlugin editorPlugin;
        private readonly UtINI ini;
        private IffEditController controller;
        private MutableIffDocument document;

        /// <summary>
        /// The provenance descriptor for the currently loaded document. The open path (08-05)
        /// constructs the appropriate <see cref="OpenSource"/> and sets it; downstream save modes
        /// (08-06 live-patch, 08-07 repack) read it to gate menu enable state. Defaults to
        /// <see cref="OpenSource.Unknown"/> so a degraded TRE-resolve fallback naturally disables
        /// the provenance-gated save modes (checker W-3).
        /// </summary>
        public OpenSource Source { get; set; }

        public FormIffEditor(IEditorPlugin editorPlugin)
        {
            InitializeComponent();

            // Phase 6 (06-05): TJT owns its brand icon; load from the plugin dir, guarded.
            string tjtIconPath = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "Resources", "TJT.ico");
            if (File.Exists(tjtIconPath))
            {
                this.Icon = new Icon(tjtIconPath);
            }

            this.editorPlugin = editorPlugin;
            ini = editorPlugin.GetConfig();

            CreateSettings();
            ini.Load();

            Width = ini.GetInt("IffEditor", "width");
            Height = ini.GetInt("IffEditor", "height");
            // Restore the splitter best-effort. SplitterDistance throws if it falls outside
            // [Panel1MinSize, width - Panel2MinSize]; guard AND try/catch so a stale/invalid ini
            // value can never bubble out of the ctor and fail the plugin's MEF load. (Same pattern
            // as FormTreBrowser.)
            try
            {
                int splitter = ini.GetInt("IffEditor", "splitterDistance");
                if (splitter >= splitContainer.Panel1MinSize &&
                    splitter <= splitContainer.Width - splitContainer.Panel2MinSize)
                {
                    splitContainer.SplitterDistance = splitter;
                }
            }
            catch
            {
                // keep the designer default splitter distance
            }

            // Theme via Colors.*() accessors only — no raw ARGB color literals.
            splitContainer.BackColor = Colors.Primary();
            toolbar.BackColor = Colors.Primary();
            sep1.BackColor = Colors.Primary();
            sep2.BackColor = Colors.Primary();
            pnlLeafEditor.BackColor = Colors.Primary();
            pnlLeafHeader.BackColor = Colors.Primary();
            pnlStatus.BackColor = Colors.Primary();
            lblDirty.ForeColor = Colors.Secondary(); // accent for unsaved-changes marker
            lblStatus.ForeColor = Colors.Font();
            lblLeafHeader.ForeColor = Colors.FontDisabled();
            txtHex.BackColor = Colors.PrimaryHighlight();
            txtHex.ForeColor = Colors.Font();
            txtText.BackColor = Colors.PrimaryHighlight();
            txtText.ForeColor = Colors.Font();

            // Default provenance is Unknown (W-3 contract). Open path (08-05) overrides.
            Source = OpenSource.Unknown.Instance;

            // Wire toolbar handlers (controller-bound; no scene-level undo plumbing — D-08).
            btnUndo.Click += (s, e) => { if (controller != null && controller.CanUndo) controller.Undo(); };
            btnRedo.Click += (s, e) => { if (controller != null && controller.CanRedo) controller.Redo(); };

            // Tree selection — Task 3 fills in the leaf-editor wiring; here we forward to a stub.
            iffChunkTree.AfterSelect += OnTreeAfterSelect;

            SetTitle(null);
        }

        /// <summary>Used by tests / callers to attach a pre-built MutableIffDocument.</summary>
        public void LoadDocument(MutableIffDocument doc)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            this.document = doc;
            this.controller = new IffEditController(doc);
            this.controller.EditApplied += OnEditApplied;
            iffChunkTree.LoadMutable(doc);
            btnSave.Enabled = true;
            UpdateUndoRedoState();
            UpdateDirtyVisuals();
        }

        private void CreateSettings()
        {
            ini.AddSetting("IffEditor", "width", "1100", UtINI.Value.Types.VtInt);
            ini.AddSetting("IffEditor", "height", "760", UtINI.Value.Types.VtInt);
            ini.AddSetting("IffEditor", "splitterDistance", "360", UtINI.Value.Types.VtInt);
            // Fallback loose-override directory (08-06 / D-05.1). Empty by default — the primary
            // source is the injected client's working directory derived at save time.
            ini.AddSetting("IffEditor", "looseOverrideDir", "", UtINI.Value.Types.VtString);
        }

        public string GetName() { return this.Text; }

        public Form Create(IEditorPlugin plugin, List<Form> parentChildren)
        {
            foreach (Form form in parentChildren)
            {
                if (form.GetType() == typeof(FormIffEditor))
                {
                    form.Activate();
                    return null;
                }
            }
            FormIffEditor newForm = new FormIffEditor(plugin);
            newForm.Show();
            parentChildren.Add(newForm);
            return newForm;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Keyboard shortcuts — Ctrl+Z / Ctrl+Y / Ctrl+S (08-REVIEWS MEDIUM-6)
        //
        // ProcessCmdKey runs BEFORE the focused control's WndProc sees the keys, so we own these
        // shortcuts regardless of which TextBox or TreeView currently has focus. Combined with
        // ShortcutsEnabled = false on the TextBoxes, this kills any competing built-in Ctrl+Z
        // that would desync the controller stack from the visible payload.
        // ─────────────────────────────────────────────────────────────────────

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z))
            {
                if (controller != null && controller.CanUndo) controller.Undo();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Y))
            {
                if (controller != null && controller.CanRedo) controller.Redo();
                return true;
            }
            if (keyData == (Keys.Control | Keys.S))
            {
                // Save▾ default action — the topmost-non-disabled Save mode. 08-05/08-06 wires
                // the actual save modes; for now this is a placeholder that flashes the status
                // strip so users see the shortcut was registered.
                lblStatus.Text = "Save target not configured — 08-05 wires this.";
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Controller integration — refresh UI on every Apply / Undo / Redo
        // ─────────────────────────────────────────────────────────────────────

        private void OnEditApplied(object sender, EventArgs e)
        {
            // Tree must re-render to reflect structural / payload changes; refresh the dirty
            // markers + button states.
            iffChunkTree.RefreshMutable(document);
            UpdateUndoRedoState();
            UpdateDirtyVisuals();
        }

        private void UpdateUndoRedoState()
        {
            btnUndo.Enabled = controller != null && controller.CanUndo;
            btnRedo.Enabled = controller != null && controller.CanRedo;
        }

        private void UpdateDirtyVisuals()
        {
            bool dirty = controller != null && controller.IsDirty;
            lblDirty.Text = dirty ? "Unsaved changes" : "";
            // Title gets a leading ● marker when dirty.
            SetTitle(dirty ? "●" : null);
        }

        private const string BaseTitle = "IFF Editor";

        private void SetTitle(string prefix)
        {
            this.Text = string.IsNullOrEmpty(prefix) ? BaseTitle : prefix + " " + BaseTitle;
            // UtinniForm draws the title in OnPaint; force a repaint so runtime Text changes show.
            this.Invalidate();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Tree selection — Task 3 wires the leaf-editor / payload-edit path here.
        // Task 2 just surfaces the selected leaf's bytes as read-only hex.
        // ─────────────────────────────────────────────────────────────────────

        private void OnTreeAfterSelect(object sender, TreeViewEventArgs e)
        {
            var node = e.Node != null ? e.Node.Tag as MutableIffNode : null;
            if (node == null)
            {
                ShowNoLeafSelected();
                return;
            }
            if (node.Kind == MutableIffNodeKind.Container)
            {
                ShowContainerSummary(node);
                return;
            }
            ShowLeafReadOnly(node);
        }

        private void ShowNoLeafSelected()
        {
            lblLeafHeader.Text = "Select a chunk in the tree to edit its bytes.";
            lblLeafHeader.ForeColor = Colors.FontDisabled();
            txtHex.Visible = false;
            txtText.Visible = false;
            btnHexMode.Visible = false;
            btnTextMode.Visible = false;
        }

        private void ShowContainerSummary(MutableIffNode container)
        {
            lblLeafHeader.Text = container.TypeId + " [" + (container.SubTypeId ?? "") + "]  ·  "
                                 + container.Children.Count + " children";
            lblLeafHeader.ForeColor = Colors.Font();
            txtHex.Visible = false;
            txtText.Visible = false;
            btnHexMode.Visible = false;
            btnTextMode.Visible = false;
        }

        private void ShowLeafReadOnly(MutableIffNode leaf)
        {
            lblLeafHeader.Text = leaf.TypeId + "  ·  " + leaf.PayloadLength + " bytes  ·  Editable";
            lblLeafHeader.ForeColor = Colors.Font();
            txtHex.Text = HexDump(leaf.GetPayloadCopy());
            txtHex.Visible = true;
            txtText.Visible = false;
            // Task 3 will set ReadOnly=false + wire commit-on-focus-leave; Task 2 leaves read-only.
            txtHex.ReadOnly = true;
        }

        // Minimal HexDump for the Task 2 read-only display (Task 3 replaces with the inverse parser).
        // Format: <offset>  <hex pairs>  |<ascii gutter>|
        private static string HexDump(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            var sb = new StringBuilder();
            const int bytesPerLine = 16;
            for (int i = 0; i < bytes.Length; i += bytesPerLine)
            {
                sb.Append(i.ToString("X8")).Append(":  ");
                int end = Math.Min(i + bytesPerLine, bytes.Length);
                for (int j = i; j < end; j++)
                {
                    sb.Append(bytes[j].ToString("X2")).Append(' ');
                }
                // Pad short last line.
                for (int j = end; j < i + bytesPerLine; j++) sb.Append("   ");
                sb.Append(" |");
                for (int j = i; j < end; j++)
                {
                    byte b = bytes[j];
                    sb.Append((b >= 0x20 && b <= 0x7E) ? (char)b : '.');
                }
                sb.Append('|').Append(Environment.NewLine);
            }
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Persistence — best-effort; never block close.
        // ─────────────────────────────────────────────────────────────────────

        private void FormIffEditor_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                ini.AddSetting("IffEditor", "width", Width.ToString(), UtINI.Value.Types.VtInt);
                ini.AddSetting("IffEditor", "height", Height.ToString(), UtINI.Value.Types.VtInt);
                ini.AddSetting("IffEditor", "splitterDistance", splitContainer.SplitterDistance.ToString(), UtINI.Value.Types.VtInt);
                ini.Save();
            }
            catch
            {
                // Persistence is best-effort; never block close.
            }
        }
    }
}
