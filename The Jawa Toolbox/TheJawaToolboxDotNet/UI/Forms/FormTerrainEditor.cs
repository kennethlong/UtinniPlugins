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
// The roomy tree+field-editor host Form the thin docked Terrain SubPanel (Plan 03) launches (21-CONTEXT
// D-02 escape hatch — the SubPanel is hard-pinned to 417px, too narrow for a tree+grid split). It is a
// pure CONSUMER of the shipped Phase 20 terrain codec (TerrainDocument / TerrainLayer / TerrainNode /
// TerrainPalettes / TgenFieldLayouts) and the Plan 01 in-proc save + candor helpers (TerrainSaveTargets,
// TerrainReloadCandor) and the Phase 8 reload dispatcher (ClientReloadDispatcher). It adds ZERO new
// format/reload logic: no byte offsets (TgenFieldLayouts is the single source), no reload trigger of its
// own (ClientReloadDispatcher.Dispatch is the only path), no candor wording of its own
// (TerrainReloadCandor.StatusCopy is the only source). Implementation original to Utinni under MIT.

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using UtinniCore.Utinni;                       // Game.IsRunning (P/Invoke — ALWAYS try/catch)
using UtinniCoreDotNet.Formats.Iff;            // OpenSource
using UtinniCoreDotNet.Formats.Terrain;        // TerrainDocument / TerrainLayer / TerrainNode / palettes
using UtinniCoreDotNet.PluginFramework;        // IEditorPlugin (undo seam)
using UtinniCoreDotNet.Saving;                 // ReloadTier, TerrainReloadCandor
using UtinniCoreDotNet.UI.Controls;            // UtinniLabel / UtinniButton
using UtinniCoreDotNet.UI.Theme;               // Colors
using TJT.Saving;                              // ClientReloadDispatcher, TerrainSaveTargets

namespace TJT.UI.Forms
{
    /// <summary>
    /// The roomy terrain tree+field-editor host Form (21-02). Opens a <c>.trn</c> (read-only from a TRE
    /// archive, or directly from a loose override), renders the navigable TGEN layer tree + the six
    /// read-only shared palettes, shows typed editable fields for Tier-1 tags and a read-only generic field
    /// list for everything else (NEVER a hard decode failure — 21-CONTEXT D-03 / Phase 20 D-01/D-02), edits
    /// fixed-length scalar/enum leaves + active-flag toggles, saves byte-exact through the Plan 01 in-proc
    /// <see cref="TerrainSaveTargets"/> loose-override path, and fires the live preview on BOTH Save and an
    /// explicit manual Preview through the EXISTING <see cref="ClientReloadDispatcher"/> reload tier with
    /// honest tiered candor (<see cref="TerrainReloadCandor"/>, D-07 honest default).
    ///
    /// <para><b>MEF-safety (D-09 / Pitfall 8):</b> the entire control build runs inside a try/catch so a
    /// partial build surfaces a state panel rather than throwing the ctor — a throwing registered child
    /// silently cascades the whole IEditorPlugin out of MEF compose. The nested <see cref="SplitContainer"/>
    /// sets <c>Size</c> BEFORE <c>SplitterDistance</c> and the <c>Dock.Fill</c> content region is added
    /// FIRST (front-most) so the docked Top/Bottom strips claim their edges
    /// (<c>[[feedback_winforms_dockfill_zorder]]</c>).</para>
    /// </summary>
    public partial class FormTerrainEditor : Form
    {
        // ── LOCKED copy (21-UI-SPEC Copywriting Contract — verbatim; kept as constants so the grep gates
        //    can verify them and a future edit can't silently soften the wording). ──
        private const string BaseTitle = "Terrain";
        private const string DecA3Footer =
            "Preview is live in the real SWG client — Utinni never renders terrain itself.";
        private const string EmptyHeading = "No terrain loaded";
        private const string EmptyBody =
            "Open a planet's .trn from the TRE Browser, or open an existing loose override, to edit its procedural layers.";
        private const string NoSelectionBody =
            "Select a layer, boundary, filter, or affector to view its fields.";
        private const string PaletteReadOnlyLabel = "Shared palette (read-only)";
        private const string RawFieldHint =
            "This tag isn't typed yet — its original bytes are preserved exactly and saved unchanged.";
        private const string ObsoleteTagHint =
            "Obsolete tag — recognized, ignored, and re-emitted unchanged. Not editable.";
        private const string NameReadOnlyHint =
            "Name editing isn't supported yet — names are shown read-only.";
        private const string NothingToPreviewGuard = "Nothing to preview — edit a field first.";
        private const string SaveFirstPreviewGuard =
            "Save first — Preview uses the last edit to classify the reload.";
        private const string PreviewNoHookTooltip =
            "Live terrain preview isn't wired this build — edits show on the next scene change or relog.";
        private const string SaveAsOverrideLabel = "Save As Override…";
        private const string SaveLabel = "Save";

