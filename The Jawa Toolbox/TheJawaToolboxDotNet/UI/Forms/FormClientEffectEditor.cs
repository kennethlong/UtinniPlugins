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
// The roomy ClientEffect command-list + field-editor host the thin docked EffectsSubPanel (22-04, D-04)
// launches. It is a pure CONSUMER of the shipped Phase 22 Plan 01 CLEF codec (ClientEffectDocument /
// MutableClientEffect / ClientEffectCommand / ClefFieldCodec / ClefCommandDefaults) and the in-proc save
// (ClientEffectSaveTargets). It adds ZERO new format logic: no byte offsets (ClefFieldCodec is the single
// source), no reload trigger of its own (Preview is honest-candor-only this build — D-07), no add-command
// default values of its own (ClefCommandDefaults is the single source — REVIEWS MEDIUM #5).
//
// Each command-list row STORES its backing command's StableId (the DeriveStableId ordinal-path from Plan
// 01) so undo/save/remove/reorder address the correct leaf, not a row index (REVIEWS HIGH #1). The Form
// save uses the IN-PROC ClientEffectSaveTargets, NOT apply-save-effect — the ClientEffectInProcSaveParity
// test proves they converge byte-for-byte (REVIEWS MEDIUM #9).
//
// MEF-safety (REVIEWS Codex divergent / Pitfall 5/8): the ctor is MINIMAL + NO-THROW — it assigns fields +
// InitializeComponent() then calls a guarded BuildContentSafe() that wraps content construction in
// try/catch and surfaces a red failure-state label. A throwing ctor would cascade the IEditorPlugin out of
// MEF compose; a partial form reachable past a whole-ctor try/catch is itself a hazard (Codex divergent),
// so the guarded build sets an explicit failure state instead. CLEF layout/semantics were studied from
// swg-client-v2 only (no code, comments, identifier names, or test fixtures copied from any reference
// source). Implementation original to Utinni under MIT.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using TJT.Saving;                              // ClientEffectSaveTargets
using TJT.UI.Controls;                         // ThemedDataGridView
using UtinniCore.Utinni;                       // Game.IsRunning (P/Invoke — ALWAYS try/catch), ParticlePreview, UtINI
using UtinniCoreDotNet.Callbacks;              // GameCallbacks.AddMainLoopCall (game-thread marshaling)
using UtinniCoreDotNet.Formats.ClientEffect;   // ClientEffectDocument / MutableClientEffect / ClefFieldCodec / ClefCommandDefaults
using UtinniCoreDotNet.Formats.Iff;            // OpenSource / MutableIffNode
using UtinniCoreDotNet.PluginFramework;        // IEditorPlugin (undo seam)
using UtinniCoreDotNet.Saving;                 // LooseOverridePath (only via the save target — none direct here)
using UtinniCoreDotNet.UI;                     // SingletonFormClosePolicy
using UtinniCoreDotNet.UI.Controls;            // UtinniLabel / UtinniButton / UtinniContextMenuStrip
using UtinniCoreDotNet.UI.Forms;               // UtinniForm, IEditorForm
using UtinniCoreDotNet.UI.Theme;               // Colors

namespace TJT.UI.Forms
{
    /// <summary>
    /// The roomy ClientEffect (FORM CLEF) command-list + field-editor host (22-04, PROD-W2-CFX-01). Opens a
    /// CLEF <c>.iff</c> read-only from the TRE Browser or directly from a loose override, renders the flat
    /// command list (each row carrying its <see cref="ClientEffectCommand.StableId"/> — REVIEWS HIGH #1) +
    /// a version-aware typed field grid (unknown/truncated commands degrade to a read-only raw/hex view,
    /// never a hard failure — D-06/D-13), edits string/scalar/flag fields + adds/removes/reorders commands,
    /// saves byte-exact through the in-proc <see cref="ClientEffectSaveTargets"/> loose-override path, and
    /// surfaces the honest-candor "Preview in client" action (no live retrigger this build — D-07).
    ///
    /// <para><b>Singleton hide-not-dispose:</b> <see cref="OnFormClosing"/> delegates its CloseReason
    /// decision to <see cref="SingletonFormClosePolicy.ShouldHideInsteadOfDispose"/> (the Phase-8-locked
    /// pattern, mandatory for MEF-registered editor forms from commit 1).</para>
    /// </summary>
    public partial class FormClientEffectEditor : UtinniForm, IEditorForm
    {
        private const string SettingsSection = "EffectsEditor";
        private const string BaseTitle = "ClientEffect Editor";

        // ── LOCKED copy (22-UI-SPEC Copywriting Contract — verbatim; constants so the grep gates can verify
        //    them and a future edit can't silently soften the wording). ──
        private const string DecA3Footer =
            "Preview is live in the real SWG client — Utinni never renders effects itself.";
        private const string EmptyHeading = "No effect loaded";
        private const string EmptyBody =
            "Open a ClientEffect .iff from the TRE Browser, or open an existing loose override, to view and edit its command list.";
        private const string NoSelectionBody = "Select a command to view its fields.";
        private const string EmptyCommandListBody =
            "This effect has no commands. Use Add command to insert one.";
        private const string RawFieldHint =
            "This command isn't typed yet — its original bytes are preserved exactly and saved unchanged.";
        private const string VersionLockedHint =
            "This effect is version <vNNNN> — only the fields that version defines are shown. The version is preserved on save.";
        private const string PreviewNoHookTooltip =
            "Live preview isn't wired this build — edits show on the next scene change or relog.";
        private const string PreviewUnavailableTooltip = "No live client — start SWG to preview in-scene.";
        private const string PreviewLiveCapable = "Re-triggered live effect.";
        private const string SaveInPlaceLabel = "Save (in place)";
        private const string SaveLooseOverrideLabel = "Save as loose override";
        private const string SaveAsLabel = "Save As…";
        private const string AddCommandLabel = "Add command";
        private const string RemoveCommandLabel = "Remove command";
        private const string MoveUpLabel = "Move up";
        private const string MoveDownLabel = "Move down";
        private const string DirtyMarker = "● Unsaved changes";

        // The plugin ref — held for the undo seam (Undo/Redo/ClearUndoStack are NULL until FormMain wires
        // them; every call site null-checks via ?.Invoke()).
        private readonly IEditorPlugin editorPlugin;
        private readonly UtINI ini;
        private readonly ToolTip toolTip = new ToolTip();

        // ── Model + provenance (null until an open path binds a document). ──
        private MutableClientEffect effect;
        private OpenSource source;
        private string displayName;
        private string lastSavedPath;

        // Bucket B-2: the TRE-relative logical name of the loaded .cef (e.g. "clienteffect/foo.cef"),
        // captured at open. This is what the native replay seam hands ClientEffectManager::playClientEffect
        // (CrcLowerString). Null for loose/unknown opens -> ResolveClientEffectLogicalName() falls back.
        private string cefLogicalName;

        // ── Editor-local undo/redo of the WHOLE serialized CLEF (CON-M-05: independent of the scene
        //    UndoRedoManager). Each entry is a full byte snapshot — simple + correct for the small CLEF
        //    files, and it covers field edits AND add/remove/reorder uniformly. ──
        private readonly Stack<byte[]> undoStack = new Stack<byte[]>();
        private readonly Stack<byte[]> redoStack = new Stack<byte[]>();
        private bool isDirty;

