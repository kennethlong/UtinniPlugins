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
// The D-01 docked entry point for the terrain editor (21-03). It is intentionally THIN — the SubPanel base
// is hard-pinned to 417px (UtinniCoreDotNet/UI/Controls/SubPanel.cs:36,63, enforced in OnResize), far too
// narrow for a tree+field split — so the heavy editing surface lives in the roomy FormTerrainEditor host
// (21-02, D-02 escape hatch) which this panel LAUNCHES as a singleton (hide-not-dispose, the SnapshotPanel
// companion-window precedent). The panel itself surfaces: a banner + accent rule, an "Open Terrain
// Override…" file-picker for a direct loose-override open, a hint that TRE-sourced opens come from the TRE
// Browser, and the dimmed DEC-A3 candor footer. It adds ZERO format/reload logic (the host owns all of it).
//
// MEF-safety (D-09 / Pitfall 8): the WHOLE control build runs inside the ctor try/catch — a throwing
// SubPanel ctor in the Plugin.cs SubPanelContainer array silently cascades the entire IEditorPlugin out of
// MEF compose with no error, so a partial build surfaces a state label rather than throwing. The undo seam
// (IEditorPlugin.Undo/Redo/ClearUndoStack) is NULL until FormMain wires it — every call site null-checks
// via ?.Invoke(). Implementation original to Utinni under MIT.

using System;
using System.Drawing;
using System.Windows.Forms;
using TJT.UI.Forms;                            // FormTerrainEditor (the roomy host this panel launches)
using UtinniCore.Utinni;                       // Game.IsRunning (P/Invoke — ALWAYS try/catch), UtINI
using UtinniCoreDotNet.Hotkeys;                // HotkeyManager (ctor parity with the sibling SubPanels)
using UtinniCoreDotNet.PluginFramework;        // IEditorPlugin (the undo seam — null-checked)
using UtinniCoreDotNet.UI.Controls;            // SubPanel, UtinniLabel, UtinniButton
using UtinniCoreDotNet.UI.Theme;               // Colors

namespace TJT.UI.SubPanels
{
    /// <summary>
    /// The thin docked Terrain entry SubPanel (21-CONTEXT D-01). Launches the roomy
    /// <see cref="FormTerrainEditor"/> host (singleton, hide-not-dispose) for the actual tree+field editing
    /// and offers the direct loose-override open; the TRE Browser's "Open in Terrain Editor" hand-off
    /// (21-03 Task 2) reaches the host through this panel's <see cref="OpenFromTre"/> entry. Implements
    /// <see cref="ISceneAvailability"/> so the host's live-preview availability tracks
    /// <c>Game.IsRunning</c> (SnapshotPanel precedent).
    /// </summary>
    public partial class TerrainSubPanel : SubPanel, ISceneAvailability
    {
        // ── LOCKED copy (21-UI-SPEC Copywriting Contract — verbatim; constants so a future edit can't
        //    silently soften the wording and so the grep gates can verify them). ──
        private const string OpenOverrideLabel = "Open Terrain Override…";
        private const string OpenEditorLabel = "Open Terrain Editor";
        private const string TreHint =
            "To edit a planet's .trn from a TRE archive, right-click it in the TRE Browser → Open in Terrain Editor.";
        private const string DecA3Footer =
            "Preview is live in the real SWG client — Utinni never renders terrain itself.";
        private const string BannerText = "Terrain";

        // The plugin ref — held for the D-09 undo seam (Undo/Redo/ClearUndoStack are NULL until FormMain
        // wires them; every call site null-checks via ?.Invoke()).
        private readonly IEditorPlugin editorPlugin;
        private readonly ToolTip toolTip = new ToolTip();

        // The launched roomy host. Singleton per editor session — hide-not-dispose; a re-launch re-Shows the
        // SAME instance (SnapshotPanel companion-window idiom). Null until first launch / after a Dispose.
        private FormTerrainEditor terrainForm;

        // Controls (built imperatively in BuildContent so the Pitfall-8 Dock order is explicit).
        private UtinniButton btnOpenEditor;
        private UtinniButton btnOpenOverride;
        private UtinniLabel lblBanner;
        private UtinniLabel lblHint;
        private UtinniLabel lblFooter;

        // True once BuildContent completed without throwing. A failed build surfaces a state label and the
        // open/launch entry points no-op defensively (so a partial-load panel never throws).
        private bool contentReady;

        // Idempotent scene-availability transition latch (SnapshotPanel:211 precedent).
        private bool previousIsSceneActive;
        private bool hasSceneState;

