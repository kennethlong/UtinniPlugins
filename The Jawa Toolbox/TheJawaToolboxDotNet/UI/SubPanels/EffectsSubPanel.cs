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
// The D-04 docked entry point for the ClientEffect editor (22-04). It is intentionally THIN — the SubPanel
// base is hard-pinned to 417px (UtinniCoreDotNet/UI/Controls/SubPanel.cs), far too narrow for a command
// list + field grid split — so the heavy editing surface lives in the roomy FormClientEffectEditor host,
// which this panel LAUNCHES as a singleton (hide-not-dispose, the TerrainSubPanel/SnapshotPanel precedent).
// The panel itself surfaces: a banner + accent rule, an "Open Effects Editor" launch button, an
// "Open ClientEffect Override…" file-picker for a direct loose-override open, a hint that TRE-sourced opens
// come from the TRE Browser, and the dimmed DEC-A3 candor footer. It adds ZERO format/save/reload logic
// (the host owns all of it).
//
// MEF-safety (REVIEWS Codex divergent / Pitfall 5/8): the ctor is MINIMAL and NO-THROW — it assigns fields
// + InitializeComponent() then calls a guarded BuildContentSafe() that wraps the real BuildContent() in
// try/catch and surfaces a read-only state label on failure. A throwing SubPanel ctor in the Plugin.cs
// SubPanelContainer array would silently cascade the ENTIRE IEditorPlugin out of MEF compose with no error;
// a half-built control reachable past a whole-ctor try/catch is itself a hazard (Codex divergent), so the
// guarded build sets an explicit failure state instead. The undo seam (IEditorPlugin.Undo/Redo/
// ClearUndoStack) is NULL until FormMain wires it — every call site null-checks via ?.Invoke().
// Implementation original to Utinni under MIT.

using System;
using System.Drawing;
using System.Windows.Forms;
using TJT.UI.Forms;                            // FormClientEffectEditor (the roomy host this panel launches)
using UtinniCore.Utinni;                       // UtINI
using UtinniCoreDotNet.Hotkeys;                // HotkeyManager (ctor parity with the sibling SubPanels)
using UtinniCoreDotNet.PluginFramework;        // IEditorPlugin (the undo seam — null-checked)
using UtinniCoreDotNet.UI.Controls;            // SubPanel, UtinniLabel, UtinniButton
using UtinniCoreDotNet.UI.Theme;               // Colors

namespace TJT.UI.SubPanels
{
    /// <summary>
    /// The thin docked ClientEffect entry SubPanel (22-CONTEXT D-04). Launches the roomy
    /// <see cref="FormClientEffectEditor"/> host (singleton, hide-not-dispose) for the actual command-list +
    /// field editing and offers the direct loose-override open; the TRE Browser's "Open in Effects Editor"
    /// hand-off (D-09) reaches the host through this panel's <see cref="OpenFromTre"/> entry.
    /// </summary>
    public partial class EffectsSubPanel : SubPanel
    {
        // ── LOCKED copy (22-UI-SPEC Copywriting Contract — verbatim; constants so a future edit can't
        //    silently soften the wording and so the grep gates can verify them). ──
        private const string OpenOverrideLabel = "Open ClientEffect Override…";
        private const string OpenEditorLabel = "Open Effects Editor";
        private const string TreHint =
            "To edit a ClientEffect .iff from a TRE archive, right-click it in the TRE Browser → Open in Effects Editor.";
        private const string DecA3Footer =
            "Preview is live in the real SWG client — Utinni never renders effects itself.";
        private const string BannerText = "Effects";

        // The plugin ref — held for the D-04 undo seam (Undo/Redo/ClearUndoStack are NULL until FormMain
        // wires them; every call site null-checks via ?.Invoke()).
        private readonly IEditorPlugin editorPlugin;
        private readonly ToolTip toolTip = new ToolTip();

        // The launched roomy host. Singleton per editor session — hide-not-dispose; a re-launch re-Shows the
        // SAME instance (TerrainSubPanel companion-window idiom). Null until first launch / after a Dispose.
        private FormClientEffectEditor effectForm;

        // Controls (built imperatively in BuildContent so the Pitfall-8 Dock order is explicit).
        private UtinniButton btnOpenEditor;
        private UtinniButton btnOpenOverride;
        private UtinniLabel lblBanner;
        private UtinniLabel lblHint;
        private UtinniLabel lblFooter;

        // True once BuildContent completed without throwing. A failed build surfaces a state label and the
        // open/launch entry points no-op defensively (so a partial-load panel never throws).
        private bool contentReady;