        // The plugin ref — held for the D-09 undo seam (Undo/Redo/ClearUndoStack are NULL until FormMain
        // wires them; every call site null-checks via ?.Invoke()).
        private readonly IEditorPlugin editorPlugin;
        private readonly ToolTip toolTip = new ToolTip();

        // ── Model + provenance (null until an open path binds a document). ──
        private TerrainDocument document;
        private OpenSource source;
        private string displayName;
        // The last path written by Save (loose override). Drives the Preview save-first guard + Dispatch.
        private string lastSavedPath;
        // True when an edit is pending in the in-memory DOM but not yet saved to disk.
        private bool hasPendingEdit;

        // ── Controls (built imperatively in BuildLayout). ──
        private UtinniLabel lblBanner;
        private Panel pnlBannerAccent;     // 2px Colors.Secondary() accent rule under the banner
        private UtinniButton btnOpen;
        private UtinniButton btnSave;
        private UtinniButton btnPreview;
        private UtinniLabel lblStatus;
        private UtinniLabel lblCandorFooter; // dimmed DEC-A3 candor footer
        private SplitContainer splitMain;    // tree (Panel1) | field pane (Panel2)
        private TreeView treeLayers;
        private Panel pnlFieldHost;          // the field-editor host (a TreDetailPane-style custom pane)

        // True once BuildLayout completed without throwing. A failed build surfaces a state panel and the
        // open/edit entry points no-op defensively.
        private bool layoutReady;

        /// <summary>
        /// Constructs the terrain editor host. Stores the plugin ref for the D-09 undo seam and builds the
        /// whole layout inside a try/catch (MEF-safety, D-09 / Pitfall 8): a throwing ctor would cascade the
        /// entire IEditorPlugin out of MEF compose with no error, so a partial build surfaces a state panel
        /// instead of throwing.
        /// </summary>
        public FormTerrainEditor(IEditorPlugin editorPlugin)
        {
            this.editorPlugin = editorPlugin;

            // The WinForms partial-class contract: the Designer file owns components/Dispose only.
            try
            {
                BuildLayout();
                layoutReady = true;
                ShowEmptyState();
            }
            catch (Exception ex)
            {
                // A partial build must NEVER throw out of the ctor (the MEF-killer). Surface a minimal
                // state panel so the user sees the failure and the host stays composed.
                SurfaceBuildFailure(ex);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Layout — Pitfall 8 LOCKED idiom: Dock.Fill content FIRST (front-most), Size BEFORE
        // SplitterDistance, Panel1MinSize/Panel2MinSize collapse guards. Theme via Colors.* only.
        // ─────────────────────────────────────────────────────────────────────

        private void BuildLayout()
        {
            SuspendLayout();

            Text = BaseTitle;
            BackColor = Colors.Primary();
            ForeColor = Colors.Font();
            Font = new Font("Segoe UI", 8.25f); // base 8.25pt; Bold reserved to the banner.
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(560, 360);
            StartPosition = FormStartPosition.CenterParent;

            // ── Dock.Fill content region (the nested SplitContainer) — added FIRST so the Top/Bottom
            //    strips claim their edges. (feedback_winforms_dockfill_zorder) ──
            splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,   // tree left | field pane right
                BackColor = Colors.PrimaryHighlight(),
                SplitterWidth = 4,
                Panel1MinSize = 40,                    // tree collapse guard (TreDetailPane precedent)
                Panel2MinSize = 80,                    // field-pane collapse guard
                FixedPanel = FixedPanel.Panel1,        // tree keeps its width on resize
            };
            // LOCKED (Pitfall 8): Size BEFORE SplitterDistance or the ctor throws and MEF silently drops
            // the whole plugin. Definite sizes here keep SplitterDistance valid at construction.
            splitMain.Size = new Size(900, 560);
            splitMain.SplitterDistance = 280;

            treeLayers = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = Colors.PrimaryHighlight(),
                ForeColor = Colors.Font(),
                BorderStyle = BorderStyle.None,
                HideSelection = false,
                ShowLines = true,
                ShowPlusMinus = true,
                ShowRootLines = true,
                FullRowSelect = false,
            };
            treeLayers.AfterSelect += OnTreeAfterSelect;

            pnlFieldHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Colors.Primary(),
                Padding = new Padding(8),
                AutoScroll = true,
            };