        // ── Controls (built imperatively in BuildContent). ──
        private UtinniLabel lblBanner;
        private Panel pnlBannerAccent;
        private UtinniButton btnOpen;
        private UtinniButton btnSave;
        private UtinniButton btnUndo;
        private UtinniButton btnRedo;
        private UtinniButton btnAdd;
        private UtinniButton btnRemove;
        private UtinniButton btnMoveUp;
        private UtinniButton btnMoveDown;
        private UtinniButton btnPreview;
        private UtinniContextMenuStrip saveMenu;
        private ToolStripMenuItem miSaveInPlace;
        private ToolStripMenuItem miSaveLooseOverride;
        private ToolStripMenuItem miSaveAs;
        private ContextMenuStrip addMenu;
        private SplitContainer splitMain;
        private ListView listCommands;
        private ThemedDataGridView gridFields;
        private UtinniLabel lblStatus;
        private UtinniLabel lblDirty;
        private UtinniLabel lblCandorFooter;

        // True once BuildContent completed without throwing. A failed build surfaces a state panel and the
        // open/edit entry points no-op defensively.
        private bool layoutReady;
        // Guards the programmatic grid repopulate so a field-edit commit doesn't re-fire on rebind.
        private bool suppressFieldCommit;

        private const int ColField = 0;
        private const int ColValue = 1;
        private const int ColType = 2;

        /// <summary>
        /// Constructs the ClientEffect editor host. The ctor is MINIMAL + NO-THROW (REVIEWS Codex
        /// divergent): it assigns fields + <c>InitializeComponent()</c> then delegates to the guarded
        /// <see cref="BuildContentSafe"/> — a throwing build surfaces a red failure-state label instead of
        /// cascading the IEditorPlugin out of MEF compose (Pitfall 5/8).
        /// </summary>
        public FormClientEffectEditor(IEditorPlugin editorPlugin)
        {
            InitializeComponent();

            this.editorPlugin = editorPlugin;
            this.ini = editorPlugin != null ? editorPlugin.GetConfig() : null;

            BuildContentSafe();
        }

