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
using System.Threading.Tasks;
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

        // ── Pending-edit descriptor (the ONE fixed-length field/active-flag edit awaiting Save/Preview).
        //    Carries everything SaveLooseOverride/ApplyFieldEdit need: the resolved DATA leaf id, the tag,
        //    version, field name, and the new value string. Null when no edit is staged. ──
        private sealed class PendingEdit
        {
            public string StableLeafId;
            public string Tag;
            public string Version;
            public string FieldName;
            public string Value;
            public TreeNode DirtyTreeNode;   // the tree node to tint + ● glyph
            public string DirtyBaseLabel;    // its label without the ● glyph (for re-render)
        }
        private PendingEdit pendingEdit;

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

        // Renders the field pane for the selected tree node. NEVER throws — degrade-not-fail throughout:
        // typed (IsEditable) nodes get editable rows; raw-preserved/dead/palette/family/name render a
        // read-only generic list with the matching verbatim hint copy (21-UI-SPEC).
        private void RenderFieldPane(TreeNodeRef refData)
        {
            try
            {
                if (refData == null) { ShowNoSelection(); return; }
                if (refData.Palette != null)
                {
                    ShowReadOnlyMessage(refData.Palette.Role + " palette", PaletteReadOnlyLabel);
                    return;
                }
                if (refData.Family != null)
                {
                    ShowReadOnlyMessage("Palette family", PaletteReadOnlyLabel);
                    return;
                }
                if (refData.Layer != null)
                {
                    RenderLayerPane(refData.Layer, FindTreeNodeFor(refData));
                    return;
                }
                if (refData.Node != null)
                {
                    RenderNodeFields(refData.Node, FindTreeNodeFor(refData));
                    return;
                }
                ShowNoSelection();
            }
            catch (Exception ex)
            {
                // Selecting any node MUST NOT throw — surface a read-only error rather than tearing down.
                ShowReadOnlyMessage("Field render failed", ex.Message);
            }
        }

        // A LAYR selection: read-only name (deferred D-06) + the editable active-flag toggle. The toggle
        // resolves the IHDR DATA leaf id via the Plan 01 ResolveIhdrLeafStableId bridge (RESEARCH OQ2) —
        // never a hand-rolled tree walk.
        private void RenderLayerPane(TerrainLayer layer, TreeNode treeNode)
        {
            pnlFieldHost.SuspendLayout();
            try
            {
                pnlFieldHost.Controls.Clear();

                var rows = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoScroll = true,
                    BackColor = Colors.Primary(),
                };

                // Read-only name row (Phase 20 D-06 — names are not editable this phase).
                rows.Controls.Add(MakeReadOnlyRow("name", string.IsNullOrEmpty(layer.Name) ? "(unnamed)" : layer.Name));
                rows.Controls.Add(MakeHintLabel(NameReadOnlyHint));

                // Active-flag toggle (the int32 at offset 0 of the IHDR DATA leaf — same leaf --field active
                // mutates). Detach/reattach the CheckedChanged handler around the programmatic set so it does
                // not re-fire (SnapshotPanel idiom).
                var chkActive = new CheckBox
                {
                    Text = "active",
                    ForeColor = Colors.Font(),
                    BackColor = Colors.Primary(),
                    AutoSize = true,
                    Margin = new Padding(0, 6, 0, 0),
                };
                chkActive.CheckedChanged -= OnActiveFlagChanged;
                chkActive.Checked = layer.Active;
                chkActive.CheckedChanged += OnActiveFlagChanged;
                chkActive.Tag = new ActiveToggleContext { Layer = layer, TreeNode = treeNode };
                rows.Controls.Add(chkActive);

                pnlFieldHost.Controls.Add(rows);  // Fill first
                pnlFieldHost.Controls.Add(MakeHeadingLabel("Layer: " + (string.IsNullOrEmpty(layer.Name) ? "(unnamed)" : layer.Name)));
            }
            finally
            {
                pnlFieldHost.ResumeLayout(true);
            }
        }

        private sealed class ActiveToggleContext
        {
            public TerrainLayer Layer;
            public TreeNode TreeNode;
        }

        private void OnActiveFlagChanged(object sender, EventArgs e)
        {
            var chk = sender as CheckBox;
            if (chk == null || document == null) return;
            var ctx = chk.Tag as ActiveToggleContext;
            if (ctx == null || ctx.Layer == null) return;
            try
            {
                // RESEARCH OQ2: the layer exposes only its LAYR FORM StableIdPath — resolve the IHDR DATA
                // leaf id via the Plan 01 bridge. NEVER walk the tree for the IHDR leaf by hand.
                string ihdrLeafId = TerrainSaveTargets.ResolveIhdrLeafStableId(document.Mutable, ctx.Layer.StableIdPath);
                StageEdit(new PendingEdit
                {
                    StableLeafId = ihdrLeafId,
                    Tag = TgenFieldLayouts.LayerHeaderTag,       // "IHDR"
                    Version = TgenFieldLayouts.LayerHeaderVersion, // synthetic "active" version
                    FieldName = TgenFieldLayouts.ActiveFieldName,  // "active"
                    Value = chk.Checked ? "1" : "0",
                    DirtyTreeNode = ctx.TreeNode,
                });
                SetStatus("Active flag staged — Save or Preview to apply.", false);
            }
            catch (Exception ex)
            {
                SetStatus("Active-flag edit failed: " + ex.Message, true);
            }
        }

        // A terrain node selection. Typed (IsEditable) → editable rows matching DisplayType; raw-preserved →
        // read-only generic list + RawFieldHint; dead-skipped → ObsoleteTagHint. NEVER throws on un-typed.
        private void RenderNodeFields(TerrainNode node, TreeNode treeNode)
        {
            pnlFieldHost.SuspendLayout();
            try
            {
                pnlFieldHost.Controls.Clear();

                if (node.IsRawPreserved)
                {
                    RenderHostMessage(node.Tag + " (raw-preserved)", RawFieldHint, Colors.FontDisabled());
                    return;
                }
                if (node.IsDeadSkipped)
                {
                    RenderHostMessage(node.Tag + " (obsolete)", ObsoleteTagHint, Colors.FontDisabled());
                    return;
                }

                var rows = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoScroll = true,
                    BackColor = Colors.Primary(),
                };

                foreach (var field in node.TypedFields)
                {
                    rows.Controls.Add(MakeFieldRow(node, field, treeNode));
                }

                pnlFieldHost.Controls.Add(rows);  // Fill first
                pnlFieldHost.Controls.Add(MakeHeadingLabel(node.Tag + " v" + node.Version));
            }
            finally
            {
                pnlFieldHost.ResumeLayout(true);
            }
        }

        // Builds one editable field row. ScalarFloat/Int32 → text box (invariant-culture parse on commit);
        // Enum32/FamilyIdRef → text box of the decoded int value; ActiveFlag → checkbox. Non-editable fields
        // render read-only.
        private Control MakeFieldRow(TerrainNode node, TerrainField field, TreeNode treeNode)
        {
            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                BackColor = Colors.Primary(),
                Margin = new Padding(0, 2, 0, 2),
            };
            row.Controls.Add(new UtinniLabel
            {
                Text = field.Name,
                ForeColor = Colors.Font(),
                AutoSize = false,
                Width = 130,
                TextAlign = ContentAlignment.MiddleLeft,
            });

            if (!field.Editable)
            {
                row.Controls.Add(new UtinniLabel
                {
                    Text = field.Value ?? "",
                    ForeColor = Colors.FontDisabled(),
                    AutoSize = false,
                    Width = 160,
                    TextAlign = ContentAlignment.MiddleLeft,
                });
                return row;
            }

            if (field.DisplayType == TgenDisplayType.ActiveFlag)
            {
                var chk = new CheckBox
                {
                    ForeColor = Colors.Font(),
                    BackColor = Colors.Primary(),
                    AutoSize = true,
                };
                bool on;
                bool.TryParse(field.Value, out on);
                if (!on && field.Value != null && field.Value.Trim() != "0") on = field.Value.Trim() == "1";
                chk.CheckedChanged -= OnTypedFieldCheckChanged;
                chk.Checked = on;
                chk.CheckedChanged += OnTypedFieldCheckChanged;
                chk.Tag = new TypedFieldContext { Node = node, Field = field, TreeNode = treeNode };
                row.Controls.Add(chk);
                return row;
            }

            var txt = new TextBox
            {
                Text = field.Value ?? "",
                BackColor = Colors.PrimaryHighlight(),
                ForeColor = Colors.Font(),
                BorderStyle = BorderStyle.FixedSingle,
                Width = 160,
            };
            txt.Tag = new TypedFieldContext { Node = node, Field = field, TreeNode = treeNode };
            txt.Leave += OnTypedFieldTextCommit;
            row.Controls.Add(txt);
            return row;
        }

        private sealed class TypedFieldContext
        {
            public TerrainNode Node;
            public TerrainField Field;
            public TreeNode TreeNode;
        }

        private void OnTypedFieldTextCommit(object sender, EventArgs e)
        {
            var txt = sender as TextBox;
            if (txt == null) return;
            var ctx = txt.Tag as TypedFieldContext;
            if (ctx == null) return;
            StageTypedFieldEdit(ctx.Node, ctx.Field, ctx.TreeNode, txt.Text);
        }

        private void OnTypedFieldCheckChanged(object sender, EventArgs e)
        {
            var chk = sender as CheckBox;
            if (chk == null) return;
            var ctx = chk.Tag as TypedFieldContext;
            if (ctx == null) return;
            StageTypedFieldEdit(ctx.Node, ctx.Field, ctx.TreeNode, chk.Checked ? "1" : "0");
        }

        // Stages a typed-field edit: resolves the node's DATA leaf id via the Plan 01 bridge (the model
        // exposes only the node's FORM StableIdPath), then records the pending edit. The value is validated
        // at Save/Preview time by the single-source TrnFieldEncoder (a bad value surfaces as red status,
        // never a throw — Pitfall 5).
        private void StageTypedFieldEdit(TerrainNode node, TerrainField field, TreeNode treeNode, string value)
        {
            if (document == null || node == null || field == null) return;
            try
            {
                string leafId = TerrainSaveTargets.ResolveTypedDataLeafStableId(document.Mutable, node.StableIdPath);
                StageEdit(new PendingEdit
                {
                    StableLeafId = leafId,
                    Tag = node.Tag,
                    Version = node.Version,
                    FieldName = field.Name,
                    Value = value,
                    DirtyTreeNode = treeNode,
                });
                SetStatus("Edit staged — Save or Preview to apply.", false);
            }
            catch (Exception ex)
            {
                SetStatus("Edit failed: " + ex.Message, true);
            }
        }

        // Records the pending edit, marks the leaf dirty (● glyph + Colors.Secondary() tint on its tree
        // node), and enables Save. Only ONE pending edit is staged at a time (D-05 fixed-length, one field).
        private void StageEdit(PendingEdit edit)
        {
            // Clear a prior dirty mark if it was on a different node.
            if (pendingEdit != null && pendingEdit.DirtyTreeNode != null
                && pendingEdit.DirtyTreeNode != edit.DirtyTreeNode)
            {
                ClearDirtyGlyph(pendingEdit.DirtyTreeNode, pendingEdit.DirtyBaseLabel);
            }

            if (edit.DirtyTreeNode != null)
            {
                edit.DirtyBaseLabel = StripDirtyGlyph(edit.DirtyTreeNode.Text);
                edit.DirtyTreeNode.Text = "● " + edit.DirtyBaseLabel; // ● dirty glyph
                edit.DirtyTreeNode.ForeColor = Colors.Secondary();          // accent tint (reserved use #2)
            }

            pendingEdit = edit;
            hasPendingEdit = true;
            RefreshActionButtonState();
        }

        private const string DirtyGlyph = "● ";

        private static string StripDirtyGlyph(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;
            return label.StartsWith(DirtyGlyph, StringComparison.Ordinal) ? label.Substring(DirtyGlyph.Length) : label;
        }

        private void ClearDirtyGlyph(TreeNode node, string baseLabel)
        {
            if (node == null) return;
            node.Text = baseLabel ?? StripDirtyGlyph(node.Text);
            node.ForeColor = Colors.Font();
        }

        // Finds the currently-selected TreeNode (for dirty-glyph + active-toggle wiring).
        private TreeNode FindTreeNodeFor(TreeNodeRef refData)
        {
            return treeLayers == null ? null : treeLayers.SelectedNode;
        }

        // ── Small row/label builders ──

        private Control MakeReadOnlyRow(string name, string value)
        {
            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                BackColor = Colors.Primary(),
                Margin = new Padding(0, 2, 0, 2),
            };
            row.Controls.Add(new UtinniLabel { Text = name, ForeColor = Colors.Font(), AutoSize = false, Width = 130 });
            row.Controls.Add(new UtinniLabel { Text = value ?? "", ForeColor = Colors.FontDisabled(), AutoSize = false, Width = 160 });
            return row;
        }

        private UtinniLabel MakeHeadingLabel(string text)
        {
            return new UtinniLabel
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 18,
                ForeColor = Colors.Font(),
                Text = text,
            };
        }

        private UtinniLabel MakeHintLabel(string text)
        {
            return new UtinniLabel
            {
                AutoSize = false,
                Width = 300,
                Height = 18,
                ForeColor = Colors.FontDisabled(),
                Text = text,
                Margin = new Padding(0, 0, 0, 4),
            };
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
        // Save / Save-As-Override / Preview. Save routes through the Plan 01 in-proc
        // TerrainSaveTargets.SaveLooseOverride (byte-parity-proven vs apply-save-trn). Preview applies the
        // edit in-memory to a temp loose override inside containment, then Dispatches (Discretion Option A).
        // Both fire EXACTLY ONE AddMainLoopCall via ClientReloadDispatcher.Dispatch (D-06); no direct
        // bindings, no per-frame work, no inline tier copy.
        // ─────────────────────────────────────────────────────────────────────

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            if (document == null) { SetStatus("No terrain loaded.", true); return; }
            if (pendingEdit == null) { SetStatus("Nothing to save — edit a field first.", true); return; }

            string resolvedRoot = ResolveClientRoot();
            if (string.IsNullOrEmpty(resolvedRoot))
            {
                SetStatus("Save failed: could not resolve the client root for the loose override.", true);
                return;
            }

            // Save-As-Override over an existing override → confirm (the only overwrite case).
            if (source is OpenSource.TreArchive)
            {
                string predicted = PredictOverridePath(resolvedRoot);
                if (!string.IsNullOrEmpty(predicted) && File.Exists(predicted) && !ConfirmOverwriteOverride(predicted))
                {
                    SetStatus("Save cancelled.", false);
                    return;
                }
            }

            btnSave.Enabled = false;
            try
            {
                TerrainSaveTargets.SaveResult result = await TerrainSaveTargets.SaveLooseOverride(
                    document, source, resolvedRoot,
                    pendingEdit.StableLeafId, pendingEdit.Tag, pendingEdit.Version,
                    pendingEdit.FieldName, pendingEdit.Value);

                if (result == null || !result.Ok)
                {
                    string reason = result == null ? "unknown error" : result.Message;
                    SetStatus("Save failed: " + reason, true);
                    return;
                }

                lastSavedPath = result.Path;
                hasPendingEdit = false;
                // The save committed the in-memory edit — a fresh document from the saved bytes now matches
                // the source, so the loose source becomes the new in-place target.
                source = new OpenSource.LooseFile(result.Path);
                RefreshSaveButtonLabel();
                ClearDirtyOnSavedNode();
                SetStatus("Saved → " + result.Path, false);

                // Route the save through the existing reload tier (D-04 auto-on-save) — honest candor.
                ReloadTier tier = ClientReloadDispatcher.Dispatch(result.Path, null); // null rootTypeId for .trn
                ApplyReloadCandor(tier);

                // D-09 undo seam — null-safe (NULL until FormMain wires it).
                if (editorPlugin != null) editorPlugin.ClearUndoStack?.Invoke();
            }
            catch (Exception ex)
            {
                SetStatus("Save failed: " + ex.Message, true);
            }
            finally
            {
                RefreshActionButtonState();
            }
        }

        private void OnPreviewClicked(object sender, EventArgs e)
        {
            if (document == null) { SetStatus("No terrain loaded.", true); return; }
            if (pendingEdit == null && !hasPendingEdit) { SetStatus(NothingToPreviewGuard, true); return; }

            string resolvedRoot = ResolveClientRoot();
            if (string.IsNullOrEmpty(resolvedRoot))
            {
                SetStatus("Preview failed: could not resolve the client root.", true);
                return;
            }

            string previewPath = lastSavedPath;
            // Manual Preview (Discretion Option A): if the edit isn't on disk yet, apply it in-memory and
            // write a temp loose override INSIDE the containment root, then Dispatch — no commit to the real
            // override. If the edit was already saved, Preview just re-dispatches the last-saved path.
            if (hasPendingEdit && pendingEdit != null)
            {
                try
                {
                    TerrainSaveTargets.SaveResult applied = TerrainSaveTargets.ApplyFieldEdit(
                        document, pendingEdit.StableLeafId, pendingEdit.Tag, pendingEdit.Version,
                        pendingEdit.FieldName, pendingEdit.Value);
                    if (applied != null && !applied.Ok)
                    {
                        SetStatus("Preview failed: " + applied.Message, true);
                        return;
                    }
                    byte[] bytes = document.Serialize();
                    previewPath = WritePreviewTemp(resolvedRoot, bytes);
                }
                catch (Exception ex)
                {
                    SetStatus("Preview failed: " + ex.Message, true);
                    return;
                }
            }

            if (string.IsNullOrEmpty(previewPath))
            {
                SetStatus(SaveFirstPreviewGuard, true);
                return;
            }

            // EXACTLY ONE AddMainLoopCall per Preview, through the existing dispatcher (D-05/D-06).
            ReloadTier tier = ClientReloadDispatcher.Dispatch(previewPath, null);
            ApplyReloadCandor(tier);
        }

        // Writes the pending-edit bytes to a temp loose override (".trn.preview") INSIDE the containment
        // root so Preview never commits to the real override path. Keeps the extension .trn-classifiable.
        private string WritePreviewTemp(string resolvedRoot, byte[] bytes)
        {
            string dir = Path.Combine(resolvedRoot, "loose_preview");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "terrain_preview.trn");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            return path;
        }

        // Reflects a reload tier on the status footer via the Plan 01 candor helper (the ONLY copy source —
        // never an inline tier string; D-07 honest default holds inside the helper).
        private void ApplyReloadCandor(ReloadTier tier)
        {
            TerrainReloadCandor.StatusCopyResult copy = TerrainReloadCandor.StatusCopy(tier);
            SetStatus(copy.Text, copy.IsError);
        }

        private void ClearDirtyOnSavedNode()
        {
            if (pendingEdit != null && pendingEdit.DirtyTreeNode != null)
            {
                ClearDirtyGlyph(pendingEdit.DirtyTreeNode, pendingEdit.DirtyBaseLabel);
            }
        }

        // ── Override-exists confirm (the only overwrite case — 21-UI-SPEC Destructive table). ──

        private bool ConfirmOverwriteOverride(string path)
        {
            DialogResult dr = MessageBox.Show(
                this,
                "A loose override already exists at " + path +
                ". Overwrite it? The original TRE asset is never modified.",
                "Override exists",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            return dr == DialogResult.OK;
        }

        // Predicts the loose-override destination for the current TRE source so the overwrite confirm can
        // check File.Exists. Mirrors the SaveLooseOverride relative-path derivation (TRE → LogicalPath).
        private string PredictOverridePath(string resolvedRoot)
        {
            var tre = source as OpenSource.TreArchive;
            if (tre == null || string.IsNullOrEmpty(tre.LogicalPath)) return null;
            try
            {
                return LooseOverridePath.Resolve(resolvedRoot, tre.LogicalPath);
            }
            catch
            {
                return null; // containment rejection is surfaced by SaveLooseOverride itself.
            }
        }

        // Resolves the client install root (process module → GetWorkingDirectory → ini fallback) — the same
        // order FormIffEditor/FormTreBrowser use. Returns null when none resolves.
        private string ResolveClientRoot()
        {
            try
            {
                string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                string moduleDir = Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(moduleDir) && Directory.Exists(moduleDir)) return moduleDir;
            }
            catch { /* fall through */ }
            try
            {
                string wd = UtinniCore.Utility.utility.GetWorkingDirectory();
                if (!string.IsNullOrEmpty(wd) && Directory.Exists(wd)) return wd;
            }
            catch { /* binding unavailable outside a live client */ }
            try
            {
                if (editorPlugin != null)
                {
                    var ini = editorPlugin.GetConfig();
                    if (ini != null)
                    {
                        string configured = ini.GetString("TreBrowser", "clientDir");
                        if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured)) return configured;
                    }
                }
            }
            catch { /* ini may not have the key yet */ }
            return null;
        }
    }
}
