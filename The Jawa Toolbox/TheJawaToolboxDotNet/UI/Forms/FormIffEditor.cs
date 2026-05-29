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
using System.Threading.Tasks;
using System.Windows.Forms;
using TJT.Saving;
using TJT.UI.Controls;
using UtinniCore.Utinni;
using UtinniCoreDotNet.Editing;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.Formats.Tre;
using UtinniCoreDotNet.PluginFramework;
using UtinniCoreDotNet.Saving;
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

        // The leaf currently bound to the editor pane (null = no selection / container selected).
        private MutableIffNode currentLeaf;
        // Tracks which pane (hex vs text) is visible for the current leaf.
        private bool textModeActive;

        // Friendly display name for the loaded document (used in the Saved <name> (<mode>) status
        // copy + the editor window title). Set on open; cleared on close-document.
        private string displayName;

        // Save menu items — kept as fields so RefreshSaveMenuEnabledState can flip Enabled +
        // Tooltip based on the current Source pattern-match. Lives in BuildSaveMenu.
        private UtinniContextMenuStrip saveMenu;
        private ToolStripMenuItem miSaveInPlace;
        private ToolStripMenuItem miSaveLooseOverride;
        private ToolStripMenuItem miSaveAs;
        private ToolStripMenuItem miPatchLive;
        private ToolStripMenuItem miRepackTre;

        // True while a save Task is in flight — drives Reload-button-disabled-while-in-flight
        // (08-REVIEWS MEDIUM-9 stale-bytes reload race barrier).
        private bool saveInFlight;

        // Inferred root TypeId of the currently loaded document (used to drive
        // ReloadAssetClassifier sub-detection on .iff carriers). Refreshed at LoadDocument time.
        private string rootTypeId;

        // The last successfully-saved file path (used to feed ClientReloadDispatcher with the
        // right extension at Reload-button-click time).
        private string lastSavedPath;

        // Lazy ToolTip provider for runtime tooltips on UtinniButtons (the WinForms ToolTip
        // component attaches by SetToolTip on any control; UtinniButton itself doesn't expose
        // a ToolTipText property).
        private readonly ToolTip toolTip = new ToolTip();

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

            // Tree selection — Task 3 wires the leaf-editor / payload-edit path here.
            iffChunkTree.AfterSelect += OnTreeAfterSelect;

            // Commit-on-focus-leave (Validating) + hex/text mode toggles (Task 3).
            txtHex.Validating += OnHexValidating;
            txtHex.Leave += OnHexLeaveCommit;
            txtText.Validating += OnTextValidating;
            txtText.Leave += OnTextLeaveCommit;
            btnHexMode.Click += (s, e) => SetTextMode(false);
            btnTextMode.Click += (s, e) => SetTextMode(true);

            // Leaf payload context menu (D-04.2) — Replace bytes from file / Export bytes to file.
            BuildLeafContextMenu();
            // Tree structural-op context menu (D-03) — 8 ops.
            BuildTreeContextMenu();
            // Save▾ drop-down (D-05 / 08-05 Task 4) — five save items, Source-gated.
            BuildSaveMenu();
            // Wire the open / save-dropdown / reload handlers (08-05 Task 4).
            btnOpen.Click += OnOpenClicked;
            btnSave.Click += OnSaveButtonClick;
            btnReload.Click += OnReloadClicked;

            SetTitle(null);
            RefreshSaveMenuEnabledState();
            RefreshReloadButtonState();
        }

        /// <summary>Used by tests / callers to attach a pre-built MutableIffDocument.</summary>
        public void LoadDocument(MutableIffDocument doc)
        {
            LoadDocument(doc, OpenSource.Unknown.Instance, null);
        }

        /// <summary>
        /// Full-provenance overload used by the 08-05 open paths (Open… loose / TRE Browser
        /// hand-off). Sets <see cref="Source"/> + <paramref name="displayName"/>, captures the
        /// root IFF TypeId for the ReloadAssetClassifier sub-detect path, and refreshes the
        /// Save▾ enabled state so menu items immediately reflect the new provenance.
        /// </summary>
        public void LoadDocument(MutableIffDocument doc, OpenSource source, string displayName)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            this.document = doc;
            this.controller = new IffEditController(doc);
            this.controller.EditApplied += OnEditApplied;
            this.Source = source ?? OpenSource.Unknown.Instance;
            this.displayName = displayName;
            this.rootTypeId = doc.Root != null ? doc.Root.TypeId : null;
            this.lastSavedPath = null;
            iffChunkTree.LoadMutable(doc);
            btnSave.Enabled = true;
            UpdateUndoRedoState();
            UpdateDirtyVisuals();
            RefreshSaveMenuEnabledState();
            RefreshReloadButtonState();
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
                // Ctrl+S — default Save action: Save in place when source is LooseFile;
                // otherwise Save As… (the only enabled mode on Unknown/TreArchive without a
                // logical-path resolved override target).
                if (document == null) return true;
                if (Source is OpenSource.LooseFile)
                {
                    OnSaveInPlace(this, EventArgs.Empty);
                }
                else
                {
                    OnSaveAs(this, EventArgs.Empty);
                }
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
            DecorateDirtyNodes();
            UpdateUndoRedoState();
            UpdateDirtyVisuals();
            // If the current leaf still exists after refresh, re-bind the editor pane to it via
            // its tree node tag (which is a fresh MutableIffNode reference); keep the selection
            // sticky for the user.
        }

        // Walk the tree and prefix dirty nodes with the UI-SPEC ●/＋ glyph + Colors.Secondary().
        // For dirty nodes that didn't exist before (added), use ＋; for edited-in-place use ●.
        // The two are not distinguishable from the model alone post-edit; we use ● for any dirty
        // node (the UI-SPEC accepts either glyph as a "needs save" visual).
        private void DecorateDirtyNodes()
        {
            foreach (TreeNode tn in WalkTree(iffChunkTree.RootNodes))
            {
                var mut = tn.Tag as MutableIffNode;
                if (mut == null) continue;
                if (mut.IsDirty)
                {
                    if (!tn.Text.StartsWith("● "))
                    {
                        tn.Text = "● " + tn.Text;
                    }
                    tn.ForeColor = Colors.Secondary();
                }
            }
        }

        private static IEnumerable<TreeNode> WalkTree(TreeNodeCollection roots)
        {
            if (roots == null) yield break;
            var stack = new Stack<TreeNode>();
            foreach (TreeNode n in roots) stack.Push(n);
            while (stack.Count > 0)
            {
                TreeNode node = stack.Pop();
                yield return node;
                foreach (TreeNode child in node.Nodes) stack.Push(child);
            }
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
            currentLeaf = null;
            lblLeafHeader.Text = "Select a chunk in the tree to edit its bytes.";
            lblLeafHeader.ForeColor = Colors.FontDisabled();
            txtHex.Visible = false;
            txtText.Visible = false;
            btnHexMode.Visible = false;
            btnTextMode.Visible = false;
            txtHex.ContextMenuStrip = null;
            txtText.ContextMenuStrip = null;
        }

        private void ShowContainerSummary(MutableIffNode container)
        {
            currentLeaf = null;
            lblLeafHeader.Text = container.TypeId + " [" + (container.SubTypeId ?? "") + "]  ·  "
                                 + container.Children.Count + " children";
            lblLeafHeader.ForeColor = Colors.Font();
            txtHex.Visible = false;
            txtText.Visible = false;
            btnHexMode.Visible = false;
            btnTextMode.Visible = false;
            txtHex.ContextMenuStrip = null;
            txtText.ContextMenuStrip = null;
        }

        private void ShowLeafReadOnly(MutableIffNode leaf)
        {
            currentLeaf = leaf;
            lblLeafHeader.Text = leaf.TypeId + "  ·  " + leaf.PayloadLength + " bytes  ·  Editable";
            lblLeafHeader.ForeColor = Colors.Font();

            byte[] payload = leaf.GetPayloadCopy();
            bool ascii = IsAsciiIsh(payload);
            btnHexMode.Visible = ascii;
            btnTextMode.Visible = ascii;
            // Attach the leaf context menu (D-04.2 Replace / Export bytes).
            txtHex.ContextMenuStrip = leafContextMenu;
            txtText.ContextMenuStrip = leafContextMenu;

            // Default mode = Hex; ASCII-ish payloads can toggle to Text.
            textModeActive = false;
            txtHex.ReadOnly = false;
            txtHex.Text = HexDump(payload);
            txtText.Text = ascii ? Encoding.ASCII.GetString(payload) : "";
            txtHex.Visible = true;
            txtText.Visible = false;
        }

        // Switch between hex-edit and text-edit modes for the current leaf.
        private void SetTextMode(bool textMode)
        {
            if (currentLeaf == null) return;
            textModeActive = textMode;
            txtHex.Visible = !textMode;
            txtText.Visible = textMode;
        }

        // Heuristic: a payload is "ASCII-ish" if >=80% of bytes are printable ASCII or common
        // whitespace. Empty payloads default to false (no toggle offered).
        private static bool IsAsciiIsh(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return false;
            int good = 0;
            foreach (byte b in payload)
            {
                if ((b >= 0x20 && b <= 0x7E) || b == 0x09 || b == 0x0A || b == 0x0D) good++;
            }
            return good * 100 / payload.Length >= 80;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Hex parser (inverse of HexDump) — tolerates whitespace, offset prefix
        // (^[0-9A-Fa-f]+:), and the trailing ASCII gutter |...|. Returns null on
        // parse error (caller surfaces the validation copy).
        // ─────────────────────────────────────────────────────────────────────

        private static byte[] TryParseHex(string s)
        {
            if (s == null) return null;
            var sb = new StringBuilder(s.Length);
            using (var sr = new StringReader(s))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    // Strip ASCII gutter (anything after the last ' |' is the gutter).
                    int gutter = line.IndexOf(" |", StringComparison.Ordinal);
                    if (gutter >= 0) line = line.Substring(0, gutter);
                    // Strip offset prefix (^<hex>:).
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        bool allHex = true;
                        for (int i = 0; i < colon; i++)
                        {
                            char c = line[i];
                            if (!IsHexChar(c)) { allHex = false; break; }
                        }
                        if (allHex) line = line.Substring(colon + 1);
                    }
                    // Append all hex chars; discard whitespace + everything else.
                    foreach (char c in line)
                    {
                        if (IsHexChar(c)) sb.Append(c);
                    }
                }
            }
            if (sb.Length % 2 != 0) return null;
            byte[] result = new byte[sb.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                int hi = HexNibble(sb[i * 2]);
                int lo = HexNibble(sb[i * 2 + 1]);
                if (hi < 0 || lo < 0) return null;
                result[i] = (byte)((hi << 4) | lo);
            }
            return result;
        }

        private static bool IsHexChar(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
        }

        private static int HexNibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return -1;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Commit-on-focus-leave for the hex / text editors (08-REVIEWS MEDIUM-6)
        // Commit ONLY on Leave/Validating, never per TextChanged — prevents the per-character
        // undo storm Codex flagged.
        // ─────────────────────────────────────────────────────────────────────

        private void OnHexValidating(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (currentLeaf == null || !txtHex.Visible) return;
            byte[] parsed = TryParseHex(txtHex.Text);
            if (parsed == null)
            {
                lblStatus.Text = "Hex must be pairs of 0-9 / A-F. Remove the highlighted characters.";
                lblStatus.ForeColor = Color.Red;
                e.Cancel = true;
                return;
            }
            lblStatus.ForeColor = Colors.Font();
            lblStatus.Text = "";
        }

        private void OnHexLeaveCommit(object sender, EventArgs e)
        {
            if (currentLeaf == null || !txtHex.Visible) return;
            byte[] parsed = TryParseHex(txtHex.Text);
            if (parsed == null) return; // Validating already surfaced the error.
            // No-op when bytes match current payload (avoids spurious dirty + undo entries).
            byte[] current = currentLeaf.GetPayloadCopy();
            if (BytesEqual(parsed, current)) return;
            controller.Apply(IffEditCommands.EditLeafPayload(currentLeaf, parsed));
        }

        private void OnTextValidating(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Text-mode commits accept any string; no validation gate.
        }

        private void OnTextLeaveCommit(object sender, EventArgs e)
        {
            if (currentLeaf == null || !txtText.Visible) return;
            byte[] newBytes = Encoding.ASCII.GetBytes(txtText.Text ?? "");
            byte[] current = currentLeaf.GetPayloadCopy();
            if (BytesEqual(newBytes, current)) return;
            controller.Apply(IffEditCommands.EditLeafPayload(currentLeaf, newBytes));
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Leaf-payload context menu (D-04.2) — Replace bytes from file / Export bytes to file
        // ─────────────────────────────────────────────────────────────────────

        private UtinniContextMenuStrip leafContextMenu;
        private UtinniContextMenuStrip treeContextMenu;

        private void BuildLeafContextMenu()
        {
            leafContextMenu = new UtinniContextMenuStrip();
            var miReplace = new ToolStripMenuItem("Replace bytes from file…");
            miReplace.Click += OnReplaceBytesFromFile;
            var miExport = new ToolStripMenuItem("Export bytes to file…");
            miExport.Click += OnExportBytesToFile;
            leafContextMenu.Items.Add(miReplace);
            leafContextMenu.Items.Add(miExport);
        }

        private void OnReplaceBytesFromFile(object sender, EventArgs e)
        {
            if (currentLeaf == null) return;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select replacement payload";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    byte[] bytes = File.ReadAllBytes(ofd.FileName);
                    controller.Apply(IffEditCommands.ReplaceLeafFromBytes(currentLeaf, bytes));
                    // Refresh the editor pane with the new bytes.
                    ShowLeafReadOnly(currentLeaf);
                }
                catch (Exception ex)
                {
                    lblStatus.Text = "Replace failed: " + ex.Message;
                    lblStatus.ForeColor = Color.Red;
                }
            }
        }

        private void OnExportBytesToFile(object sender, EventArgs e)
        {
            if (currentLeaf == null) return;
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Export chunk payload";
                sfd.FileName = (currentLeaf.TypeId ?? "chunk").Trim() + ".bin";
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllBytes(sfd.FileName, currentLeaf.GetPayloadCopy());
                    lblStatus.Text = "Exported " + currentLeaf.PayloadLength + " bytes.";
                    lblStatus.ForeColor = Colors.Font();
                }
                catch (Exception ex)
                {
                    lblStatus.Text = "Export failed: " + ex.Message;
                    lblStatus.ForeColor = Color.Red;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Tree structural-op context menu (D-03) — 8 ops, contextually enabled
        // ─────────────────────────────────────────────────────────────────────

        private ToolStripMenuItem miAddChunk;
        private ToolStripMenuItem miAddForm;
        private ToolStripMenuItem miRemove;
        private ToolStripMenuItem miRenameRetag;
        private ToolStripMenuItem miEditFormSubType;
        private ToolStripMenuItem miDuplicate;
        private ToolStripMenuItem miMoveUp;
        private ToolStripMenuItem miMoveDown;

        private void BuildTreeContextMenu()
        {
            treeContextMenu = new UtinniContextMenuStrip();
            miAddChunk = new ToolStripMenuItem("Add chunk…");           miAddChunk.Click += OnAddChunk;
            miAddForm = new ToolStripMenuItem("Add FORM…");              miAddForm.Click += OnAddForm;
            miRemove = new ToolStripMenuItem("Remove");                  miRemove.Click += OnRemove;
            miRenameRetag = new ToolStripMenuItem("Rename / retag…");    miRenameRetag.Click += OnRenameRetag;
            miEditFormSubType = new ToolStripMenuItem("Edit FORM sub-type…"); miEditFormSubType.Click += OnEditFormSubType;
            miDuplicate = new ToolStripMenuItem("Duplicate");            miDuplicate.Click += OnDuplicate;
            miMoveUp = new ToolStripMenuItem("Move up");                 miMoveUp.Click += OnMoveUp;
            miMoveDown = new ToolStripMenuItem("Move down");             miMoveDown.Click += OnMoveDown;
            treeContextMenu.Items.AddRange(new ToolStripItem[] {
                miAddChunk, miAddForm, miRemove, miRenameRetag, miEditFormSubType,
                new ToolStripSeparator(),
                miDuplicate, miMoveUp, miMoveDown
            });
            treeContextMenu.Opening += OnTreeContextMenuOpening;
            iffChunkTree.StructuralOpMenu = treeContextMenu;
        }

        private void OnTreeContextMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var node = iffChunkTree.SelectedNode != null ? iffChunkTree.SelectedNode.Tag as MutableIffNode : null;
            bool hasNode = node != null;
            bool isContainer = hasNode && node.Kind == MutableIffNodeKind.Container;
            bool isRoot = hasNode && node.Parent == null;

            miAddChunk.Enabled = isContainer;
            miAddForm.Enabled = isContainer;
            miRemove.Enabled = hasNode && !isRoot;
            miRenameRetag.Enabled = hasNode;
            miEditFormSubType.Enabled = isContainer;
            miDuplicate.Enabled = hasNode && !isRoot;
            miMoveUp.Enabled = hasNode && !isRoot && IndexOfNodeAmongSiblings(node) > 0;
            miMoveDown.Enabled = hasNode && !isRoot
                && IndexOfNodeAmongSiblings(node) < node.Parent.Children.Count - 1;

            if (!hasNode) e.Cancel = true;
        }

        private static int IndexOfNodeAmongSiblings(MutableIffNode node)
        {
            if (node.Parent == null) return -1;
            for (int i = 0; i < node.Parent.Children.Count; i++)
            {
                if (object.ReferenceEquals(node.Parent.Children[i], node)) return i;
            }
            return -1;
        }

        private MutableIffNode SelectedNode()
        {
            return iffChunkTree.SelectedNode != null ? iffChunkTree.SelectedNode.Tag as MutableIffNode : null;
        }

        private void OnAddChunk(object sender, EventArgs e)
        {
            var sel = SelectedNode();
            if (sel == null || sel.Kind != MutableIffNodeKind.Container) return;
            string tag = PromptFourCc("New chunk tag", "DATA");
            if (tag == null) return;
            // 08-REVIEW WR-01: MutableIffNode.NewLeaf throws ArgumentException for non-printable-ASCII
            // FourCCs (the FormFourCcDialog only enforces length, not content). Surface the validation
            // failure into the status strip instead of letting it escape to the WinForms unhandled-
            // exception handler. InvalidOperationException covers parent-kind / cross-boundary cases.
            try
            {
                controller.Apply(IffEditCommands.AddLeaf(sel, tag, new byte[0]));
            }
            catch (ArgumentException ex) { SurfaceStructuralOpError(ex.Message); }
            catch (InvalidOperationException ex) { SurfaceStructuralOpError(ex.Message); }
        }

        private void OnAddForm(object sender, EventArgs e)
        {
            var sel = SelectedNode();
            if (sel == null || sel.Kind != MutableIffNodeKind.Container) return;
            string sub = PromptFourCc("New FORM sub-type", "NEWS");
            if (sub == null) return;
            // 08-REVIEW WR-01: see OnAddChunk — same ArgumentException surface from FourCC validation.
            try
            {
                controller.Apply(IffEditCommands.AddContainer(sel, "FORM", sub));
            }
            catch (ArgumentException ex) { SurfaceStructuralOpError(ex.Message); }
            catch (InvalidOperationException ex) { SurfaceStructuralOpError(ex.Message); }
        }

        private void OnRemove(object sender, EventArgs e)
        {
            var sel = SelectedNode();
            if (sel == null || sel.Parent == null) return;
            // 08-REVIEW WR-01: Remove does not validate FourCC, but the controller can still surface
            // InvalidOperationException from edge cases (e.g. root removal slipping past the gate).
            try
            {
                controller.Apply(IffEditCommands.Remove(sel));
            }
            catch (ArgumentException ex) { SurfaceStructuralOpError(ex.Message); }
            catch (InvalidOperationException ex) { SurfaceStructuralOpError(ex.Message); }
        }

        private void OnRenameRetag(object sender, EventArgs e)
        {
            var sel = SelectedNode();
            if (sel == null) return;
            string tag = PromptFourCc("Rename / retag", sel.TypeId);
            if (tag == null) return;
            // 08-REVIEW WR-01: TypeId setter throws ArgumentException for non-printable-ASCII AND
            // InvalidOperationException for cross-boundary retag — catch both.
            try
            {
                controller.Apply(IffEditCommands.RenameRetag(sel, tag));
            }
            catch (ArgumentException ex) { SurfaceStructuralOpError(ex.Message); }
            catch (InvalidOperationException ex) { SurfaceStructuralOpError(ex.Message); }
        }

        private void OnEditFormSubType(object sender, EventArgs e)
        {
            var sel = SelectedNode();
            if (sel == null || sel.Kind != MutableIffNodeKind.Container) return;
            string sub = PromptFourCc("Edit FORM sub-type", sel.SubTypeId);
            if (sub == null) return;
            // 08-REVIEW WR-01: SubTypeId setter throws ArgumentException for non-printable-ASCII.
            try
            {
                controller.Apply(IffEditCommands.EditFormSubType(sel, sub));
            }
            catch (ArgumentException ex) { SurfaceStructuralOpError(ex.Message); }
            catch (InvalidOperationException ex) { SurfaceStructuralOpError(ex.Message); }
        }

        private void OnDuplicate(object sender, EventArgs e)
        {
            var sel = SelectedNode();
            if (sel == null || sel.Parent == null) return;
            try
            {
                controller.Apply(IffEditCommands.Duplicate(sel));
            }
            catch (ArgumentException ex) { SurfaceStructuralOpError(ex.Message); }
            catch (InvalidOperationException ex) { SurfaceStructuralOpError(ex.Message); }
        }

        private void OnMoveUp(object sender, EventArgs e)
        {
            var sel = SelectedNode();
            if (sel == null || sel.Parent == null) return;
            try
            {
                controller.Apply(IffEditCommands.MoveUp(sel));
            }
            catch (ArgumentException ex) { SurfaceStructuralOpError(ex.Message); }
            catch (InvalidOperationException ex) { SurfaceStructuralOpError(ex.Message); }
        }

        private void OnMoveDown(object sender, EventArgs e)
        {
            var sel = SelectedNode();
            if (sel == null || sel.Parent == null) return;
            try
            {
                controller.Apply(IffEditCommands.MoveDown(sel));
            }
            catch (ArgumentException ex) { SurfaceStructuralOpError(ex.Message); }
            catch (InvalidOperationException ex) { SurfaceStructuralOpError(ex.Message); }
        }

        // 08-REVIEW WR-01: shared status-strip surface for structural-op validation failures so
        // ArgumentException / InvalidOperationException from MutableIffNode never escape to the
        // WinForms unhandled-exception path (which would pop the JIT debugger). Mirrors the prior
        // inline pattern used by OnRenameRetag (now consolidated).
        private void SurfaceStructuralOpError(string message)
        {
            lblStatus.Text = message;
            lblStatus.ForeColor = Color.Red;
        }

        // Prompts the user for a 4-character FourCC via FormFourCcDialog. Returns null if the
        // user cancelled OR typed a non-4-char value (which surfaces the UI-SPEC validation copy
        // in the status strip).
        private string PromptFourCc(string title, string initial)
        {
            using (var dlg = new FormFourCcDialog(title, initial ?? ""))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                string v = dlg.Value ?? "";
                if (v.Length != 4)
                {
                    lblStatus.Text = "A chunk tag must be exactly 4 characters (e.g. \"DATA\").";
                    lblStatus.ForeColor = Color.Red;
                    return null;
                }
                return v;
            }
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
        // 08-05 Task 4: Save▾ drop-down + Open… + Reload-in-client + provenance
        // gating (W-3 / round-2 MEDIUM 5) + TRE Browser hand-off
        // ─────────────────────────────────────────────────────────────────────

        // The five Save▾ items per UI-SPEC §Save-target chooser. Round-2 MEDIUM 5: when Source
        // is OpenSource.Unknown, Save (in place), Save as loose override, Patch live client,
        // and Repack into source .tre ALL stay disabled; ONLY Save As… remains enabled (it
        // doesn't pattern-match a populated case — the user picks the path).
        private void BuildSaveMenu()
        {
            saveMenu = new UtinniContextMenuStrip();
            miSaveInPlace = new ToolStripMenuItem("Save (in place)");
            miSaveInPlace.Click += OnSaveInPlace;
            miSaveLooseOverride = new ToolStripMenuItem("Save as loose override");
            miSaveLooseOverride.Click += OnSaveLooseOverride;
            miSaveAs = new ToolStripMenuItem("Save As…");
            miSaveAs.Click += OnSaveAs;
            miPatchLive = new ToolStripMenuItem("Patch live client (in memory)");
            // 08-06: provenance-gated on `Source is OpenSource.ClientMemory cm` AND
            // Game.IsRunning. No current Phase-8 open path constructs ClientMemory, so
            // the item ships DISABLED in this phase with the honest "infra-ready"
            // tooltip (round-2 MEDIUM 11). A follow-up phase will wire a discovery
            // path; at that point this menu item enables on a live ClientMemory open.
            miPatchLive.Enabled = false;
            miPatchLive.ToolTipText = "Live patch requires opening from client memory — not wired in this phase.";
            miPatchLive.Click += OnPatchLive;
            miRepackTre = new ToolStripMenuItem("Repack into source .tre…");
            // 08-07: provenance-gated on `Source is OpenSource.TreArchive ta`
            // (08-REVIEWS HIGH-2). Default disabled; RefreshSaveMenuEnabledState
            // flips it on when a TreArchive document is loaded. Honest tooltip
            // wording mirrors the round-2 MEDIUM 5 disposition on Unknown.
            miRepackTre.Enabled = false;
            miRepackTre.ToolTipText = "Open from a packed .tre to repack the source archive.";
            miRepackTre.Click += OnRepackTre;
            saveMenu.Items.AddRange(new ToolStripItem[]
            {
                miSaveInPlace, miSaveLooseOverride, miSaveAs,
                new ToolStripSeparator(),
                miPatchLive, miRepackTre,
            });
        }

        // Pattern-match the current Source against the four sealed cases and gate each Save
        // mode accordingly. Round-2 MEDIUM 5: Save As… is ALWAYS enabled when a document is
        // loaded (incl. on OpenSource.Unknown) — it's the user's explicit escape hatch.
        private void RefreshSaveMenuEnabledState()
        {
            bool hasDoc = document != null;
            bool isLoose = hasDoc && Source is OpenSource.LooseFile;
            bool isTre = hasDoc && Source is OpenSource.TreArchive;
            bool isClientMemory = hasDoc && Source is OpenSource.ClientMemory;
            bool isUnknown = hasDoc && Source is OpenSource.Unknown;
            bool inFlight = saveInFlight;
            // 08-06: live-patch is the only mode that touches the running client.
            // The Save▾ item is provenance-gated on ClientMemory AND Game.IsRunning
            // (08-REVIEWS HIGH-2). No current Phase-8 open path constructs ClientMemory
            // (round-2 MEDIUM 11), so this branch is reachable only via a follow-up
            // discovery path or a maintainer-only debug construction during Tier-4
            // verification.
            bool clientUp = false;
            try { clientUp = Game.IsRunning; }
            catch { clientUp = false; /* binding unavailable outside an injected client */ }

            // Reload tooltip copy per round-2 MEDIUM 5: on Unknown the four provenance-gated
            // modes show the documented "Cannot resolve archive record" message.
            const string unknownTooltip = "Cannot resolve archive record — use Save As to write to a chosen file.";

            if (miSaveInPlace != null)
            {
                miSaveInPlace.Enabled = isLoose && !inFlight;
                miSaveInPlace.ToolTipText = isUnknown
                    ? unknownTooltip
                    : (isLoose ? "" : "Open from a loose .iff to save in place.");
            }
            if (miSaveLooseOverride != null)
            {
                miSaveLooseOverride.Enabled = (isLoose || isTre) && !inFlight;
                miSaveLooseOverride.ToolTipText = isUnknown
                    ? unknownTooltip
                    : (isLoose || isTre ? "" : "Available when the document has a logical path.");
            }
            if (miSaveAs != null)
            {
                miSaveAs.Enabled = hasDoc && !inFlight;
                // Save As is ALWAYS available on a loaded document (round-2 MEDIUM 5).
                miSaveAs.ToolTipText = "Save the current edits to a path you choose.";
            }
            if (miPatchLive != null)
            {
                // 08-06: enable iff Source is ClientMemory AND a live client is up
                // AND no save is in flight. Otherwise disabled with the honest
                // future-phase tooltip (round-2 MEDIUM 11).
                miPatchLive.Enabled = isClientMemory && clientUp && !inFlight;
                if (isUnknown)
                {
                    miPatchLive.ToolTipText = unknownTooltip;
                }
                else if (isClientMemory && !clientUp)
                {
                    miPatchLive.ToolTipText = "No live client — start SWG to patch the running client.";
                }
                else if (isClientMemory)
                {
                    miPatchLive.ToolTipText = "Patch the running client's mapped IFF region (same-length only).";
                }
                else
                {
                    miPatchLive.ToolTipText = "Live patch requires opening from client memory — not wired in this phase.";
                }
            }
            if (miRepackTre != null)
            {
                // 08-07: provenance-gated on TreArchive (08-REVIEWS HIGH-2). Also gated on
                // !inFlight (MEDIUM-9 stale-bytes reload race barrier) — the repack is the
                // highest-risk save mode, so concurrent saves are explicitly serialized.
                miRepackTre.Enabled = isTre && !inFlight;
                if (isUnknown)
                {
                    miRepackTre.ToolTipText = unknownTooltip;
                }
                else if (isTre)
                {
                    miRepackTre.ToolTipText =
                        "Repack the source .tre. Defaults to a timestamped backup; refuses cleanly if the client holds the archive open.";
                }
                else
                {
                    miRepackTre.ToolTipText = "Open from a packed .tre to repack the source archive.";
                }
            }
            btnSave.Enabled = hasDoc;
        }

        // ── Save▾ drop-down trigger ─────────────────────────────────────────

        private void OnSaveButtonClick(object sender, EventArgs e)
        {
            if (document == null || saveMenu == null) return;
            // Anchor the drop-down at the bottom of the Save button.
            saveMenu.Show(btnSave, new Point(0, btnSave.Height));
        }

        // ── Save handlers ───────────────────────────────────────────────────

        private async void OnSaveInPlace(object sender, EventArgs e)
        {
            if (document == null) return;
            var loose = Source as OpenSource.LooseFile;
            if (loose == null)
            {
                lblStatus.Text = "Cannot resolve archive record — use Save As to write to a chosen file.";
                lblStatus.ForeColor = Color.Red;
                return;
            }
            await DoFileSaveAsync(() => IffSaveTargets.SaveInPlace(document, Source), "in place");
        }

        private async void OnSaveLooseOverride(object sender, EventArgs e)
        {
            if (document == null) return;
            string resolvedRoot = ResolveClientRoot();
            if (string.IsNullOrEmpty(resolvedRoot))
            {
                lblStatus.Text =
                    "Could not locate the client root — use Save As… and we'll remember the directory.";
                lblStatus.ForeColor = Color.Red;
                return;
            }
            string subDir = ini.GetString("IffEditor", "looseOverrideDir");
            // Planner's best-guess default subdir when none is recorded yet (round-2 MEDIUM 10).
            // Tier-4 smoke (Task 5) confirms or overrides this; the Save As… fallback path
            // (OnSaveAs below) persists the user-chosen directory back into the ini key.
            if (string.IsNullOrEmpty(subDir)) subDir = "loose";
            await DoFileSaveAsync(
                () => IffSaveTargets.SaveLooseOverride(document, Source, resolvedRoot, subDir),
                "loose override");
        }

        private async void OnSaveAs(object sender, EventArgs e)
        {
            if (document == null) return;
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Save IFF as…";
                sfd.Filter = "IFF files (*.iff)|*.iff|All files (*.*)|*.*";
                sfd.FileName = !string.IsNullOrEmpty(displayName) ? displayName : "untitled.iff";
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                string path = sfd.FileName;
                bool ok = await DoFileSaveAsync(
                    () => IffSaveTargets.SaveToPath(document, path),
                    "save-as");
                // Round-2 MEDIUM 10: persist the user-chosen directory back to
                // [IffEditor] looseOverrideDir so the NEXT loose-override save defaults
                // to the user's preferred location (when the planner's-best-guess subdir
                // didn't match the live client).
                if (ok)
                {
                    IffSaveTargets.RecordSaveAsDirectory(ini, path);
                }
            }
        }

        // ── Patch live client (08-06 / D-05.3) ──────────────────────────────
        //
        // Provenance-gated on `Source is OpenSource.ClientMemory cm` AND Game.IsRunning
        // (08-REVIEWS HIGH-2). Shows FormSaveConfirmDialog with the UI-SPEC §Destructive
        // heading/body/verbs; on Accept, serializes via IffWriter.Write and queues the
        // game-thread CON-N-04 write via LivePatchSaveTarget.Apply. The patch is
        // VOLATILE by design (lost on reload / scene change) — the candid copy in the
        // confirm body states this.
        //
        // The patch is NOT a file save — it does NOT clear the dirty marker and does
        // NOT update lastSavedPath (the Reload button is for file-based saves).
        private void OnPatchLive(object sender, EventArgs e)
        {
            if (document == null) return;
            var cm = Source as OpenSource.ClientMemory;
            if (cm == null)
            {
                // Defensive — the menu item should not have been enabled. Surface the
                // honest tooltip wording as the status copy and stop.
                lblStatus.Text = "Live patch requires opening from client memory — not wired in this phase.";
                lblStatus.ForeColor = Color.Red;
                return;
            }

            // UI-SPEC §Destructive — heading, body, explicit verb captions. Body
            // emphasis renders in Color.Red inside the dialog (the documented
            // destructive exception); the confirm dialog enforces that.
            const string heading = "Patch the live client in memory?";
            const string body = "This writes your edits straight into the running client. " +
                                "The change is temporary (lost on reload) and can destabilize " +
                                "the session. Continue?";

            using (var dlg = new FormSaveConfirmDialog(
                heading: heading,
                body: body,
                acceptVerb: "Patch live",
                cancelVerb: "Cancel",
                showBackupCheckbox: false))
            {
                dlg.ShowDialog(this);
                if (dlg.Outcome != FormSaveConfirmDialog.ConfirmOutcome.Accepted) return;
            }

            // Serialize the document. IffWriter.Write throws on over-cap chunks (08-01
            // 64 MB cap) — surface as a save-time validation, not a crash.
            byte[] rewritten;
            try
            {
                rewritten = IffWriter.Write(document);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Live patch failed (serialize): " + ex.Message + " Your edits are kept in the editor.";
                lblStatus.ForeColor = Color.Red;
                return;
            }

            // Queue the CON-N-04 write through the framework-side LivePatchValidator
            // bounds gate. LivePatchSaveTarget never touches the UI thread; the write
            // is queued via GameCallbacks.AddMainLoopCall.
            LivePatchSaveTarget.LivePatchResult result =
                LivePatchSaveTarget.Apply(cm, rewritten);

            switch (result)
            {
                case LivePatchSaveTarget.LivePatchResult.Applied:
                    // Volatile — does NOT clear the dirty marker (live patch is not a
                    // file save). The candid status mirrors UI-SPEC §States Reloading
                    // wording for the live-patch path.
                    lblStatus.Text = "Saving (live patch)… Applied.";
                    lblStatus.ForeColor = Colors.Font();
                    break;
                case LivePatchSaveTarget.LivePatchResult.RefusedSameLength:
                    lblStatus.Text =
                        "Live patch requires the rewritten IFF to be the same length as the original. Save to file/repack instead.";
                    lblStatus.ForeColor = Color.Red;
                    break;
                case LivePatchSaveTarget.LivePatchResult.RefusedZeroTarget:
                    lblStatus.Text = "Live patch target address is invalid. Save to file/repack instead.";
                    lblStatus.ForeColor = Color.Red;
                    break;
                case LivePatchSaveTarget.LivePatchResult.RefusedNoClient:
                default:
                    // Defensive: the menu item should not have been clickable when
                    // Game.IsRunning is false. The honest copy.
                    lblStatus.Text = "No live client.";
                    lblStatus.ForeColor = Color.Red;
                    break;
            }
        }

        // ── Repack into source .tre (08-07 / D-05.4) ────────────────────────
        //
        // Provenance-gated on `Source is OpenSource.TreArchive ta` (08-REVIEWS HIGH-2).
        // Shows FormSaveConfirmDialog with the heading "Repack <archive>.tre?", the
        // body explaining the full-rebuild + atomic-replace path, AND the opt-in
        // "back up first" checkbox DEFAULTED ON (08-REVIEWS MEDIUM-10 — never
        // overwrite a prior timestamped backup). On Accept, serializes via
        // IffWriter.Write and calls TreRepackSaveTarget.Apply on a background Task.
        //
        // Result translation:
        //   Replaced / BackedUpThenReplaced → success status + offer the tiered reload
        //     (08-05 ClientReloadDispatcher); the lastSavedPath is set to the .tre so
        //     the Reload button knows the asset to classify.
        //   RefusedClientHoldsArchive_LooseOverrideRecommended → candid status copy
        //     and pre-select the "Save as loose override" Save▾ item (do NOT auto-save —
        //     the user must consent to the alternative save).
        //   Failed → status "Repack failed — your edits are retained. See log."
        private async void OnRepackTre(object sender, EventArgs e)
        {
            if (document == null) return;
            var ta = Source as OpenSource.TreArchive;
            if (ta == null)
            {
                // Defensive — the menu item should not have been enabled.
                lblStatus.Text = "Open from a packed .tre to repack the source archive.";
                lblStatus.ForeColor = Color.Red;
                return;
            }

            string archiveName = Path.GetFileName(ta.TrePath ?? "");

            // UI-SPEC §Destructive — heading, body, explicit verb captions. The
            // FormSaveConfirmDialog renders the body in Color.Red as the documented
            // destructive exception. Backup checkbox is shown AND defaulted on per
            // 08-REVIEWS MEDIUM-10 (Assumption #5).
            string heading = "Repack " + archiveName + "?";
            string body = "This rewrites the entire " + archiveName + " archive on disk and "
                        + "replaces it atomically. Untouched entries are preserved byte-for-byte; "
                        + "only the edited entry recompresses. If the client holds the archive open, "
                        + "the repack is refused without a partial-write. Continue?";

            bool backupRequested;
            using (var dlg = new FormSaveConfirmDialog(
                heading: heading,
                body: body,
                acceptVerb: "Repack",
                cancelVerb: "Cancel",
                showBackupCheckbox: true,
                backupCheckboxLabel: "Create a timestamped backup (" + archiveName + ".<yyyyMMdd-HHmmss>.bak) first"))
            {
                dlg.ShowDialog(this);
                if (dlg.Outcome != FormSaveConfirmDialog.ConfirmOutcome.Accepted) return;
                backupRequested = dlg.BackupRequested;
            }

            // Serialize the document. IffWriter.Write throws on over-cap chunks (08-01
            // 64 MB cap) — surface as a save-time validation, not a crash.
            byte[] rewritten;
            try
            {
                rewritten = IffWriter.Write(document);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Repack failed (serialize): " + ex.Message + " Your edits are kept in the editor.";
                lblStatus.ForeColor = Color.Red;
                return;
            }

            saveInFlight = true;
            RefreshSaveMenuEnabledState();
            RefreshReloadButtonState();
            lblStatus.Text = "Saving (repack " + archiveName + ")…";
            lblStatus.ForeColor = Colors.Font();

            TreRepackSaveTarget.TreRepackResult result;
            try
            {
                result = await TreRepackSaveTarget.Apply(ta, rewritten, backupRequested).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Repack failed: " + ex.Message + " Your edits are kept in the editor.";
                lblStatus.ForeColor = Color.Red;
                saveInFlight = false;
                RefreshSaveMenuEnabledState();
                RefreshReloadButtonState();
                return;
            }

            saveInFlight = false;

            switch (result)
            {
                case TreRepackSaveTarget.TreRepackResult.Replaced:
                case TreRepackSaveTarget.TreRepackResult.BackedUpThenReplaced:
                    // Success — the .tre on disk now contains the edit. lastSavedPath
                    // is set to the .tre's PATH (not the logical-entry path) so the
                    // Reload button feeds the .tre's extension to the classifier; the
                    // tiered reload outcome (texture / terrain / pending / unavailable)
                    // will reflect the asset kind of the .tre as a whole (typically
                    // PendingNextSceneChange for a generic .tre — no in-session reload).
                    lastSavedPath = ta.TrePath;
                    string ok = result == TreRepackSaveTarget.TreRepackResult.BackedUpThenReplaced
                        ? "Repacked " + archiveName + " (backup created)"
                        : "Repacked " + archiveName;
                    lblStatus.Text = ok;
                    lblStatus.ForeColor = Colors.Font();
                    break;

                case TreRepackSaveTarget.TreRepackResult.RefusedClientHoldsArchive_LooseOverrideRecommended:
                    // 08-REVIEWS MEDIUM-10 honest fallback. Pre-select the loose-override
                    // save mode by anchoring the Save▾ drop-down with that item highlighted
                    // — do NOT auto-save; the user must consent.
                    lblStatus.Text = "The client appears to hold " + archiveName
                        + " open. Save a loose override of the edited entry instead, or close the client and retry.";
                    lblStatus.ForeColor = Color.Red;
                    // Pre-select the loose-override save mode: show the menu with the
                    // loose-override item as the default-highlighted choice.
                    if (saveMenu != null && miSaveLooseOverride != null && miSaveLooseOverride.Enabled)
                    {
                        saveMenu.Show(btnSave, new Point(0, btnSave.Height));
                        miSaveLooseOverride.Select();
                    }
                    break;

                case TreRepackSaveTarget.TreRepackResult.Failed:
                default:
                    lblStatus.Text = "Repack failed — your edits are retained. See log.";
                    lblStatus.ForeColor = Color.Red;
                    break;
            }

            RefreshSaveMenuEnabledState();
            RefreshReloadButtonState();
        }

        // Shared file-save orchestration: lock the toolbar Reload button while the save Task is
        // in flight (08-REVIEWS MEDIUM-9 stale-bytes reload race), surface the Saving/Saved/
        // Save-failed status copy per UI-SPEC, refresh the dirty / undo-redo visuals.
        private async Task<bool> DoFileSaveAsync(
            Func<Task<IffSaveTargets.SaveResult>> saveOp,
            string modeLabel)
        {
            saveInFlight = true;
            RefreshSaveMenuEnabledState();
            RefreshReloadButtonState();
            lblStatus.Text = "Saving (" + modeLabel + ")…";
            lblStatus.ForeColor = Colors.Font();

            IffSaveTargets.SaveResult result;
            try
            {
                result = await saveOp().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                result = IffSaveTargets.SaveResult.Failure(ex.Message);
            }

            saveInFlight = false;

            if (result.Ok)
            {
                lblStatus.Text = "Saved " + (displayName ?? Path.GetFileName(result.Path) ?? "<untitled>") + " (" + modeLabel + ")";
                lblStatus.ForeColor = Colors.Font();
                lastSavedPath = result.Path;
                // The hybrid DOM keeps the verbatim source bytes for unedited subtrees, but the
                // canonical "saved baseline" semantics live in the controller. We do NOT clear
                // controller.IsDirty here — the SUMMARY documents that the controller's
                // baseline-clean dirty model is the source of truth, and the file save is an
                // orthogonal action. The UI nevertheless clears the lblDirty banner so a fresh
                // baseline can be re-established by an explicit Reload-from-disk if the user
                // chooses. See round-2 follow-up for the dirty-after-save audit (DEFERRED).
            }
            else
            {
                lblStatus.Text = (result.Message ?? "Save failed.") + " Your edits are kept in the editor — try another save target.";
                lblStatus.ForeColor = Color.Red;
            }
            RefreshSaveMenuEnabledState();
            RefreshReloadButtonState();
            return result.Ok;
        }

        // ── Open… ───────────────────────────────────────────────────────────

        private void OnOpenClicked(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Open IFF…";
                ofd.Filter = "IFF files (*.iff)|*.iff|All files (*.*)|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                OpenFromLooseFile(ofd.FileName);
            }
        }

        private void OpenFromLooseFile(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                IffDocument doc;
                using (var ms = new MemoryStream(bytes, writable: false))
                {
                    doc = IffReader.Read(ms);
                }
                MutableIffDocument mut = MutableIffDocument.FromDocument(doc, bytes);
                LoadDocument(mut, new OpenSource.LooseFile(path), Path.GetFileName(path));
                lblStatus.Text = "Opened " + Path.GetFileName(path);
                lblStatus.ForeColor = Colors.Font();
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Open failed: " + ex.Message;
                lblStatus.ForeColor = Color.Red;
            }
        }

        // ── TRE Browser hand-off (08-05 Task 4 — populates OpenSource.TreArchive) ──

        /// <summary>
        /// Entry point invoked by <c>FormTreBrowser</c>'s "Open in IFF Editor" context-menu
        /// item. Constructs the appropriate <see cref="OpenSource"/> from the descriptor
        /// fields (TreArchive on a resolved record-index, Unknown on degraded fallback per
        /// checker W-3) and binds the document into the editor.
        /// </summary>
        /// <param name="payload">Resolved payload bytes (from FormTreBrowser's DispatchDetail).</param>
        /// <param name="resolvedArchivePath">The physical .tre path
        /// (<c>TreEntryDescriptor.ResolvedArchivePath</c>).</param>
        /// <param name="logicalPath">The entry's logical path inside the archive
        /// (<c>TreEntryDescriptor.Path</c>).</param>
        /// <param name="archiveLocalOffset">The payload offset inside the .tre
        /// (<c>TreEntryDescriptor.ArchiveLocalOffset</c>).</param>
        public void OpenFromTreEntry(
            byte[] payload,
            string resolvedArchivePath,
            string logicalPath,
            long archiveLocalOffset)
        {
            if (payload == null)
            {
                lblStatus.Text = "TRE entry has no payload to open.";
                lblStatus.ForeColor = Color.Red;
                return;
            }
            try
            {
                IffDocument doc;
                using (var ms = new MemoryStream(payload, writable: false))
                {
                    doc = IffReader.Read(ms);
                }
                MutableIffDocument mut = MutableIffDocument.FromDocument(doc, payload);
                // Resolve recordIndex via the framework-side helper. On success → TreArchive;
                // on failure → Unknown.Instance (checker W-3 — Save-In-Place + Repack stay
                // disabled; Save As remains as the user's escape hatch per round-2 MEDIUM 5).
                OpenSource src = TreRecordIndexResolver.ResolveOrUnknown(
                    resolvedArchivePath, archiveLocalOffset, logicalPath);
                string name = logicalPath ?? Path.GetFileName(resolvedArchivePath ?? "");
                LoadDocument(mut, src, name);
                // Surface the source kind so the user sees the provenance the editor inferred.
                if (src is OpenSource.TreArchive)
                {
                    lblStatus.Text = "Opened " + name + " from " + Path.GetFileName(resolvedArchivePath ?? "");
                    lblStatus.ForeColor = Colors.Font();
                }
                else
                {
                    lblStatus.Text = "Opened " + name + " — record index unresolved; use Save As to write to a chosen file.";
                    lblStatus.ForeColor = Colors.Font();
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "TRE hand-off failed: " + ex.Message;
                lblStatus.ForeColor = Color.Red;
            }
        }

        // ── Reload-in-client ────────────────────────────────────────────────

        private void OnReloadClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(lastSavedPath))
            {
                lblStatus.Text = "Save first — Reload uses the last-saved path to classify the asset.";
                lblStatus.ForeColor = Color.Red;
                return;
            }
            ReloadTier tier = ClientReloadDispatcher.Dispatch(lastSavedPath, rootTypeId);
            switch (tier)
            {
                case ReloadTier.ReloadedTextures:
                    lblStatus.Text = "Reloaded (textures)";
                    lblStatus.ForeColor = Colors.Font();
                    break;
                case ReloadTier.ReloadedTerrain:
                    lblStatus.Text = "Reloaded (terrain)";
                    lblStatus.ForeColor = Colors.Font();
                    break;
                case ReloadTier.PendingNextSceneChange:
                    lblStatus.Text = "Reloads on next scene change";
                    lblStatus.ForeColor = Colors.Font();
                    toolTip.SetToolTip(btnReload,
                        "Reloads on next scene change. Datatables, string tables, and object templates can't be hot-swapped — the running scene caches them.");
                    break;
                case ReloadTier.Unavailable:
                default:
                    lblStatus.Text = "No live client — start SWG to reload edits in-session.";
                    lblStatus.ForeColor = Color.Red;
                    break;
            }
        }

        private void RefreshReloadButtonState()
        {
            // Disabled while a save Task is in flight (MEDIUM-9). Otherwise enabled iff we
            // have a last-saved path AND a live client.
            bool clientUp = false;
            try { clientUp = Game.IsRunning; }
            catch { clientUp = false; }
            bool enable = !saveInFlight && !string.IsNullOrEmpty(lastSavedPath) && clientUp;
            btnReload.Enabled = enable;
            if (!clientUp)
            {
                toolTip.SetToolTip(btnReload, "No live client — start SWG to reload edits in-session.");
            }
            else if (saveInFlight)
            {
                toolTip.SetToolTip(btnReload, "Disabled while save is in progress.");
            }
            else if (string.IsNullOrEmpty(lastSavedPath))
            {
                toolTip.SetToolTip(btnReload, "Save the document first to enable Reload.");
            }
            else
            {
                toolTip.SetToolTip(btnReload, "");
            }
        }

        // Resolves the client install root the same way FormTreBrowser does (process-module
        // primary, then GetWorkingDirectory(), then the [IffEditor] / [TreBrowser] ini fallback).
        // Kept private to FormIffEditor to avoid coupling to FormTreBrowser internals; the
        // small duplication is acceptable per round-2 MEDIUM 10's "best-guess default subdir"
        // disposition.
        private string ResolveClientRoot()
        {
            // (1) Process module directory.
            try
            {
                string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                string moduleDir = Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(moduleDir) && Directory.Exists(moduleDir))
                {
                    return moduleDir;
                }
            }
            catch { /* fall through */ }
            // (2) GetWorkingDirectory.
            try
            {
                string wd = UtinniCore.Utility.utility.GetWorkingDirectory();
                if (!string.IsNullOrEmpty(wd) && Directory.Exists(wd))
                {
                    return wd;
                }
            }
            catch { /* binding unavailable outside a live client */ }
            // (3) ini fallback — sibling [TreBrowser] clientDir is the documented source.
            try
            {
                string configured = ini.GetString("TreBrowser", "clientDir");
                if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
                {
                    return configured;
                }
            }
            catch { /* ini may not have the key yet */ }
            return null;
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

            // Singleton form (Plugin.cs registers ONE instance at load): user-initiated close
            // hides instead of disposing so subsequent FindOrCreateIffEditor().Show() calls
            // from the TRE Browser "Open in IFF Editor" hand-off (and the TJT window menu)
            // can re-Show this same instance. Default WinForms behavior disposes on close,
            // which makes the next Show() throw ObjectDisposedException at Form.CreateHandle
            // (observed during 08-05 live smoke when opening a second IFF after closing the
            // first). Editor-host shutdown (CloseReason.ApplicationExitCall /
            // TaskManagerClosing / WindowsShutDown) still falls through and disposes normally.
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }
    }
}