            splitMain.Panel1.Controls.Add(treeLayers);   // tree → left
            splitMain.Panel2.Controls.Add(pnlFieldHost); // field pane → right

            // ── Dock.Top banner + 2px accent rule ──
            pnlBannerAccent = new Panel
            {
                Dock = DockStyle.Top,
                Height = 2,
                BackColor = Colors.Secondary(), // accent reserved use #1 (UI-SPEC Color)
            };
            lblBanner = new UtinniLabel
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                ForeColor = Colors.Font(),
                Font = new Font(Font, FontStyle.Bold), // the ONE authorized Bold use (the banner)
                Text = BaseTitle,
            };

            // ── Dock.Top action bar (Open / Save / Preview) ──
            Panel pnlActions = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                BackColor = Colors.Primary(),
                Padding = new Padding(8, 4, 8, 4),
            };
            btnOpen = MakeActionButton("Open", 0);
            btnSave = MakeActionButton(SaveLabel, 84);
            btnPreview = MakeActionButton("Preview", 168);
            btnOpen.Click += OnOpenClicked;
            btnSave.Click += OnSaveClicked;
            btnPreview.Click += OnPreviewClicked;
            toolTip.SetToolTip(btnOpen, "Open a loose-override .trn for editing.");
            toolTip.SetToolTip(btnSave, "Save the edited field to a loose override.");
            toolTip.SetToolTip(btnPreview, "Apply the last edit to the live client (no commit to disk).");
            pnlActions.Controls.Add(btnPreview);
            pnlActions.Controls.Add(btnSave);
            pnlActions.Controls.Add(btnOpen);

            // ── Dock.Bottom status + dimmed DEC-A3 candor footer ──
            lblCandorFooter = new UtinniLabel
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 18,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                ForeColor = Colors.FontDisabled(), // dimmed DEC-A3 candor (verbatim)
                Text = DecA3Footer,
            };
            lblStatus = new UtinniLabel
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 18,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                ForeColor = Colors.Font(),
                Text = "",
            };

            // ── Assemble (Fill child FIRST, then Top/Bottom strips). ──
            Controls.Add(splitMain);          // Dock.Fill — front-most
            Controls.Add(pnlActions);         // Dock.Top
            Controls.Add(lblBanner);          // Dock.Top
            Controls.Add(pnlBannerAccent);    // Dock.Top (accent rule sits directly under the banner)
            Controls.Add(lblStatus);          // Dock.Bottom
            Controls.Add(lblCandorFooter);    // Dock.Bottom

            ResumeLayout(false);
            PerformLayout();

            RefreshActionButtonState();
        }

        private UtinniButton MakeActionButton(string text, int x)
        {
            return new UtinniButton
            {
                Text = text,
                Location = new Point(8 + x, 4),
                Size = new Size(80, 22),
                DrawOutline = false,
                UseDisableColor = true,
                UseVisualStyleBackColor = true,
            };
        }

        // Minimal failure surface: a single read-only label so a partial build never throws the ctor.
        private void SurfaceBuildFailure(Exception ex)
        {
            try
            {
                Controls.Clear();
                Text = BaseTitle;
                BackColor = Colors.Primary();
                var lbl = new UtinniLabel
                {
                    Dock = DockStyle.Fill,
                    ForeColor = Color.Red,
                    Padding = new Padding(12),
                    Text = "Terrain editor failed to initialize: " + (ex == null ? "(unknown)" : ex.Message),
                };
                Controls.Add(lbl);
            }
            catch
            {
                // Last-ditch: even the failure surface must not re-throw out of the ctor.
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Open paths (D-08) — TRE source's first commit is "Save As Override…"; a loose source commits in
        // place. Both decode via TerrainDocument.FromBytes inside try/catch (red status on failure — NEVER
        // a hard throw).
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Opens a <c>.trn</c> handed off read-only from the TRE Browser. Decodes via
        /// <see cref="TerrainDocument.FromBytes"/> inside try/catch (red status on failure), records the TRE
        /// provenance (first commit is "Save As Override…"), and populates the tree + palettes.
        /// </summary>
        public void OpenFromTreEntry(byte[] payload, string archivePath, string logicalPath)
        {
            if (!layoutReady) return;
            if (payload == null || payload.Length == 0)
            {
                SetStatus("TRE entry has no payload to open.", true);
                return;
            }
            try
            {
                TerrainDocument doc = TerrainDocument.FromBytes(payload);
                string rel = string.IsNullOrEmpty(logicalPath)
                    ? Path.GetFileName(archivePath ?? "")
                    : logicalPath;
                // A TRE record opened read-only — never edited in place. We do not need a resolved record
                // index here: the loose-override save derives the relative path from LogicalPath.
                OpenSource src = new OpenSource.TreArchive(
                    archivePath ?? "", 0, rel ?? "");
                BindDocument(doc, src, rel);
                SetStatus("Opened " + rel + " (read-only TRE — first save writes a loose override).", false);
            }
            catch (Exception ex)
            {
                SetStatus("Open failed: " + ex.Message, true);
            }
        }

        /// <summary>
        /// Opens an existing loose-override <c>.trn</c> directly from disk. Reads + decodes inside try/catch
        /// (red status on failure); a loose source commits in place on Save.
        /// </summary>
        public void OpenLooseOverride(string loosePath)
        {
            if (!layoutReady) return;
            if (string.IsNullOrEmpty(loosePath) || !File.Exists(loosePath))
            {
                SetStatus("File not found: " + (loosePath ?? "(null)"), true);
                return;
            }
            try
            {
                byte[] bytes = File.ReadAllBytes(loosePath);
                TerrainDocument doc = TerrainDocument.FromBytes(bytes);
                OpenSource src = new OpenSource.LooseFile(loosePath);
                BindDocument(doc, src, Path.GetFileName(loosePath));
                lastSavedPath = loosePath; // a loose source commits in place; preview can use it after Save.
                SetStatus("Opened " + Path.GetFileName(loosePath) + " (loose override).", false);
            }
            catch (Exception ex)
            {
                SetStatus("Open failed: " + ex.Message, true);
            }
        }

        private void OnOpenClicked(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Open Terrain Override…";
                ofd.Filter = "Terrain (*.trn)|*.trn|All files (*.*)|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                OpenLooseOverride(ofd.FileName);
            }
        }

        // Binds a freshly-decoded document: records provenance, resets edit state, populates the tree +
        // palettes, refreshes the banner + buttons, and shows the no-selection field hint.
        private void BindDocument(TerrainDocument doc, OpenSource src, string name)
        {
            document = doc;
            source = src;
            displayName = name;
            hasPendingEdit = false;
            lastSavedPath = null;

            PopulateTree();
            ShowNoSelection();
            RefreshBanner();
            RefreshSaveButtonLabel();
            RefreshActionButtonState();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Tree population — TGEN root → each TerrainLayer (name + active flag) → its Nodes
        // (boundary/filter/affector) → recurse SubLayers; plus a top-level read-only "Shared palettes"
        // branch (six palette nodes, each with familyId → name children). All palette/family nodes are
        // non-editable (read-only presentation).
        // ─────────────────────────────────────────────────────────────────────

        private void PopulateTree()
        {
            treeLayers.BeginUpdate();
            try
            {
                treeLayers.Nodes.Clear();
                if (document == null) return;

                TreeNode root = new TreeNode("TGEN — procedural layers");
                root.Tag = null;
                foreach (var layer in document.Layers)
                {
                    root.Nodes.Add(BuildLayerNode(layer));
                }
                treeLayers.Nodes.Add(root);

                TreeNode palettes = BuildPalettesBranch(document.Palettes);
                treeLayers.Nodes.Add(palettes);

                root.Expand();
            }
            finally
            {
                treeLayers.EndUpdate();
            }
        }

        private TreeNode BuildLayerNode(TerrainLayer layer)
        {
            string activeMark = layer.Active ? "" : "  (inactive)";
            string label = (string.IsNullOrEmpty(layer.Name) ? "(unnamed layer)" : layer.Name) + activeMark;
            TreeNode node = new TreeNode(label) { Tag = new TreeNodeRef(layer) };

            foreach (var child in layer.Nodes)
            {
                node.Nodes.Add(BuildTerrainNode(child));
            }
            foreach (var sub in layer.SubLayers)
            {
                node.Nodes.Add(BuildLayerNode(sub));
            }
            return node;
        }

        private TreeNode BuildTerrainNode(TerrainNode tn)
        {
            string suffix = tn.IsRawPreserved ? "  (raw)" : (tn.IsDeadSkipped ? "  (obsolete)" : "");
            string label = (tn.Tag ?? "????") + (string.IsNullOrEmpty(tn.Version) ? "" : " v" + tn.Version) + suffix;
            return new TreeNode(label) { Tag = new TreeNodeRef(tn) };
        }

        private TreeNode BuildPalettesBranch(TerrainPalettes palettes)
        {
            TreeNode branch = new TreeNode("Shared palettes (read-only)") { Tag = null };
            if (palettes == null) return branch;
            foreach (var pal in palettes.InLoadOrder)
            {
                string presence = pal.Present ? (pal.Ambiguous ? " (present, ambiguous)" : " (present)") : " (absent)";
                TreeNode pnode = new TreeNode(pal.Role + " — " + PaletteReadOnlyLabel + presence)
                {
                    Tag = new TreeNodeRef(pal),
                };
                foreach (var fam in pal.Families)
                {
                    string famLabel = fam.FamilyId.ToString(CultureInfo.InvariantCulture) + " → " +
                        (string.IsNullOrEmpty(fam.Name) ? "(unnamed)" : fam.Name);
                    pnode.Nodes.Add(new TreeNode(famLabel) { Tag = new TreeNodeRef(fam) });
                }
                branch.Nodes.Add(pnode);
            }
            return branch;
        }

        // A discriminated tree-node payload so the field pane can render the right view per selection
        // (layer / terrain node / palette / family) without re-walking the model.
        private sealed class TreeNodeRef
        {
            public TerrainLayer Layer { get; }
            public TerrainNode Node { get; }
            public TerrainPalette Palette { get; }
            public TerrainPaletteFamily Family { get; }

            public TreeNodeRef(TerrainLayer layer) { Layer = layer; }
            public TreeNodeRef(TerrainNode node) { Node = node; }
            public TreeNodeRef(TerrainPalette palette) { Palette = palette; }
            public TreeNodeRef(TerrainPaletteFamily family) { Family = family; }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Field pane (Task 2 wires the typed/raw editors). Task 1 ships the empty / no-selection states +
        // a defensive read-only render so selecting any node never throws.
        // ─────────────────────────────────────────────────────────────────────

        private void OnTreeAfterSelect(object sender, TreeViewEventArgs e)
        {
            if (!layoutReady) return;
            TreeNodeRef refData = e.Node == null ? null : e.Node.Tag as TreeNodeRef;
            RenderFieldPane(refData);
        }

        // Task 1 baseline render: shows read-only context for any selection and never throws. Task 2
        // replaces the editable branches with typed rows + the active-flag toggle.
        private void RenderFieldPane(TreeNodeRef refData)
        {
            if (refData == null)
            {
                ShowNoSelection();
                return;
            }
            if (refData.Palette != null)
            {
                ShowReadOnlyMessage("Shared palette", PaletteReadOnlyLabel);
                return;
            }
            if (refData.Family != null)
            {
                ShowReadOnlyMessage("Palette family", PaletteReadOnlyLabel);
                return;
            }
            if (refData.Layer != null)
            {
                ShowReadOnlyMessage("Layer: " + refData.Layer.Name, NameReadOnlyHint);
                return;
            }
            if (refData.Node != null)
            {
                RenderNodeFields(refData.Node);
                return;
            }
            ShowNoSelection();
        }

        // Task 1 placeholder for a terrain node: a read-only list. Task 2 makes typed nodes editable.
        private void RenderNodeFields(TerrainNode node)
        {
            if (node.IsRawPreserved) { ShowReadOnlyMessage(node.Tag + " (raw-preserved)", RawFieldHint); return; }
            if (node.IsDeadSkipped) { ShowReadOnlyMessage(node.Tag + " (obsolete)", ObsoleteTagHint); return; }
            ShowReadOnlyMessage(node.Tag + " v" + node.Version, NoSelectionBody);
        }

        // ── Field-pane state renderers ──

        private void ShowEmptyState()
        {
            if (pnlFieldHost == null) return;
            RenderHostMessage(EmptyHeading, EmptyBody, Colors.Font());
        }

        private void ShowNoSelection()
        {
            RenderHostMessage(null, NoSelectionBody, Colors.FontDisabled());
        }

        private void ShowReadOnlyMessage(string heading, string body)
        {
            RenderHostMessage(heading, body, Colors.FontDisabled());
        }

        // Renders a heading + body message into the field host. Heading uses Colors.Font(); body dimmed.
        private void RenderHostMessage(string heading, string body, Color bodyColor)
        {
            if (pnlFieldHost == null) return;
            pnlFieldHost.SuspendLayout();
            try
            {
                pnlFieldHost.Controls.Clear();
                UtinniLabel lblBody = new UtinniLabel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    ForeColor = bodyColor,
                    Text = body ?? "",
                };
                pnlFieldHost.Controls.Add(lblBody); // Fill first
                if (!string.IsNullOrEmpty(heading))
                {
                    UtinniLabel lblHeading = new UtinniLabel
                    {
                        Dock = DockStyle.Top,
                        AutoSize = false,
                        Height = 18,
                        ForeColor = Colors.Font(),
                        Text = heading,
                    };
                    pnlFieldHost.Controls.Add(lblHeading);
                }
            }
            finally
            {
                pnlFieldHost.ResumeLayout(true);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Banner / status / button-state helpers.
        // ─────────────────────────────────────────────────────────────────────

        private void RefreshBanner()
        {
            if (lblBanner == null) return;
            string src = source is OpenSource.TreArchive ? "read-only TRE" :
                         source is OpenSource.LooseFile ? "loose override" : "";
            string name = string.IsNullOrEmpty(displayName) ? "<unloaded>" : displayName;
            lblBanner.Text = string.IsNullOrEmpty(src)
                ? BaseTitle + " — " + name
                : BaseTitle + " — " + name + "  (" + src + ")";
        }

        // The first commit from a read-only TRE source is "Save As Override…"; a loose source is "Save".
        private void RefreshSaveButtonLabel()
        {
            if (btnSave == null) return;
            btnSave.Text = source is OpenSource.TreArchive ? SaveAsOverrideLabel : SaveLabel;
        }

        private void RefreshActionButtonState()
        {
            if (btnSave == null || btnPreview == null) return;
            bool loaded = document != null;
            btnSave.Enabled = loaded;

            bool clientUp = false;
            try { clientUp = Game.IsRunning; } catch { clientUp = false; } // Pitfall 4 — P/Invoke can throw
            btnPreview.Enabled = loaded && clientUp;
            if (!clientUp)
            {
                toolTip.SetToolTip(btnPreview, PreviewNoHookTooltip);
            }
        }

        private void SetStatus(string text, bool isError)
        {
            if (lblStatus == null) return;
            lblStatus.Text = text ?? "";
            lblStatus.ForeColor = isError ? Color.Red : Colors.Font();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Save / Preview — wired in Task 2. Task 1 ships the guards so the buttons are inert-but-honest
        // until the edit/save/preview path lands.
        // ─────────────────────────────────────────────────────────────────────

        private void OnSaveClicked(object sender, EventArgs e)
        {
            if (document == null) { SetStatus("No terrain loaded.", true); return; }
            SetStatus("Edit a field, then Save.", false);
        }

        private void OnPreviewClicked(object sender, EventArgs e)
        {
            if (document == null) { SetStatus("No terrain loaded.", true); return; }
            if (!hasPendingEdit) { SetStatus(NothingToPreviewGuard, true); return; }
            if (string.IsNullOrEmpty(lastSavedPath)) { SetStatus(SaveFirstPreviewGuard, true); return; }
        }

        // Reflects a reload tier on the status footer via the Plan 01 candor helper (the ONLY copy source —
        // never an inline tier string). Task 2 calls this after a Dispatch.
        private void ApplyReloadCandor(ReloadTier tier)
        {
            TerrainReloadCandor.StatusCopyResult copy = TerrainReloadCandor.StatusCopy(tier);
            SetStatus(copy.Text, copy.IsError);
        }
    }
}