        /// <summary>
        /// Constructs the thin docked ClientEffect entry SubPanel. The ctor signature mirrors the sibling
        /// SubPanels (<c>IEditorPlugin, HotkeyManager, UtINI</c>) so it slots into the existing
        /// <c>SubPanelContainer("Controls", …)</c> array in Plugin.cs. The ctor is MINIMAL + NO-THROW: it
        /// assigns fields + <c>InitializeComponent()</c> then delegates to the guarded
        /// <see cref="BuildContentSafe"/> (REVIEWS Codex divergent) — a throwing build surfaces a read-only
        /// state label instead of cascading the whole IEditorPlugin out of MEF compose (Pitfall 5/8).
        /// </summary>
        public EffectsSubPanel(IEditorPlugin editorPlugin, HotkeyManager hotkeyManager, UtINI ini)
            : base("Effects")
        {
            // The base SubPanel ctor sets the collapsible-header text + pins width to 417px. InitializeComponent
            // is a no-op container hook (the layout is built imperatively below).
            InitializeComponent();

            this.editorPlugin = editorPlugin;

            BuildContentSafe();
        }

        // The MEF-safe guarded build (REVIEWS Codex divergent): the ctor stays no-throw by routing the real
        // BuildContent() through this single try/catch. On failure it surfaces a read-only state label and
        // leaves contentReady false so the launch/open entries no-op defensively — a partial control is never
        // reachable past a half-completed build.
        private void BuildContentSafe()
        {
            try
            {
                BuildContent();
                contentReady = true;
            }
            catch (Exception ex)
            {
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
            toolTip.SetToolTip(btnOpenEditor, "Open the roomy ClientEffect editor host.");

            btnOpenOverride = new UtinniButton
            {
                Text = OpenOverrideLabel,
                Location = new Point(150, 30),
                Size = new Size(180, 22),
                DrawOutline = false,
                UseDisableColor = true,
                UseVisualStyleBackColor = true,
            };
            btnOpenOverride.Click += OnOpenOverrideClicked;
            toolTip.SetToolTip(btnOpenOverride, "Open an existing loose-override ClientEffect .iff directly for editing.");

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

        // Minimal failure surface: a single read-only label so a partial build never throws the ctor.
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
                    Text = "Effects panel failed to initialize: " + (ex == null ? "(unknown)" : ex.Message),
                };
                Controls.Add(lbl);
            }
            catch
            {
                // Last-ditch: even the failure surface must not re-throw out of the ctor.
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Singleton host launch (hide-not-dispose) — the TerrainSubPanel companion-window pattern. A
        // re-launch re-Shows / Activates the SAME instance. Returns the live host (or null if it can't be
        // created).
        // ─────────────────────────────────────────────────────────────────────

        private FormClientEffectEditor LaunchHost()
        {
            if (!contentReady) return null;
            if (effectForm == null || effectForm.IsDisposed)
            {
                effectForm = new FormClientEffectEditor(editorPlugin);
            }
            if (effectForm.Visible)
            {
                effectForm.Activate();
            }
            else
            {
                effectForm.Show();
            }
            return effectForm;
        }

        private void OnOpenEditorClicked(object sender, EventArgs e)
        {
            LaunchHost();
        }

        // Direct loose-override open (D-09): launch the singleton host and trigger ITS loose-override file
        // picker (FormClientEffectEditor.PromptOpenLooseOverride → OpenFileDialog). The picker lives in the
        // host (one source of the .iff dialog), so the thin docked panel never runs a modal dialog of its
        // own — it is a pure launcher (the host reads + decodes inside its own try/catch, red status on
        // failure).
        private void OnOpenOverrideClicked(object sender, EventArgs e)
        {
            try
            {
                FormClientEffectEditor host = LaunchHost();
                if (host == null) return;
                host.PromptOpenLooseOverride();
            }
            catch
            {
                // A launch failure must never tear down the docked panel.
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Public open entries the TRE Browser hand-off (D-09) calls. The hand-off finds THIS panel instance
        // via the plugin's GetStandalonePanels() "Controls" container and forwards a read-only TRE payload
        // (the TRE is never edited in place — the first save writes a loose override). Both entries launch
        // the singleton host and forward to its open path; they never throw.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Opens a ClientEffect <c>.iff</c> handed off read-only from the TRE Browser. Launches the
        /// singleton host and forwards to <see cref="FormClientEffectEditor.OpenFromTreEntry"/> (which
        /// decodes inside its own try/catch — red status on failure; the first commit writes a loose
        /// override, D-09). Null-checks the undo seam. Never throws.
        /// </summary>
        public void OpenFromTre(byte[] payload, string archivePath, string logicalPath)
        {
            try
            {
                FormClientEffectEditor host = LaunchHost();
                if (host == null) return;
                editorPlugin?.ClearUndoStack?.Invoke(); // D-04 undo seam — NULL until FormMain wires it.
                host.OpenFromTreEntry(payload, archivePath, logicalPath);
            }
            catch
            {
                // The hand-off must never tear down the panel or the TRE Browser.
            }
        }

        /// <summary>
        /// Opens an existing loose-override <c>.iff</c> directly (the same path the panel's own
        /// "Open ClientEffect Override…" button uses) — exposed so a future caller can route a known loose
        /// path through the singleton host. Never throws.
        /// </summary>
        public void OpenLooseOverride(string loosePath)
        {
            try
            {
                FormClientEffectEditor host = LaunchHost();
                if (host == null) return;
                host.OpenLooseOverride(loosePath);
            }
            catch
            {
                // Defensive — a bad path is surfaced by the host's own red status, never a throw here.
            }
        }
    }
}