        /// <summary>
        /// Constructs the thin docked Terrain entry SubPanel. The ctor signature mirrors the sibling
        /// SubPanels (<c>IEditorPlugin, HotkeyManager, UtINI</c>) so it slots into the existing
        /// <c>SubPanelContainer("Controls", …)</c> array in Plugin.cs. The WHOLE build is wrapped in
        /// try/catch (D-09 MEF-safety): a throwing ctor would cascade the entire IEditorPlugin out of MEF
        /// compose, so a partial build surfaces a read-only state label instead of throwing.
        /// </summary>
        public TerrainSubPanel(IEditorPlugin editorPlugin, HotkeyManager hotkeyManager, UtINI ini)
            : base("Terrain")
        {
            // The base SubPanel ctor sets the collapsible-header text + pins width to 417px. InitializeComponent
            // is a no-op container hook (the layout is built imperatively below).
            InitializeComponent();

            this.editorPlugin = editorPlugin;

            try
            {
                BuildContent();
                contentReady = true;
            }
            catch (Exception ex)
            {
                // A partial build must NEVER throw out of the ctor (the MEF-killer). Surface a minimal state
                // label so the user sees the failure and the whole TJT plugin stays composed.
                SurfaceBuildFailure(ex);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Layout — Pitfall 8 LOCKED idiom: the Bottom/Top docked strips claim their edges; everything fits
        // the 417px column. Theme via Colors.* only (no raw ARGB literals; Color.Red reserved for errors).
        // ─────────────────────────────────────────────────────────────────────

        private void BuildContent()
        {
            SuspendLayout();

            BackColor = Colors.Primary();
            ForeColor = Colors.Font();
            Font = new Font("Segoe UI", 8.25f); // base 8.25pt; Bold reserved to the banner.

            // ── Dock.Bottom dimmed DEC-A3 candor footer (added FIRST so it claims the bottom edge). ──
            lblFooter = new UtinniLabel
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 4, 0),
                ForeColor = Colors.FontDisabled(), // dimmed DEC-A3 candor (verbatim)
                Text = DecA3Footer,
            };

            // ── Dock.Top banner + 2px accent rule. ──
            Panel pnlBannerAccent = new Panel
            {
                Dock = DockStyle.Top,
                Height = 2,
                BackColor = Colors.Secondary(), // accent reserved use #1 (UI-SPEC Color)
            };
            lblBanner = new UtinniLabel
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 4, 0),
                ForeColor = Colors.Font(),
                Font = new Font(Font, FontStyle.Bold), // the ONE authorized Bold use (the banner)
                Text = BannerText,
            };

            // ── Action row: launch the editor + open a loose override (fits 417px). ──
            btnOpenEditor = new UtinniButton
            {
                Text = OpenEditorLabel,
                Location = new Point(4, 30),
                Size = new Size(140, 22),
                DrawOutline = false,
                UseDisableColor = true,
                UseVisualStyleBackColor = true,
            };
            btnOpenEditor.Click += OnOpenEditorClicked;
            toolTip.SetToolTip(btnOpenEditor, "Open the roomy Terrain editor host.");

            btnOpenOverride = new UtinniButton
            {
                Text = OpenOverrideLabel,
                Location = new Point(150, 30),
                Size = new Size(160, 22),
                DrawOutline = false,
                UseDisableColor = true,
                UseVisualStyleBackColor = true,
            };
            btnOpenOverride.Click += OnOpenOverrideClicked;
            toolTip.SetToolTip(btnOpenOverride, "Open an existing loose-override .trn directly for editing.");

            // ── Hint: TRE-sourced opens come from the TRE Browser. ──
            lblHint = new UtinniLabel
            {
                Location = new Point(4, 58),
                AutoSize = false,
                Width = 408,
                Height = 30,
                ForeColor = Colors.FontDisabled(),
                Text = TreHint,
            };

            // ── Assemble. The Dock.Bottom/Top strips are added first so they claim their edges; the
            //    free-positioned action row + hint sit in the remaining body. ──
            Controls.Add(lblFooter);          // Dock.Bottom — claim the bottom edge first
            Controls.Add(lblBanner);          // Dock.Top
            Controls.Add(pnlBannerAccent);    // Dock.Top (accent rule sits directly under the banner)
            Controls.Add(btnOpenEditor);
            Controls.Add(btnOpenOverride);
            Controls.Add(lblHint);