        // The MEF-safe guarded build (REVIEWS Codex divergent): the ctor stays no-throw by routing content
        // construction through this single try/catch. On failure it surfaces a red state label and leaves
        // layoutReady false so the open/edit entries no-op defensively — a partial form is never reachable.
        private void BuildContentSafe()
        {
            try
            {
                CreateSettings();
                BuildContent();
                layoutReady = true;
                ShowEmptyState();
                RefreshActionButtonState();
            }
            catch (Exception ex)
            {
                SetFailure(ex);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Layout — Pitfall 8 LOCKED idiom: Dock.Fill content FIRST (front-most), Size BEFORE
        // SplitterDistance, Panel1MinSize/Panel2MinSize collapse guards. Theme via Colors.* only.
        // ─────────────────────────────────────────────────────────────────────

        private void BuildContent()
        {
            SuspendLayout();

            Text = BaseTitle;
            BackColor = Colors.Primary();
            ForeColor = Colors.Font();
            Font = new Font("Segoe UI", 8.25f); // base 8.25pt; Bold reserved to the banner.
            ClientSize = new Size(GetIniInt("width", 980), GetIniInt("height", 640));
            MinimumSize = new Size(620, 380);
            StartPosition = FormStartPosition.CenterParent;
            FormClosing += OnFormClosing;
            KeyPreview = true;
            KeyDown += OnKeyDown;

            // ── Dock.Fill content region (the nested SplitContainer) — added FIRST so the Top/Bottom strips
            //    claim their edges (feedback_winforms_dockfill_zorder). ──
            splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,   // command list left | field grid right
                BackColor = Colors.PrimaryHighlight(),
                SplitterWidth = 4,
                FixedPanel = FixedPanel.Panel1,
            };
            // SplitterDistance + the Panel1/Panel2 min-sizes are applied in OnShown (ApplySplitterLayout),
            // NOT here. A Dock.Fill SplitContainer IGNORES an explicit Size while it is unparented, so its
            // width stays at the ~150px default — at which point ANY SplitterDistance (or a Panel1MinSize
            // wider than that default) is out of range and the setter THROWS, dropping BuildContentSafe to
            // its red failure state. Deferring to first-show, when the form has a real client width, keeps
            // the value valid and lets us clamp a stale persisted value into range. (Replaces the prior —
            // incorrect — "Size BEFORE SplitterDistance keeps it valid at construction" idiom, which the
            // 22-04 layout-smoke harness proved throws every time.)

            listCommands = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                BackColor = Colors.PrimaryHighlight(),
                ForeColor = Colors.Font(),
                BorderStyle = BorderStyle.None,
            };
            listCommands.Columns.Add("#", 36);
            listCommands.Columns.Add("Tag", 64);
            listCommands.Columns.Add("Summary", 200);
            listCommands.SelectedIndexChanged += OnCommandSelectionChanged;

            gridFields = new ThemedDataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToOrderColumns = false,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            };
            gridFields.Columns.Add(new DataGridViewTextBoxColumn { Name = "Field", HeaderText = "Field", ReadOnly = true, FillWeight = 34 });
            gridFields.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", ReadOnly = false, FillWeight = 50, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            gridFields.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", ReadOnly = true, FillWeight = 16 });
            gridFields.CellEndEdit += OnFieldCellEndEdit;

            splitMain.Panel1.Controls.Add(listCommands);
            splitMain.Panel2.Controls.Add(gridFields);

            // ── Dock.Top banner + 2px accent rule ──
            pnlBannerAccent = new Panel
            {
                Dock = DockStyle.Top,
                Height = 2,
                BackColor = Colors.Secondary(),
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

            // ── Dock.Top toolbar ──
            Panel pnlActions = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                BackColor = Colors.Primary(),
                Padding = new Padding(8, 4, 8, 4),
            };
            int x = 0;
            btnOpen = MakeButton("Open", ref x, 64);
            btnSave = MakeButton("Save ▾", ref x, 72);
            btnUndo = MakeButton("Undo", ref x, 56);
            btnRedo = MakeButton("Redo", ref x, 56);
            btnAdd = MakeButton(AddCommandLabel, ref x, 100);
            btnRemove = MakeButton(RemoveCommandLabel, ref x, 116);
            btnMoveUp = MakeButton(MoveUpLabel, ref x, 70);
            btnMoveDown = MakeButton(MoveDownLabel, ref x, 80);
            btnPreview = MakeButton("Preview in client", ref x, 116);
            btnOpen.Click += OnOpenClicked;
            btnSave.Click += OnSaveButtonClick;
            btnUndo.Click += (s, e) => DoUndo();
            btnRedo.Click += (s, e) => DoRedo();
            btnAdd.Click += OnAddCommandClicked;
            btnRemove.Click += OnRemoveCommandClicked;
            btnMoveUp.Click += (s, e) => MoveSelected(up: true);
            btnMoveDown.Click += (s, e) => MoveSelected(up: false);
            btnPreview.Click += OnPreviewClicked;
            pnlActions.Controls.Add(btnPreview);
            pnlActions.Controls.Add(btnMoveDown);
            pnlActions.Controls.Add(btnMoveUp);
            pnlActions.Controls.Add(btnRemove);
            pnlActions.Controls.Add(btnAdd);
            pnlActions.Controls.Add(btnRedo);
            pnlActions.Controls.Add(btnUndo);
            pnlActions.Controls.Add(btnSave);
            pnlActions.Controls.Add(btnOpen);

            BuildSaveMenu();
            BuildAddMenu();

            // ── Dock.Bottom status + dirty marker + dimmed DEC-A3 candor footer ──
            lblCandorFooter = new UtinniLabel
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 18,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                ForeColor = Colors.FontDisabled(),
                Text = DecA3Footer,
            };
            lblDirty = new UtinniLabel
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 18,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                ForeColor = Colors.Secondary(), // accent reserved use #2 (dirty marker)
                Text = "",
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
            Controls.Add(pnlBannerAccent);    // Dock.Top
            Controls.Add(lblStatus);          // Dock.Bottom
            Controls.Add(lblDirty);           // Dock.Bottom
            Controls.Add(lblCandorFooter);    // Dock.Bottom

            // Keep the docked banner/accent ABOVE the form's titlebar padding (UtinniForm draws a 32px bar).
            Padding = new Padding(0, 32, 0, 0);

            ResumeLayout(false);
            PerformLayout();
        }

        // Applied-once flag for the deferred splitter layout (OnShown can fire again on a singleton re-show;
        // we keep the user's dragged splitter position by only applying the default+clamp the first time).
        private bool splitterLayoutApplied;

        // SplitterDistance and the Panel min-sizes need a REAL (parented, shown) width to validate against —
        // a Dock.Fill SplitContainer only has its ~150px default until the form is shown, and setting them at
        // construction throws (see BuildContent). First-show is the earliest point the form has its true
        // client width, so we apply them here, clamping any stale persisted value into the valid range. Order
        // matters: SplitterDistance is set FIRST (while min-sizes are still the 25px default, so a wide value
        // is in range), THEN the min-sizes are tightened (the now-current distance already satisfies them).
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplySplitterLayout();
        }

        private void ApplySplitterLayout()
        {
            if (!layoutReady || splitMain == null || splitterLayoutApplied) return;
            try
            {
                const int panel1Min = 120;   // command-list (left) lower bound
                const int panel2Min = 160;   // field-grid (right) lower bound
                int width = splitMain.Width;
                int reserveRight = panel2Min + splitMain.SplitterWidth;
                if (width - reserveRight < panel1Min) return; // too narrow to place a constrained splitter
                int desired = GetIniInt("splitterDistance", 320);
                int clamped = Math.Max(panel1Min, Math.Min(desired, width - reserveRight));
                splitMain.SplitterDistance = clamped;         // valid now: min-sizes are still the 25px default
                splitMain.Panel1MinSize = panel1Min;          // distance >= panel1Min already holds
                splitMain.Panel2MinSize = panel2Min;          // width - distance - splitter >= panel2Min already holds
                splitterLayoutApplied = true;
            }
            catch
            {
                // Splitter placement must never break an already-built editor.
            }
        }

        private UtinniButton MakeButton(string text, ref int x, int width)
        {
            var b = new UtinniButton
            {
                Text = text,
                Location = new Point(8 + x, 4),
                Size = new Size(width, 22),
                DrawOutline = false,
                UseDisableColor = true,
                UseVisualStyleBackColor = true,
            };
            x += width + 4;
            return b;
        }

        private void BuildSaveMenu()
        {
            saveMenu = new UtinniContextMenuStrip();
            miSaveInPlace = new ToolStripMenuItem(SaveInPlaceLabel);
            miSaveInPlace.Click += OnSaveInPlaceClick;
            miSaveLooseOverride = new ToolStripMenuItem(SaveLooseOverrideLabel);
            miSaveLooseOverride.Click += OnSaveLooseOverrideClick;
            miSaveAs = new ToolStripMenuItem(SaveAsLabel);
            miSaveAs.Click += OnSaveAsClick;
            saveMenu.Items.AddRange(new ToolStripItem[] { miSaveInPlace, miSaveLooseOverride, miSaveAs });
        }

        private void BuildAddMenu()
        {
            addMenu = new ContextMenuStrip();
            foreach (string tag in new[] { "CPAP", "PSND", "CLGT", "CAMS", "FFBK" })
            {
                string captured = tag;
                var mi = new ToolStripMenuItem(tag);
                mi.Click += (s, e) => AddCommandOfTag(captured);
                addMenu.Items.Add(mi);
            }
        }

        // Minimal failure surface: a single read-only red label so a partial build never throws the ctor.
        private void SetFailure(Exception ex)
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
                    Padding = new Padding(12, 40, 12, 12),
                    Text = "ClientEffect editor failed to initialize: " + (ex == null ? "(unknown)" : ex.Message),
                };
                Controls.Add(lbl);
            }
            catch
            {
                // Last-ditch: even the failure surface must not re-throw out of the ctor.
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // IEditorForm + singleton hide-not-dispose.
        // ─────────────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string GetName() { return BaseTitle; }

        /// <inheritdoc/>
        public Form Create(IEditorPlugin plugin, List<Form> parentChildren)
        {
            foreach (Form form in parentChildren)
            {
                if (form.GetType() == typeof(FormClientEffectEditor))
                {
                    form.Activate();
                    return null;
                }
            }
            FormClientEffectEditor newForm = new FormClientEffectEditor(plugin);
            newForm.Show();
            parentChildren.Add(newForm);
            return newForm;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (ini != null)
                {
                    ini.AddSetting(SettingsSection, "width", Width.ToString(CultureInfo.InvariantCulture), UtINI.Value.Types.VtInt);
                    ini.AddSetting(SettingsSection, "height", Height.ToString(CultureInfo.InvariantCulture), UtINI.Value.Types.VtInt);
                    if (splitMain != null)
                    {
                        ini.AddSetting(SettingsSection, "splitterDistance",
                            splitMain.SplitterDistance.ToString(CultureInfo.InvariantCulture), UtINI.Value.Types.VtInt);
                    }
                    ini.Save();
                }
            }
            catch
            {
                // Persistence is best-effort; never block close.
            }

            if (SingletonFormClosePolicy.ShouldHideInsteadOfDispose(e.CloseReason))
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Z) { DoUndo(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Y) { DoRedo(); e.Handled = true; }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Open paths (D-09) — TRE source opens read-only (first save writes a loose override); a loose
        // source commits in place. Both decode via ClientEffectDocument.FromBytes inside try/catch (red
        // status on failure / parse exception caught → NEVER thrown out of the ctor or a handler).
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Opens a CLEF <c>.iff</c> handed off read-only from the TRE Browser.</summary>
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
                MutableClientEffect doc = ClientEffectDocument.FromBytes(payload);
                string rel = string.IsNullOrEmpty(logicalPath) ? Path.GetFileName(archivePath ?? "") : logicalPath;
                OpenSource src = new OpenSource.TreArchive(archivePath ?? "", 0, rel ?? "");
                BindDocument(doc, src, rel);
                // Retain the full TRE-relative .cef logical name for the live replay (BindDocument reset it).
                cefLogicalName = rel;
                SetStatus("Opened " + rel + " (read-only TRE — first save writes a loose override).", false);
            }
            catch (ClientEffectParseException ex)
            {
                SetStatus("Open failed: " + ex.Message, true);
            }
            catch (Exception ex)
            {
                SetStatus("Open failed: " + ex.Message, true);
            }
        }

        /// <summary>Opens an existing loose-override CLEF <c>.iff</c> directly from disk.</summary>
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
                MutableClientEffect doc = ClientEffectDocument.FromBytes(bytes);
                OpenSource src = new OpenSource.LooseFile(loosePath);
                BindDocument(doc, src, Path.GetFileName(loosePath));
                lastSavedPath = loosePath;
                SetStatus("Opened " + Path.GetFileName(loosePath) + " (loose override).", false);
            }
            catch (ClientEffectParseException ex)
            {
                SetStatus("Open failed: " + ex.Message, true);
            }
            catch (Exception ex)
            {
                SetStatus("Open failed: " + ex.Message, true);
            }
        }

        private void OnOpenClicked(object sender, EventArgs e)
        {
            PromptOpenLooseOverride();
        }

        /// <summary>
        /// Shows the host's loose-override file picker and, on OK, opens the chosen CLEF <c>.iff</c> in
        /// place. Exposed so the thin docked <c>EffectsSubPanel</c> can trigger the SAME picker (one source
        /// of the dialog) instead of running a modal dialog of its own. No-ops if the layout failed to build.
        /// </summary>
        public void PromptOpenLooseOverride()
        {
            if (!layoutReady) return;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = OpenOverrideTitle;
                ofd.Filter = ClefDialogFilter;
                // Default to the loose-override folder (where saves land) instead of inheriting whatever
                // directory the process last used for a file dialog (e.g. the client's string/en).
                string initialDir = ResolveLooseOverrideDir();
                if (!string.IsNullOrEmpty(initialDir)) ofd.InitialDirectory = initialDir;
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                OpenLooseOverride(ofd.FileName);
            }
        }

        private const string OpenOverrideTitle = "Open ClientEffect Override…";

        // ClientEffect template files use the .cef extension (the IFF FORM tag is CLEF); .iff is offered as a
        // secondary filter for hand-renamed copies, and All files as the escape hatch.
        private const string ClefDialogFilter =
            "ClientEffect (*.cef)|*.cef|IFF (*.iff)|*.iff|All files (*.*)|*.*";

        // The loose-override directory (<clientRoot>/<looseSubDir>) used as the Open/Save-As initial dir, or
        // null when the client root can't be resolved. Falls back to the client root if the loose subdir
        // doesn't exist on disk yet (first run before any override is saved).
        private string ResolveLooseOverrideDir()
        {
            try
            {
                string root = ResolveClientRoot();
                if (string.IsNullOrEmpty(root)) return null;
                string sub = ResolveLooseOverrideSubDir();
                if (!string.IsNullOrEmpty(sub))
                {
                    string looseDir = Path.Combine(root, sub);
                    if (Directory.Exists(looseDir)) return looseDir;
                }
                return Directory.Exists(root) ? root : null;
            }
            catch
            {
                return null;
            }
        }

        // Binds a freshly-decoded document: records provenance, resets edit/undo state, populates the
        // command list, refreshes the banner + buttons, and shows the no-selection field hint.
        private void BindDocument(MutableClientEffect doc, OpenSource src, string name)
        {
            effect = doc;
            source = src;
            displayName = name;
            lastSavedPath = (src is OpenSource.LooseFile loose) ? loose.Path : null;
            // Reset the TRE logical name; OpenFromTreEntry re-sets it from the real logicalPath.
            cefLogicalName = null;
            isDirty = false;
            undoStack.Clear();
            redoStack.Clear();

            PopulateCommandList();
            RefreshBanner();
            RefreshDirtyMarker();
            RefreshActionButtonState();

            if (effect.IsRawPreserved)
            {
                SetStatus(VersionLockedHint.Replace("<vNNNN>", "v" + effect.Version)
                    + "  (unrecognized version — read-only)", false);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Command list population — flat (NO tree). Each row's Tag carries the backing leaf + its StableId
        // (REVIEWS HIGH #1) so undo/save/remove/reorder address the correct command, not a row index.
        // ─────────────────────────────────────────────────────────────────────

        // The per-row payload stored in ListViewItem.Tag — the backing leaf + its ordinal-path StableId.
        private sealed class CommandRowRef
        {
            public MutableIffNode Leaf;
            public string StableId;
            public ClientEffectCommand Command;
        }

        private void PopulateCommandList()
        {
            listCommands.BeginUpdate();
            try
            {
                listCommands.Items.Clear();
                if (effect == null) return;

                int i = 0;
                foreach (ClientEffectCommand cmd in effect.Commands)
                {
                    var item = new ListViewItem((i + 1).ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(cmd.Tag ?? "????");
                    item.SubItems.Add(SummarizeCommand(cmd));
                    item.Tag = new CommandRowRef { Leaf = cmd.Leaf, StableId = cmd.StableId, Command = cmd };
                    if (cmd.IsRaw) item.ForeColor = Colors.FontDisabled();
                    listCommands.Items.Add(item);
                    i++;
                }
            }
            finally
            {
                listCommands.EndUpdate();
            }

            if (effect != null && effect.Commands.Count == 0)
            {
                ShowGridMessage(EmptyCommandListBody, Colors.FontDisabled());
            }
            else
            {
                ShowGridMessage(NoSelectionBody, Colors.FontDisabled());
            }
        }

        private static string SummarizeCommand(ClientEffectCommand cmd)
        {
            if (cmd.IsRaw) return "(raw — preserved)";
            switch (cmd.Tag)
            {
                case "CPAP": return cmd.StringValue ?? "";
                case "PSND": return cmd.StringValue ?? "";
                case "FFBK": return cmd.StringValue ?? "";
                case "CLGT":
                    return cmd.Bytes != null ? ("rgb " + cmd.Bytes[0] + "," + cmd.Bytes[1] + "," + cmd.Bytes[2]) : "";
                case "CAMS":
                    return cmd.Floats != null && cmd.Floats.Length > 0
                        ? ("magnitude " + cmd.Floats[0].ToString(CultureInfo.InvariantCulture)) : "";
                default: return "";
            }
        }

        private CommandRowRef SelectedRow()
        {
            if (listCommands == null || listCommands.SelectedItems.Count == 0) return null;
            return listCommands.SelectedItems[0].Tag as CommandRowRef;
        }

        private void OnCommandSelectionChanged(object sender, EventArgs e)
        {
            if (!layoutReady) return;
            CommandRowRef row = SelectedRow();
            RenderFieldGrid(row);
            RefreshActionButtonState();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Field grid — version-aware typed fields for the 5 known commands; unknown/truncated commands
        // (IsRaw) degrade to a read-only raw/hex field list (Consolas, dimmed) — NEVER a hard failure
        // (D-06/D-13; REVIEWS MEDIUM #4). String fields are free-text editable (the variable-length edit).
        // ─────────────────────────────────────────────────────────────────────

        // Per-row edit descriptor stored on the Value cell's Tag: which field of which command this is.
        private sealed class FieldEditContext
        {
            public CommandRowRef Row;
            public string FieldName;
            public FieldKind Kind;
            public int FloatIndex;   // index into Floats[] for a float field
            public int ByteIndex;    // index into Bytes[] for a CLGT channel
        }
        private enum FieldKind { String, Float, ByteChannel, Bool, Int32 }

        private void RenderFieldGrid(CommandRowRef row)
        {
            if (row == null) { ShowGridMessage(NoSelectionBody, Colors.FontDisabled()); return; }

            suppressFieldCommit = true;
            try
            {
                gridFields.Rows.Clear();
                ClientEffectCommand cmd = row.Command;

                if (effect.IsRawPreserved || cmd.IsRaw)
                {
                    RenderRawHexRows(cmd);
                    return;
                }

                switch (cmd.Tag)
                {
                    case "CPAP": RenderCpap(row, cmd); break;
                    case "PSND": AddEditableRow(row, "soundTemplateName", cmd.StringValue, "string", FieldKind.String, 0, 0); break;
                    case "CLGT": RenderClgt(row, cmd); break;
                    case "CAMS": RenderCams(row, cmd); break;
                    case "FFBK": RenderFfbk(row, cmd); break;
                    default: RenderRawHexRows(cmd); break;
                }
            }
            catch (Exception ex)
            {
                ShowGridMessage("Field render failed: " + ex.Message, Color.Red);
            }
            finally
            {
                suppressFieldCommit = false;
            }
        }

        private void RenderCpap(CommandRowRef row, ClientEffectCommand cmd)
        {
            AddEditableRow(row, "appearanceTemplateName", cmd.StringValue, "string", FieldKind.String, 0, 0);
            AddEditableRow(row, "timeInSeconds", Flt(cmd.Floats, 0), "float", FieldKind.Float, 0, 0);
            if (cmd.SoftParticleTerminate.HasValue)
            {
                AddEditableRow(row, "softParticleTerminate", cmd.SoftParticleTerminate.Value ? "1" : "0", "bool", FieldKind.Bool, 0, 0);
            }
            if (cmd.Floats != null && cmd.Floats.Length >= 5)
            {
                AddEditableRow(row, "minScale", Flt(cmd.Floats, 1), "float", FieldKind.Float, 1, 0);
                AddEditableRow(row, "maxScale", Flt(cmd.Floats, 2), "float", FieldKind.Float, 2, 0);
                AddEditableRow(row, "minPlaybackRate", Flt(cmd.Floats, 3), "float", FieldKind.Float, 3, 0);
                AddEditableRow(row, "maxPlaybackRate", Flt(cmd.Floats, 4), "float", FieldKind.Float, 4, 0);
            }
        }

        private void RenderClgt(CommandRowRef row, ClientEffectCommand cmd)
        {
            AddEditableRow(row, "r", cmd.Bytes != null ? cmd.Bytes[0].ToString(CultureInfo.InvariantCulture) : "0", "uint8", FieldKind.ByteChannel, 0, 0);
            AddEditableRow(row, "g", cmd.Bytes != null ? cmd.Bytes[1].ToString(CultureInfo.InvariantCulture) : "0", "uint8", FieldKind.ByteChannel, 0, 1);
            AddEditableRow(row, "b", cmd.Bytes != null ? cmd.Bytes[2].ToString(CultureInfo.InvariantCulture) : "0", "uint8", FieldKind.ByteChannel, 0, 2);
            AddEditableRow(row, "timeInSeconds", Flt(cmd.Floats, 0), "float", FieldKind.Float, 0, 0);
            AddEditableRow(row, "constantAttenuation", Flt(cmd.Floats, 1), "float", FieldKind.Float, 1, 0);
            AddEditableRow(row, "linearAttenuation", Flt(cmd.Floats, 2), "float", FieldKind.Float, 2, 0);
            AddEditableRow(row, "quadraticAttenuation", Flt(cmd.Floats, 3), "float", FieldKind.Float, 3, 0);
            AddEditableRow(row, "range", Flt(cmd.Floats, 4), "float", FieldKind.Float, 4, 0);
        }

        private void RenderCams(CommandRowRef row, ClientEffectCommand cmd)
        {
            AddEditableRow(row, "magnitude", Flt(cmd.Floats, 0), "float", FieldKind.Float, 0, 0);
            AddEditableRow(row, "frequency", Flt(cmd.Floats, 1), "float", FieldKind.Float, 1, 0);
            AddEditableRow(row, "time", Flt(cmd.Floats, 2), "float", FieldKind.Float, 2, 0);
            AddEditableRow(row, "falloffRadius", Flt(cmd.Floats, 3), "float", FieldKind.Float, 3, 0);
        }

        private void RenderFfbk(CommandRowRef row, ClientEffectCommand cmd)
        {
            AddEditableRow(row, "forceFeedbackFile", cmd.StringValue, "string", FieldKind.String, 0, 0);
            AddEditableRow(row, "iterations", cmd.Int32Value.HasValue ? cmd.Int32Value.Value.ToString(CultureInfo.InvariantCulture) : "0", "int32", FieldKind.Int32, 0, 0);
            AddEditableRow(row, "range", Flt(cmd.Floats, 0), "float", FieldKind.Float, 0, 0);
        }

        private static string Flt(float[] arr, int i)
        {
            if (arr == null || i < 0 || i >= arr.Length) return "0";
            return arr[i].ToString("R", CultureInfo.InvariantCulture);
        }

        private void AddEditableRow(CommandRowRef row, string field, string value, string type, FieldKind kind, int floatIdx, int byteIdx)
        {
            int idx = gridFields.Rows.Add(field, value ?? "", type);
            DataGridViewRow gr = gridFields.Rows[idx];
            gr.Cells[ColValue].Tag = new FieldEditContext
            {
                Row = row, FieldName = field, Kind = kind, FloatIndex = floatIdx, ByteIndex = byteIdx,
            };
        }

        private void RenderRawHexRows(ClientEffectCommand cmd)
        {
            byte[] payload = cmd.Leaf != null ? cmd.Leaf.GetPayloadCopy() : new byte[0];
            int idx = gridFields.Rows.Add("raw bytes", BytesToHex(payload), "hex");
            DataGridViewRow gr = gridFields.Rows[idx];
            foreach (DataGridViewCell cell in gr.Cells)
            {
                cell.ReadOnly = true;
                cell.Style.ForeColor = Colors.FontDisabled();
                cell.Style.Font = new Font("Consolas", 9f);
            }
            int hint = gridFields.Rows.Add("", RawFieldHint, "");
            foreach (DataGridViewCell cell in gridFields.Rows[hint].Cells)
            {
                cell.ReadOnly = true;
                cell.Style.ForeColor = Colors.FontDisabled();
            }
        }

        private static string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "(empty)";
            var sb = new System.Text.StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private void ShowGridMessage(string body, Color color)
        {
            suppressFieldCommit = true;
            try
            {
                gridFields.Rows.Clear();
                int idx = gridFields.Rows.Add("", body, "");
                foreach (DataGridViewCell cell in gridFields.Rows[idx].Cells)
                {
                    cell.ReadOnly = true;
                    cell.Style.ForeColor = color;
                }
            }
            finally
            {
                suppressFieldCommit = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Field edit commit — re-encode the WHOLE command payload from the command's CURRENT decoded values
        // + the ONE substituted field (the codec has only whole-payload encoders; CLEF payloads are
        // variable-length), then EditCommandPayload (the length-ripple DOM re-rolls the FORM lengths). The
        // SAME re-encode-from-current-values-plus-one-field algorithm apply-save-effect uses (REVIEWS #9).
        // ─────────────────────────────────────────────────────────────────────

        private void OnFieldCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (suppressFieldCommit || !layoutReady || effect == null) return;
            if (e.ColumnIndex != ColValue || e.RowIndex < 0) return;
            DataGridViewCell cell = gridFields.Rows[e.RowIndex].Cells[ColValue];
            var ctx = cell.Tag as FieldEditContext;
            if (ctx == null || ctx.Row == null) return;

            string newValue = Convert.ToString(cell.Value ?? "", CultureInfo.InvariantCulture);
            try
            {
                PushUndoSnapshot();
                byte[] newPayload = ReencodeCommand(ctx.Row.Command, ctx, newValue);
                effect.EditCommandPayload(ctx.Row.Leaf, newPayload);
                MarkDirty();
                // Re-decode the row + re-render so derived fields/summary reflect the new bytes and the row
                // ref points at the fresh command view.
                RefreshAfterMutation(reselectStableId: ctx.Row.StableId);
                SetStatus("Edited " + ctx.FieldName + ".", false);
            }
            catch (Exception ex)
            {
                // Pop the snapshot we pushed (the edit didn't apply), restore the cell, surface red status.
                if (undoStack.Count > 0) undoStack.Pop();
                SetStatus("Edit rejected: " + ex.Message, true);
                RenderFieldGrid(ctx.Row); // restore the displayed value
            }
        }

        // Re-encodes a command's WHOLE payload from its current decoded values with the one edited field
        // substituted. CPAP is emitted at the file's existing version (D-03 — never upgraded).
        private byte[] ReencodeCommand(ClientEffectCommand cmd, FieldEditContext ctx, string newValue)
        {
            string version = effect.Version;
            switch (cmd.Tag)
            {
                case "CPAP":
                    {
                        string appearance = cmd.StringValue;
                        float time = Flt0(cmd.Floats, 0);
                        bool? soft = cmd.SoftParticleTerminate;
                        float? minS = NullableFlt(cmd.Floats, 1, 5);
                        float? maxS = NullableFlt(cmd.Floats, 2, 5);
                        float? minR = NullableFlt(cmd.Floats, 3, 5);
                        float? maxR = NullableFlt(cmd.Floats, 4, 5);
                        switch (ctx.FieldName)
                        {
                            case "appearanceTemplateName": appearance = newValue; break;
                            case "timeInSeconds": time = ParseFloat(newValue); break;
                            case "softParticleTerminate": soft = ParseBool(newValue); break;
                            case "minScale": minS = ParseFloat(newValue); break;
                            case "maxScale": maxS = ParseFloat(newValue); break;
                            case "minPlaybackRate": minR = ParseFloat(newValue); break;
                            case "maxPlaybackRate": maxR = ParseFloat(newValue); break;
                        }
                        return ClefFieldCodec.EncodeCpap(version, appearance, time, soft, minS, maxS, minR, maxR);
                    }
                case "PSND":
                    return ClefFieldCodec.EncodePsnd(ctx.FieldName == "soundTemplateName" ? newValue : cmd.StringValue);
                case "CLGT":
                    {
                        byte[] rgb = cmd.Bytes ?? new byte[] { 0, 0, 0 };
                        byte r = rgb.Length > 0 ? rgb[0] : (byte)0, g = rgb.Length > 1 ? rgb[1] : (byte)0, b = rgb.Length > 2 ? rgb[2] : (byte)0;
                        float time = Flt0(cmd.Floats, 0), ca = Flt0(cmd.Floats, 1), la = Flt0(cmd.Floats, 2), qa = Flt0(cmd.Floats, 3), range = Flt0(cmd.Floats, 4);
                        switch (ctx.FieldName)
                        {
                            case "r": r = ParseByte(newValue); break;
                            case "g": g = ParseByte(newValue); break;
                            case "b": b = ParseByte(newValue); break;
                            case "timeInSeconds": time = ParseFloat(newValue); break;
                            case "constantAttenuation": ca = ParseFloat(newValue); break;
                            case "linearAttenuation": la = ParseFloat(newValue); break;
                            case "quadraticAttenuation": qa = ParseFloat(newValue); break;
                            case "range": range = ParseFloat(newValue); break;
                        }
                        return ClefFieldCodec.EncodeClgt(r, g, b, time, ca, la, qa, range);
                    }
                case "CAMS":
                    {
                        float mag = Flt0(cmd.Floats, 0), freq = Flt0(cmd.Floats, 1), time = Flt0(cmd.Floats, 2), fall = Flt0(cmd.Floats, 3);
                        switch (ctx.FieldName)
                        {
                            case "magnitude": mag = ParseFloat(newValue); break;
                            case "frequency": freq = ParseFloat(newValue); break;
                            case "time": time = ParseFloat(newValue); break;
                            case "falloffRadius": fall = ParseFloat(newValue); break;
                        }
                        return ClefFieldCodec.EncodeCams(mag, freq, time, fall);
                    }
                case "FFBK":
                    {
                        string file = cmd.StringValue;
                        int iters = cmd.Int32Value ?? 0;
                        float range = Flt0(cmd.Floats, 0);
                        switch (ctx.FieldName)
                        {
                            case "forceFeedbackFile": file = newValue; break;
                            case "iterations": iters = ParseInt(newValue); break;
                            case "range": range = ParseFloat(newValue); break;
                        }
                        return ClefFieldCodec.EncodeFfbk(file, iters, range);
                    }
                default:
                    throw new InvalidOperationException("Command '" + cmd.Tag + "' is not editable.");
            }
        }

        private static float Flt0(float[] a, int i) { return (a != null && i >= 0 && i < a.Length) ? a[i] : 0f; }
        private static float? NullableFlt(float[] a, int i, int minLen)
        {
            return (a != null && a.Length >= minLen && i < a.Length) ? (float?)a[i] : null;
        }
        private static float ParseFloat(string s) { return float.Parse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture); }
        private static int ParseInt(string s) { return int.Parse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture); }
        private static byte ParseByte(string s) { return byte.Parse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture); }
        private static bool ParseBool(string s)
        {
            string t = (s ?? "").Trim();
            return t == "1" || string.Equals(t, "true", StringComparison.OrdinalIgnoreCase);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Add / Remove / Reorder — structural mutations. Add uses the LOCKED ClefCommandDefaults (the SAME
        // constants the CLI --add-command + ClefFixtureBuilder use — REVIEWS MEDIUM #5). All are undoable
        // (full-snapshot undo) and re-derive StableIds afterward (the ordinal shifts).
        // ─────────────────────────────────────────────────────────────────────

        private void OnAddCommandClicked(object sender, EventArgs e)
        {
            if (effect == null || effect.IsRawPreserved) { SetStatus("Add command needs an editable effect.", true); return; }
            addMenu.Show(btnAdd, new Point(0, btnAdd.Height));
        }

        private void AddCommandOfTag(string tag)
        {
            if (effect == null || effect.IsRawPreserved) return;
            try
            {
                PushUndoSnapshot();
                // The locked default payload at the FILE'S existing version (D-03) — the single source the
                // CLI + fixtures use (REVIEWS MEDIUM #5).
                byte[] payload = ClefCommandDefaults.BuildDefaultPayload(tag, effect.Version);
                effect.AddCommand(tag, payload);
                MarkDirty();
                RefreshAfterMutation(reselectStableId: null);
                SetStatus("Added " + tag + " command.", false);
            }
            catch (Exception ex)
            {
                if (undoStack.Count > 0) undoStack.Pop();
                SetStatus("Add failed: " + ex.Message, true);
            }
        }

        private void OnRemoveCommandClicked(object sender, EventArgs e)
        {
            CommandRowRef row = SelectedRow();
            if (effect == null || effect.IsRawPreserved || row == null) { SetStatus("Select a command to remove.", true); return; }
            try
            {
                PushUndoSnapshot();
                effect.RemoveCommand(row.Leaf);
                MarkDirty();
                RefreshAfterMutation(reselectStableId: null);
                SetStatus("Removed " + (row.Command != null ? row.Command.Tag : "command") + ".", false);
            }
            catch (Exception ex)
            {
                if (undoStack.Count > 0) undoStack.Pop();
                SetStatus("Remove failed: " + ex.Message, true);
            }
        }

        private void MoveSelected(bool up)
        {
            CommandRowRef row = SelectedRow();
            if (effect == null || effect.IsRawPreserved || row == null) return;
            int currentIndex = listCommands.SelectedIndices.Count > 0 ? listCommands.SelectedIndices[0] : -1;
            try
            {
                PushUndoSnapshot();
                if (up) effect.ReorderCommandUp(row.Leaf); else effect.ReorderCommandDown(row.Leaf);
                MarkDirty();
                // Reselect the moved command at its NEW position so the row stays highlighted and visibly
                // jumps up/down (the reparse drops leaf identity, so we address it by index — exactly where
                // the move placed it). A boundary move is a no-op in the codec; clamping keeps the same row
                // selected. Count is unchanged by a reorder, so the pre-refresh Items.Count bounds it.
                int target = currentIndex < 0
                    ? -1
                    : Math.Max(0, Math.Min(listCommands.Items.Count - 1, up ? currentIndex - 1 : currentIndex + 1));
                RefreshAfterMutation(reselectIndex: target);
                SetStatus(up ? "Moved command up." : "Moved command down.", false);
            }
            catch (Exception ex)
            {
                if (undoStack.Count > 0) undoStack.Pop();
                SetStatus("Reorder failed: " + ex.Message, true);
            }
        }

        // Re-parses the in-memory CLEF from the held DOM so the command views + StableIds reflect the
        // current structure (ordinals shift on add/remove/reorder), repopulates the list, and reselects.
        private void RefreshAfterMutation(string reselectStableId = null, int reselectIndex = -1)
        {
            // Re-decode from the held bytes so the command views (and their re-derived StableIds) are fresh.
            byte[] current = effect.Serialize();
            MutableClientEffect reparsed = ClientEffectDocument.FromBytes(current);
            // Preserve provenance/name; swap in the fresh model.
            effect = reparsed;

            PopulateCommandList();

            // Reselect: by stable id (field edit — the command stays put, its ordinal id is stable), else by
            // index (reorder — leaf identity is gone after the reparse, so we address the moved row by its new
            // position), else leave cleared (add/remove).
            if (reselectStableId != null)
            {
                foreach (ListViewItem item in listCommands.Items)
                {
                    var r = item.Tag as CommandRowRef;
                    if (r != null && r.StableId == reselectStableId)
                    {
                        item.Selected = true;
                        item.Focused = true;
                        item.EnsureVisible();
                        break;
                    }
                }
            }
            else if (reselectIndex >= 0 && reselectIndex < listCommands.Items.Count)
            {
                ListViewItem item = listCommands.Items[reselectIndex];
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
            }
            RefreshActionButtonState();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Undo / Redo — full serialized-CLEF snapshots (CON-M-05: editor-local, independent of the scene
        // UndoRedoManager). Covers field edits AND add/remove/reorder uniformly.
        // ─────────────────────────────────────────────────────────────────────

        private void PushUndoSnapshot()
        {
            if (effect == null) return;
            undoStack.Push(effect.Serialize());
            redoStack.Clear();
        }

        private void DoUndo()
        {
            if (effect == null || undoStack.Count == 0) { return; }
            try
            {
                redoStack.Push(effect.Serialize());
                byte[] prev = undoStack.Pop();
                effect = ClientEffectDocument.FromBytes(prev);
                PopulateCommandList();
                isDirty = undoStack.Count > 0;
                RefreshDirtyMarker();
                RefreshActionButtonState();
                // D-04 undo seam — null-safe.
                editorPlugin?.Undo?.Invoke();
                SetStatus("Undo.", false);
            }
            catch (Exception ex)
            {
                SetStatus("Undo failed: " + ex.Message, true);
            }
        }

        private void DoRedo()
        {
            if (effect == null || redoStack.Count == 0) { return; }
            try
            {
                undoStack.Push(effect.Serialize());
                byte[] next = redoStack.Pop();
                effect = ClientEffectDocument.FromBytes(next);
                PopulateCommandList();
                MarkDirty();
                editorPlugin?.Redo?.Invoke();
                SetStatus("Redo.", false);
            }
            catch (Exception ex)
            {
                SetStatus("Redo failed: " + ex.Message, true);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Save — serialize MutableClientEffect.Serialize() and route through the IN-PROC
        // ClientEffectSaveTargets (NOT apply-save-effect — the parity test proves byte-convergence, REVIEWS
        // MEDIUM #9). Marshal the result back via BeginInvoke.
        // ─────────────────────────────────────────────────────────────────────

        private void OnSaveButtonClick(object sender, EventArgs e)
        {
            if (effect == null) { SetStatus("No effect loaded.", true); return; }
            saveMenu.Show(btnSave, new Point(0, btnSave.Height));
        }

        private async void OnSaveInPlaceClick(object sender, EventArgs e)
        {
            if (!GuardSaveable()) return;
            if (!(source is OpenSource.LooseFile))
            {
                SetStatus("In-place save is disabled for a read-only TRE source — use Save as loose override.", true);
                return;
            }
            byte[] bytes = effect.Serialize();
            ClientEffectSaveTargets.SaveResult result =
                await ClientEffectSaveTargets.SaveInPlace(bytes, source).ConfigureAwait(true);
            ApplySaveResult(result);
        }

        private async void OnSaveLooseOverrideClick(object sender, EventArgs e)
        {
            if (!GuardSaveable()) return;
            string root = ResolveClientRoot();
            if (string.IsNullOrEmpty(root))
            {
                SetStatus("Save failed: could not resolve the client root for the loose override.", true);
                return;
            }
            byte[] bytes = effect.Serialize();
            ClientEffectSaveTargets.SaveResult result =
                await ClientEffectSaveTargets.SaveLooseOverride(bytes, source, root, ResolveLooseOverrideSubDir())
                    .ConfigureAwait(true);
            ApplySaveResult(result);
        }

        private async void OnSaveAsClick(object sender, EventArgs e)
        {
            if (!GuardSaveable()) return;
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Save ClientEffect As…";
                sfd.Filter = ClefDialogFilter;
                sfd.FileName = displayName ?? "effect.cef";
                string initialDir = ResolveLooseOverrideDir();
                if (!string.IsNullOrEmpty(initialDir)) sfd.InitialDirectory = initialDir;
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                byte[] bytes = effect.Serialize();
                ClientEffectSaveTargets.SaveResult result =
                    await ClientEffectSaveTargets.SaveToPath(bytes, sfd.FileName).ConfigureAwait(true);
                if (result != null && result.Ok)
                {
                    source = new OpenSource.LooseFile(result.Path);
                    RefreshBanner();
                }
                ApplySaveResult(result);
            }
        }

        private bool GuardSaveable()
        {
            if (effect == null) { SetStatus("No effect loaded.", true); return false; }
            if (effect.IsRawPreserved)
            {
                SetStatus("This effect is raw-preserved (unrecognized version) — editing/save is refused.", true);
                return false;
            }
            return true;
        }

        private void ApplySaveResult(ClientEffectSaveTargets.SaveResult result)
        {
            if (result == null || !result.Ok)
            {
                string reason = result == null ? "unknown error" : result.Message;
                SetStatus("Save failed: " + reason + " — your edits are kept in the editor.", true);
                return;
            }
            lastSavedPath = result.Path;
            isDirty = false;
            RefreshDirtyMarker();
            // A successful save to a loose path makes the loose source the new in-place target.
            if (!(source is OpenSource.LooseFile))
            {
                source = new OpenSource.LooseFile(result.Path);
                RefreshBanner();
            }
            SetStatus("Saved → " + result.Path, false);
            // D-04 undo seam — null-safe (NULL until FormMain wires it).
            editorPlugin?.ClearUndoStack?.Invoke();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Preview in client — HONEST-CANDOR-ONLY (D-07, RESEARCH Pitfall 3). IsRetriggerHookReachable() is
        // hardcoded false this build; the button is ENABLED whenever a doc is open but performs NO retrigger.
        // The no-client and no-hook copy stay DISTINCT. The live-capable branch is kept in the seam (gated
        // behind the false reachability) for the future native hook (D-08).
        // ─────────────────────────────────────────────────────────────────────

        // Bucket B-2 (v8): the native .cef RE-PLAY hook is now wired. On the advertised client
        // ParticlePreview.IsReplayAvailable resolves particlePreview::replayClientEffect and returns true
        // once a scene is safe; on SWGEmu / pre-v8 it stays false and the editor keeps the honest degraded
        // candor. The P/Invoke can throw outside an injected client -> try/catch -> false (the form's pattern).
        private static bool IsRetriggerHookReachable()
        {
            try { return ParticlePreview.IsReplayAvailable; }
            catch { return false; }
        }

        // The .cef logical name the native replay hands ClientEffectManager::playClientEffect
        // (e.g. "clienteffect/foo.cef"). Prefers the TRE logical name captured at open; falls back to the
        // "clienteffect/…" tail of the saved/loose path; null -> the seam degrades (no wrong-name play).
        private string ResolveClientEffectLogicalName()
        {
            if (!string.IsNullOrEmpty(cefLogicalName))
            {
                return cefLogicalName.Replace('\\', '/');
            }

            string path = lastSavedPath;
            if (string.IsNullOrEmpty(path) && source is OpenSource.LooseFile lf) path = lf.Path;
            if (string.IsNullOrEmpty(path)) return null;

            string norm = path.Replace('\\', '/');
            int idx = norm.IndexOf("/clienteffect/", StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? norm.Substring(idx + 1) : null; // drop the leading slash
        }

        private void OnPreviewClicked(object sender, EventArgs e)
        {
            if (effect == null) { SetStatus("No effect loaded.", true); return; }

            bool clientUp = false;
            try { clientUp = Game.IsRunning; }
            catch { clientUp = false; } // P/Invoke can throw outside an injected client.

            if (!(clientUp && IsRetriggerHookReachable()))
            {
                // Honest degraded path — never over-promise, NO retrigger. Keep no-client distinct from
                // no-hook; dimmed styling reads as candor, not error.
                lblStatus.Text = clientUp ? PreviewNoHookTooltip : PreviewUnavailableTooltip;
                lblStatus.ForeColor = Colors.FontDisabled();
                return;
            }

            // Live-capable path (Bucket B-2 / v8): marshal the .cef re-play onto the game thread
            // (heap-free, once per preview — project_rh_snapshot_no_heap_alloc). The native seam re-fetches
            // the .cef + referenced templates (so the edit shows) and plays it fresh on the player; a
            // null/empty name degrades inside the seam (no wrong-name play).
            string cefName = ResolveClientEffectLogicalName();
            GameCallbacks.AddMainLoopCall(() =>
            {
                // P/Invoke into the injected client; never let a teardown race crash the editor UI.
                try { ParticlePreview.ReplayClientEffect(cefName); }
                catch { /* injected-client unavailable mid-call — honest no-op */ }
            });
            lblStatus.Text = PreviewLiveCapable;
            lblStatus.ForeColor = Colors.Font();
            PulseAccent();
        }

        private void PulseAccent()
        {
            if (lblBanner == null) return;
            var pulse = new Timer { Interval = 1000 };
            pulse.Tick += (s, ev) =>
            {
                pulse.Stop();
                pulse.Dispose();
            };
            pulse.Start();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Banner / status / dirty / button-state helpers.
        // ─────────────────────────────────────────────────────────────────────

        private void RefreshBanner()
        {
            if (lblBanner == null) return;
            string src = source is OpenSource.TreArchive ? "read-only TRE"
                : source is OpenSource.LooseFile ? "loose override" : "";
            string name = string.IsNullOrEmpty(displayName) ? "<unloaded>" : displayName;
            string ver = effect != null ? " (v" + effect.Version + ")" : "";
            lblBanner.Text = string.IsNullOrEmpty(src)
                ? "ClientEffect — " + name + ver
                : "ClientEffect — " + name + ver + "  (" + src + ")";
        }

        private void MarkDirty()
        {
            isDirty = true;
            RefreshDirtyMarker();
        }

        private void RefreshDirtyMarker()
        {
            if (lblDirty != null) lblDirty.Text = isDirty ? DirtyMarker : "";
        }

        private void RefreshActionButtonState()
        {
            bool loaded = effect != null;
            bool editable = loaded && !effect.IsRawPreserved;
            bool hasSel = SelectedRow() != null;
            if (btnSave != null) btnSave.Enabled = editable;
            if (btnUndo != null) btnUndo.Enabled = undoStack.Count > 0;
            if (btnRedo != null) btnRedo.Enabled = redoStack.Count > 0;
            if (btnAdd != null) btnAdd.Enabled = editable;
            if (btnRemove != null) btnRemove.Enabled = editable && hasSel;
            if (btnMoveUp != null) btnMoveUp.Enabled = editable && hasSel;
            if (btnMoveDown != null) btnMoveDown.Enabled = editable && hasSel;
            // Preview stays ENABLED whenever a doc is open (honest candor reachable by click — D-07).
            if (btnPreview != null) btnPreview.Enabled = loaded;
        }

        private void SetStatus(string text, bool isError)
        {
            if (lblStatus == null) return;
            lblStatus.Text = text ?? "";
            lblStatus.ForeColor = isError ? Color.Red : Colors.Font();
        }

        private void ShowEmptyState()
        {
            RefreshBanner();
            ShowGridMessage(EmptyBody, Colors.Font());
            if (lblStatus != null) { lblStatus.Text = EmptyHeading; lblStatus.ForeColor = Colors.Font(); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Root + subdir resolution (the same order FormTerrainEditor uses).
        // ─────────────────────────────────────────────────────────────────────

        private string ResolveLooseOverrideSubDir()
        {
            try
            {
                if (ini != null)
                {
                    string configured = ini.GetString(SettingsSection, "looseOverrideDir");
                    if (!string.IsNullOrEmpty(configured)) return configured;
                }
            }
            catch { /* ini may not have the key yet — fall through to the default */ }
            return ClientEffectSaveTargets.DefaultLooseSubDir;
        }

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
                if (ini != null)
                {
                    string configured = ini.GetString("TreBrowser", "clientDir");
                    if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured)) return configured;
                }
            }
            catch { /* ini may not have the key yet */ }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Settings + ini helpers.
        // ─────────────────────────────────────────────────────────────────────

        private void CreateSettings()
        {
            if (ini == null) return;
            ini.AddSetting(SettingsSection, "width", "980", UtINI.Value.Types.VtInt);
            ini.AddSetting(SettingsSection, "height", "640", UtINI.Value.Types.VtInt);
            ini.AddSetting(SettingsSection, "splitterDistance", "320", UtINI.Value.Types.VtInt);
            ini.AddSetting(SettingsSection, "looseOverrideDir", "", UtINI.Value.Types.VtString);
            ini.Load();
        }

        private int GetIniInt(string key, int fallback)
        {
            try
            {
                if (ini != null)
                {
                    int v = ini.GetInt(SettingsSection, key);
                    if (v > 0) return v;
                }
            }
            catch { /* fall through to the fallback */ }
            return fallback;
        }
    }
}