            ResumeLayout(false);
            PerformLayout();
        }

        // Minimal failure surface: a single read-only label so a partial build never throws the ctor (D-09).
        private void SurfaceBuildFailure(Exception ex)
        {
            try
            {
                Controls.Clear();
                BackColor = Colors.Primary();
                var lbl = new UtinniLabel
                {
                    Dock = DockStyle.Fill,
                    ForeColor = Color.Red,
                    Padding = new Padding(8),
                    Text = "Terrain panel failed to initialize: " + (ex == null ? "(unknown)" : ex.Message),
                };
                Controls.Add(lbl);
            }
            catch
            {
                // Last-ditch: even the failure surface must not re-throw out of the ctor.
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Singleton host launch (hide-not-dispose) — the SnapshotPanel companion-window pattern. A re-launch
        // re-Shows / Activates the SAME instance. Returns the live host (or null if it can't be created).
        // ─────────────────────────────────────────────────────────────────────

        private FormTerrainEditor LaunchHost()
        {
            if (!contentReady) return null;
            if (terrainForm == null || terrainForm.IsDisposed)
            {
                terrainForm = new FormTerrainEditor(editorPlugin);
            }
            if (terrainForm.Visible)
            {
                terrainForm.Activate();
            }
            else
            {
                terrainForm.Show();
            }
            return terrainForm;
        }

        private void OnOpenEditorClicked(object sender, EventArgs e)
        {
            LaunchHost();
        }

        // Direct loose-override open (D-08): launch the singleton host and trigger ITS loose-override file
        // picker (FormTerrainEditor.PromptOpenLooseOverride → OpenFileDialog). The picker lives in the host
        // (one source of the .trn dialog), so the thin docked panel never runs a modal dialog of its own —
        // it is a pure launcher (the host reads + decodes inside its own try/catch, red status on failure).
        private void OnOpenOverrideClicked(object sender, EventArgs e)
        {
            try
            {
                FormTerrainEditor host = LaunchHost();
                if (host == null) return;
                host.PromptOpenLooseOverride();
            }
            catch
            {
                // A launch failure must never tear down the docked panel.
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Public open entries the TRE Browser hand-off (21-03 Task 2) calls. The hand-off finds THIS panel
        // instance via the plugin's GetStandalonePanels() "Controls" container and forwards a read-only TRE
        // payload (D-08 — first commit is "Save As Override…", host-enforced; the TRE is never edited in
        // place). Both entries launch the singleton host and forward to its open path; they never throw.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Opens a <c>.trn</c> handed off read-only from the TRE Browser. Launches the singleton host and
        /// forwards to <see cref="FormTerrainEditor.OpenFromTreEntry"/> (which decodes inside its own
        /// try/catch — red status on failure; the first commit writes a loose override, D-08). Never throws.
        /// </summary>
        public void OpenFromTre(byte[] payload, string archivePath, string logicalPath)
        {
            try
            {
                FormTerrainEditor host = LaunchHost();
                if (host == null) return;
                host.OpenFromTreEntry(payload, archivePath, logicalPath);
            }
            catch
            {
                // The hand-off must never tear down the panel or the TRE Browser.
            }
        }

        /// <summary>
        /// Opens an existing loose-override <c>.trn</c> directly (the same path the panel's own
        /// "Open Terrain Override…" button uses) — exposed so a future caller can route a known loose path
        /// through the singleton host. Never throws.
        /// </summary>
        public void OpenLooseOverride(string loosePath)
        {
            try
            {
                FormTerrainEditor host = LaunchHost();
                if (host == null) return;
                host.OpenLooseOverride(loosePath);
            }
            catch
            {
                // Defensive — a bad path is surfaced by the host's own red status, never a throw here.
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Scene availability (ISceneAvailability). Idempotent on transition (SnapshotPanel idiom). The panel
        // itself has no live-only control, but the host's Preview button gates on Game.IsRunning internally
        // (FormTerrainEditor.RefreshActionButtonState) — this hook keeps the panel honest if a future
        // live-only affordance is added, and lets the compose host drive the panel like its siblings.
        // ─────────────────────────────────────────────────────────────────────

        public void UpdateSceneAvailability(bool isSceneActive)
        {
            if (hasSceneState && previousIsSceneActive == isSceneActive)
            {
                return; // idempotent — only on transition.
            }
            previousIsSceneActive = isSceneActive;
            hasSceneState = true;

            // D-09 undo seam is null until FormMain wires it — any future use MUST null-check (e.g.
            // editorPlugin?.ClearUndoStack?.Invoke()). No undo-seam call is needed here today.
        }
    }
}
