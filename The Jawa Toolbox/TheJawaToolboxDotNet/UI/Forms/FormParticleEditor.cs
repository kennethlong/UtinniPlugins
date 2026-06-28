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
// Particle editor host (15-06, PROD-W2-PRT) — a resizable UtinniForm cloned in shape from
// FormObjectTemplateEditor (Phase 11). Renders a .prt (FORM PEFT) effect as an emitter tree (left,
// reusing IffChunkTree) + a typed param grid (right) with the D-05 honest hex fallback for greyed-out
// unknowns, the inherited Phase-8 Save▾ drop-down + provenance gating, editor-local undo/redo of leaf
// edits (CON-M-05: independent of the scene UndoRedoManager), an in-app Explain effect AI read-assist
// button reusing the SAME .prt CLI/MCP read path (D-07/D-08, read-assist ONLY), and a Preview in client
// hot-retrigger button (D-09) with honest live-capable-vs-degraded reload candor. Particle
// layout/semantics were studied from swg-client-v2 only (no code, comments, identifier names, or test
// fixtures copied from any reference source). Implementation original to Utinni under MIT.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using TJT.Saving;
using TJT.UI.Controls;
using UtinniCore.Utinni;
using UtinniCoreDotNet.Callbacks;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.Formats.Particle;
using UtinniCoreDotNet.Formats.Tre;
using UtinniCoreDotNet.PluginFramework;
using UtinniCoreDotNet.Saving;
using UtinniCoreDotNet.UI;
using UtinniCoreDotNet.UI.Controls;
using UtinniCoreDotNet.UI.Forms;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Forms
{
    /// <summary>
    /// Typed particle (.prt / FORM PEFT) editor window — a resizable <see cref="UtinniForm"/> registered
    /// ONCE via the plugin's <c>GetForms()</c> list (a Wave-2 editor; <c>GetSubPanels()</c> stays null,
    /// CON-M-01/02 NOT widened). Hosts an emitter tree (<see cref="IffChunkTree"/>) + a typed param grid
    /// (<see cref="ThemedDataGridView"/>) with the D-05 hex fallback, the inherited Save▾ + provenance
    /// gating, an <c>Explain effect</c> read-assist button (D-08, read-only), and a state-encoded
    /// <c>Preview in client</c> hot-retrigger (D-09) with honest reload candor. Clones the Phase 11
    /// <c>FormObjectTemplateEditor</c> shell.
    ///
    /// <para><b>Singleton hide-not-dispose:</b> <see cref="FormParticleEditor_FormClosing"/> delegates
    /// its CloseReason decision to <see cref="SingletonFormClosePolicy.ShouldHideInsteadOfDispose"/> —
    /// the Phase-8-smoke-locked pattern applied from commit 1 (mandatory for MEF-registered editor
    /// forms, D-03).</para>
    /// </summary>
    public partial class FormParticleEditor : UtinniForm, IEditorForm
    {
        private const string SettingsSection = "ParticleEditor";
        private const string BaseTitle = "Particle Editor";

        // LOCKED copy strings (UI-SPEC § Copywriting Particle + Reload Candor Contract). Kept as
        // constants so the grep gates can verify them verbatim and a future edit can't silently soften.
        private const string RawPreservedTooltip =
            "This field isn't typed yet — its original bytes are preserved exactly and saved unchanged.";
        private const string ReloadBadgeLiveCapable = "Re-triggers live instances on Preview.";
        private const string ReloadBadgeDegraded = "Reloads on next scene change or relog.";
        private const string PreviewUnavailableTooltip = "No live client — start SWG to preview in-scene.";
        // B6 (15-16): honest disabled-Preview reason for the running-but-no-hook case. Distinct from
        // PreviewUnavailableTooltip (no-client). Must NOT imply a reachable hook and stays consistent
        // with the LOCKED degraded reload-badge copy (ReloadBadgeDegraded "…next scene change or relog.").
        private const string PreviewNoHookTooltip =
            "Live preview isn't wired this build — edits show on the next scene change or relog.";
        private const string PatchLiveDisabledTooltip =
            "Live patch requires opening from client memory — not wired in this phase.";
        private const string BoundarySentence =
            "Utinni edits emitter, timing, and color parameters and swaps texture/mesh references — "
            + "authoring the referenced meshes or textures stays in Blender.";

        private readonly IEditorPlugin editorPlugin;
        private readonly UtINI ini;
        private readonly ToolTip toolTip = new ToolTip();

        // The typed mutable model. Null until LoadDocument binds a document.
        private MutableParticleEffect effect;

        // B4/B5 (15-16): the MutableIffNode the param grid is currently bound to (the emitter/leaf node
        // whose params are shown). RefreshMutable rebuilds the TreeView's TreeNode wrappers from the SAME
        // MutableIffDocument, so these model-node references stay valid across a refresh — letting
        // AfterModelMutated re-bind the grid for the live selection without a reselect. Null when nothing
        // is selected.
        private MutableIffNode currentParamNode;

        // Friendly display name + last-saved path for the loaded document.
        private string displayName;
        private string lastSavedPath;

        // B6 (Bucket B): the TRE-relative logical name of the loaded .prt (e.g.
        // "appearance/pt_smoke.prt") when opened from the TRE Browser. This is the name the
        // engine matches live particle instances by (Appearance::getAppearanceTemplateName()),
        // so it is what the Preview-in-client hot-retrigger hands the native seam. Null for
        // loose/unknown opens (then ResolveAppearanceLogicalName() falls back to the path).
        private string treLogicalName;

        // Save▾ drop-down state (modes 1/2/4 via ParticleSaveTargets; mode 3 disabled CF-03).
        private UtinniContextMenuStrip saveMenu;
        private ToolStripMenuItem miSaveInPlace;
        private ToolStripMenuItem miSaveLooseOverride;
        private ToolStripMenuItem miSaveAs;
        private ToolStripMenuItem miPatchLive;
        private ToolStripMenuItem miRepackTre;
        private bool saveInFlight;

        // Editor-local undo/redo of leaf-payload edits (CON-M-05: NOT the scene UndoRedoManager). Each
        // entry restores a single captured leaf to its prior bytes. Independent, editor-local, undoable.
        private sealed class LeafEdit
        {
            public MutableIffNode Leaf;
            public byte[] Before;
            public byte[] After;
        }
        private readonly Stack<LeafEdit> undoStack = new Stack<LeafEdit>();
        private readonly Stack<LeafEdit> redoStack = new Stack<LeafEdit>();
        private bool isDirty;

        private const int ColField = 0;
        private const int ColValue = 1;
        private const int ColType = 2;

        /// <summary>
        /// Provenance descriptor for the currently loaded document. The open paths set this to gate the
        /// Save▾ modes; defaults to <see cref="OpenSource.Unknown"/>.
        /// </summary>
        public OpenSource Source { get; set; }

        public FormParticleEditor(IEditorPlugin editorPlugin)
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

            Width = ini.GetInt(SettingsSection, "width");
            Height = ini.GetInt(SettingsSection, "height");

            // Theme via Colors.*() accessors only — Color.Red is the sole allowed raw literal (failure
            // emphasis), applied at runtime in the handlers.
            toolbar.BackColor = Colors.Primary();
            sep1.BackColor = Colors.Primary();
            sep2.BackColor = Colors.Primary();
            sep3.BackColor = Colors.Primary();
            pnlStatus.BackColor = Colors.Primary();
            pnlCounters.BackColor = Colors.Primary();
            pnlFooter.BackColor = Colors.Primary();
            lblBoundary.ForeColor = Colors.FontDisabled();
            lblBoundary.Text = BoundarySentence; // DEC-A3 / D-11 surfaced verbatim.
            lblDirty.ForeColor = Colors.Secondary(); // accent for the unsaved-changes marker
            lblStatus.ForeColor = Colors.Font();

            // The AI-assist results pane uses the Consolas-9pt monospace exception (UI-SPEC § Typography).
            txtAiResults.Font = new Font("Consolas", 9f);

            // Default provenance is Unknown. Open paths override.
            Source = OpenSource.Unknown.Instance;

            // Toolbar tooltips.
            toolTip.SetToolTip(btnOpen, "Open a particle effect (.prt).");
            toolTip.SetToolTip(btnSave, "Save the current effect (choose a target).");
            toolTip.SetToolTip(btnUndo, "Undo (Ctrl+Z)");
            toolTip.SetToolTip(btnRedo, "Redo (Ctrl+Y)");
            toolTip.SetToolTip(btnExplain, "Explain this effect (reads the file; never edits it).");
            toolTip.SetToolTip(btnPreview, PreviewUnavailableTooltip);
            toolTip.SetToolTip(btnReload, "State how/when the client re-resolves the edit.");

            // Open… is enabled from the start; the rest become enabled on LoadDocument.
            btnOpen.Click += OnOpenClicked;
            btnSave.Click += OnSaveButtonClick;
            btnUndo.Click += OnUndoClicked;
            btnRedo.Click += OnRedoClicked;
            btnExplain.Click += OnExplainClicked;
            btnPreview.Click += OnPreviewClicked;
            btnReload.Click += OnReloadClicked;

            BuildSaveMenu();

            ConfigureGridColumns();
            gridSurface.CellDoubleClick += OnCellDoubleClick;
            gridSurface.CellMouseDown += OnCellMouseDown;

            emitterTree.AfterSelect += OnTreeAfterSelect;

            SetTitle(null);
            UpdateDirtyVisuals();
            UpdateCounters();
            RefreshButtonsState();
        }

        // ── IEditorForm ──────────────────────────────────────────────────────

        public string GetName() { return this.Text; }

        public Form Create(IEditorPlugin plugin, List<Form> parentChildren)
        {
            foreach (Form form in parentChildren)
            {
                if (form.GetType() == typeof(FormParticleEditor))
                {
                    form.Activate();
                    return null;
                }
            }
            FormParticleEditor newForm = new FormParticleEditor(plugin);
            newForm.Show();
            parentChildren.Add(newForm);
            return newForm;
        }

        // ── Settings ─────────────────────────────────────────────────────────

        private void CreateSettings()
        {
            ini.AddSetting(SettingsSection, "width", "1100", UtINI.Value.Types.VtInt);
            ini.AddSetting(SettingsSection, "height", "760", UtINI.Value.Types.VtInt);
            ini.AddSetting(SettingsSection, "splitterDistance", "280", UtINI.Value.Types.VtInt);
            ini.AddSetting(SettingsSection, "looseOverrideDir", "", UtINI.Value.Types.VtString);
        }

        // ── Grid column configuration (Field · Value · Type) ─────────────────

        private void ConfigureGridColumns()
        {
            gridSurface.AllowUserToOrderColumns = false;
            gridSurface.AllowUserToResizeColumns = true;
            gridSurface.MultiSelect = false;
            gridSurface.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridSurface.ReadOnly = true; // typed inline editing is not wired this phase; raw edits via the hex sub-editor.

            var colField = new DataGridViewTextBoxColumn
            {
                Name = "Field",
                HeaderText = "Field",
                ReadOnly = true,
                FillWeight = 34,
            };
            var colValue = new DataGridViewTextBoxColumn
            {
                Name = "Value",
                HeaderText = "Value",
                ReadOnly = true,
                FillWeight = 50,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            };
            var colType = new DataGridViewTextBoxColumn
            {
                Name = "Type",
                HeaderText = "Type",
                ReadOnly = true,
                FillWeight = 16,
            };
            gridSurface.Columns.AddRange(new DataGridViewColumn[] { colField, colValue, colType });
        }

        // ── Open… ────────────────────────────────────────────────────────────

        private void OnOpenClicked(object sender, EventArgs e)
        {
            if (!ConfirmDiscardIfDirty("opening another file")) return;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Open particle effect…";
                ofd.Filter = "Particle effects (*.prt)|*.prt|All files (*.*)|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                OpenFromLooseFile(ofd.FileName);
            }
        }

        private void OpenFromLooseFile(string path)
        {
            lblStatus.Text = "Opening " + Path.GetFileName(path) + "…";
            lblStatus.ForeColor = Colors.Font();

            // Open/parse on a background Task; marshal UI mutations back (no modal spinner).
            string captured = path;
            Task.Run(() =>
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(captured);
                    MutableParticleEffect parsed = ParticleEffectDocument.FromBytes(bytes);
                    if (!IsHandleCreated) return;
                    BeginInvoke((Action)(() =>
                    {
                        LoadDocument(parsed, new OpenSource.LooseFile(captured), Path.GetFileName(captured));
                    }));
                }
                catch (Exception ex)
                {
                    if (!IsHandleCreated) return;
                    BeginInvoke((Action)(() => SetFailure("Open failed: " + ex.Message)));
                }
            });
        }

        /// <summary>
        /// Binds a typed <see cref="MutableParticleEffect"/> to the editor: the emitter tree (left) +
        /// the param grid (right). Resets the editor-local undo/redo + dirty state.
        /// </summary>
        public void LoadDocument(MutableParticleEffect doc, OpenSource source, string displayName)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            this.effect = doc;
            this.Source = source ?? OpenSource.Unknown.Instance;
            this.displayName = displayName;
            this.lastSavedPath = (source is OpenSource.LooseFile lf) ? lf.Path : null;
            // Reset the TRE logical name; OpenFromTreEntry re-sets it from the real logicalPath.
            this.treLogicalName = null;

            undoStack.Clear();
            redoStack.Clear();
            isDirty = false;

            lblEmptyState.Visible = false;
            emitterTree.LoadMutable(doc.SourceIff);
            gridSurface.Rows.Clear();

            lblReloadBadge.Visible = true;

            EnableDocumentControls();
            UpdateDirtyVisuals();
            UpdateCounters();
            RefreshButtonsState();

            if (doc.IsRawPreserved)
            {
                lblStatus.Text = "Opened " + (displayName ?? "effect")
                    + " — unrecognized version " + doc.Version + ", preserved as original bytes.";
            }
            else
            {
                lblStatus.Text = "Opened " + (displayName ?? "effect");
            }
            lblStatus.ForeColor = Colors.Font();
        }

        // ── TRE Browser hand-off ─────────────────────────────────────────────

        /// <summary>
        /// TRE Browser hand-off. Reads the resolved payload as a typed particle effect and binds it with
        /// TreArchive (or Unknown on degraded resolve) provenance — mirrors
        /// <c>FormObjectTemplateEditor.OpenFromTreEntry</c>. Visibility is gated by
        /// <see cref="ParticleHandoffPolicy"/> at the TRE Browser (HIDDEN when the sniff fails).
        /// </summary>
        public void OpenFromTreEntry(byte[] payload, string resolvedArchivePath, string logicalPath, long archiveLocalOffset)
        {
            if (payload == null)
            {
                SetFailure("TRE entry has no payload to open.");
                return;
            }
            if (!ConfirmDiscardIfDirty("opening another file")) return;
            try
            {
                MutableParticleEffect parsed = ParticleEffectDocument.FromBytes(payload);
                OpenSource src = TreRecordIndexResolver.ResolveOrUnknown(resolvedArchivePath, archiveLocalOffset, logicalPath);
                string name = logicalPath != null ? Path.GetFileName(logicalPath) : Path.GetFileName(resolvedArchivePath ?? "");
                LoadDocument(parsed, src, name);
                // Retain the full TRE-relative logical path for the live-retrigger match (LoadDocument
                // reset it to null above). This is the engine's appearance-template name for the .prt.
                this.treLogicalName = logicalPath;
            }
            catch (Exception ex)
            {
                SetFailure("TRE hand-off failed: " + ex.Message);
            }
        }

        // ── Emitter tree → param grid ────────────────────────────────────────

        private void OnTreeAfterSelect(object sender, TreeViewEventArgs e)
        {
            MutableIffNode node = (e != null && e.Node != null) ? e.Node.Tag as MutableIffNode : null;
            BindParamGrid(node);
        }

        // Populates the Field/Value/Type grid for the selected tree node. EMTR nodes show their typed
        // WaveForm/ColorRamp fields (the visible D-04 typed surface); leaf chunks show their raw bytes as
        // greyed-out Consolas hex with the LOCKED D-05 tooltip (the visible surface of degrade-don't-abort).
        private void BindParamGrid(MutableIffNode node)
        {
            currentParamNode = node; // B4/B5: remember the bound node so AfterModelMutated can re-bind it.
            gridSurface.SuspendLayout();
            try
            {
                gridSurface.Rows.Clear();
                if (node == null) return;

                if (node.Kind == MutableIffNodeKind.Container && node.SubTypeId == "EMTR")
                {
                    BindEmitterRows(node);
                    return;
                }

                if (node.Kind == MutableIffNodeKind.Leaf)
                {
                    BindRawLeafRow(node);
                    return;
                }

                // A non-EMTR container (PEFT / version form / EMGP / PTIM / WVFM / CLRR / PTQD …): show a
                // single summary row pointing the user at its children.
                int row = gridSurface.Rows.Add();
                gridSurface.Rows[row].Cells[ColField].Value = node.SubTypeId ?? node.TypeId;
                gridSurface.Rows[row].Cells[ColValue].Value = node.Children.Count + " child node(s)";
                gridSurface.Rows[row].Cells[ColType].Value = "form";
            }
            finally
            {
                gridSurface.ResumeLayout();
            }
        }

        private void BindEmitterRows(MutableIffNode emtr)
        {
            ParticleEmitterDescription view;
            try
            {
                view = ParticleEmitterDescription.FromEmtrNode(emtr);
            }
            catch
            {
                BindRawLeafRow(FirstLeaf(emtr));
                return;
            }

            if (view.IsRawPreserved)
            {
                int r = gridSurface.Rows.Add();
                var c = gridSurface.Rows[r];
                c.Cells[ColField].Value = "emitter (version " + view.Version + ")";
                c.Cells[ColValue].Value = "preserved as original bytes";
                c.Cells[ColType].Value = "raw bytes (hex)";
                MarkRawPreservedRow(c);
                return;
            }

            int wi = 0;
            foreach (ParticleFieldValue wf in view.WaveFormFields)
            {
                AddTypedFieldRow("waveform[" + wi + "]", wf);
                wi++;
            }
            int ci = 0;
            foreach (ParticleFieldValue cr in view.ColorRampFields)
            {
                AddTypedFieldRow("colorRamp[" + ci + "]", cr);
                ci++;
            }
            if (gridSurface.Rows.Count == 0)
            {
                int r = gridSurface.Rows.Add();
                gridSurface.Rows[r].Cells[ColField].Value = "emitter (version " + view.Version + ")";
                gridSurface.Rows[r].Cells[ColValue].Value = "no typed waveform/color fields";
                gridSurface.Rows[r].Cells[ColType].Value = "emitter";
            }
        }

        // Adds one typed-field row. WaveForm/ColorRamp render a read-only control-point summary
        // (UI-SPEC § value-cell states); raw-bytes fallback renders greyed Consolas hex + the LOCKED
        // tooltip. (Per-type inline editing of scalars is a future plan; this phase shows the typed
        // surface + the honest hex fallback, the visible surface of D-05.)
        private void AddTypedFieldRow(string fieldName, ParticleFieldValue value)
        {
            int r = gridSurface.Rows.Add();
            DataGridViewRow row = gridSurface.Rows[r];
            row.Cells[ColField].Value = fieldName;
            row.Cells[ColType].Value = value.FieldTypeLabel;
            row.Cells[ColValue].Value = DescribeValue(value);

            if (value.Kind == ParticleFieldKind.RawBytesHexFallback)
            {
                MarkRawPreservedRow(row);
            }
        }

        private void BindRawLeafRow(MutableIffNode leaf)
        {
            if (leaf == null) return;
            int r = gridSurface.Rows.Add();
            DataGridViewRow row = gridSurface.Rows[r];
            byte[] bytes = leaf.GetPayloadCopy();
            row.Cells[ColField].Value = leaf.TypeId;
            row.Cells[ColValue].Value = BytesToHex(bytes);
            row.Cells[ColType].Value = "raw bytes (hex)";
            row.Tag = leaf; // double-click opens the hex sub-editor against this leaf.
            MarkRawPreservedRow(row);
        }

        // Greys a row to the D-05 degrade style: Consolas 9pt, FontDisabled() foreground, read-only, with
        // the LOCKED "preserved as original bytes" tooltip on every cell.
        private void MarkRawPreservedRow(DataGridViewRow row)
        {
            foreach (DataGridViewCell cell in row.Cells)
            {
                cell.Style.ForeColor = Colors.FontDisabled();
                cell.Style.Font = new Font("Consolas", 9f);
                cell.ReadOnly = true;
                cell.ToolTipText = RawPreservedTooltip;
            }
        }

        private static string DescribeValue(ParticleFieldValue value)
        {
            switch (value.Kind)
            {
                case ParticleFieldKind.Float:
                    return value.FloatValue.ToString("0.######", CultureInfo.InvariantCulture);
                case ParticleFieldKind.Int:
                    return value.IntValue.ToString(CultureInfo.InvariantCulture);
                case ParticleFieldKind.Bool:
                    return value.BoolValue ? "true" : "false";
                case ParticleFieldKind.Enum:
                    return value.EnumValue.ToString(CultureInfo.InvariantCulture);
                case ParticleFieldKind.WaveForm:
                    return value.WaveForm.ControlPoints.Count + " control points";
                case ParticleFieldKind.ColorRamp:
                    return value.ColorRamp.ControlPoints.Count + " control points";
                case ParticleFieldKind.RawBytesHexFallback:
                    return BytesToHex(value.GetRawBytesCopy());
                default:
                    return "";
            }
        }

        private static string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            int shown = Math.Min(bytes.Length, 256);
            var sb = new System.Text.StringBuilder(shown * 2);
            for (int i = 0; i < shown; i++) sb.Append(bytes[i].ToString("x2"));
            if (bytes.Length > shown) sb.Append("… (" + bytes.Length + " bytes)");
            return sb.ToString();
        }

        private static MutableIffNode FirstLeaf(MutableIffNode parent)
        {
            if (parent == null) return null;
            foreach (MutableIffNode child in parent.Children)
            {
                if (child.Kind == MutableIffNodeKind.Leaf) return child;
            }
            return null;
        }

        // ── Raw-leaf hex edit (routed through EditLeafPayload, editor-local undoable) ──

        private void OnCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            gridSurface.ClearSelection();
            gridSurface.Rows[e.RowIndex].Selected = true;
        }

        private void OnCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || effect == null || effect.IsRawPreserved) return;
            MutableIffNode leaf = gridSurface.Rows[e.RowIndex].Tag as MutableIffNode;
            if (leaf == null || leaf.Kind != MutableIffNodeKind.Leaf) return;

            byte[] before = leaf.GetPayloadCopy();
            using (var dlg = new FormParamHexEditor(leaf.TypeId, before))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.ResultBytes == null) return;
                byte[] after = dlg.ResultBytes;
                ApplyLeafEdit(new LeafEdit { Leaf = leaf, Before = before, After = after });
            }
        }

        private void ApplyLeafEdit(LeafEdit edit)
        {
            try
            {
                effect.EditLeafPayload(edit.Leaf, edit.After);
            }
            catch (Exception ex)
            {
                SetFailure("Edit refused: " + ex.Message);
                return;
            }
            undoStack.Push(edit);
            redoStack.Clear();
            isDirty = true;
            AfterModelMutated();
            lblStatus.Text = "Edited " + edit.Leaf.TypeId + " (" + edit.After.Length + " bytes).";
            lblStatus.ForeColor = Colors.Font();
        }

        private void AfterModelMutated()
        {
            emitterTree.RefreshMutable(effect.SourceIff);
            // B4/B5 (15-16): RefreshMutable only rebuilt the tree's TreeNode wrappers (and reset the
            // TreeView selection). Re-bind the param grid to the node it was showing so the edited
            // leaf's cell re-renders with the new bytes immediately — no reselect needed. The model
            // MutableIffNode reference survives the refresh (same MutableIffDocument), and the leaf
            // edited belongs to (or IS) that node. BindParamGrid re-derives currentParamNode itself.
            if (currentParamNode != null)
            {
                BindParamGrid(currentParamNode);
            }
            UpdateDirtyVisuals();
            UpdateCounters();
            RefreshButtonsState();
        }

        // ── Undo / Redo (editor-local; CON-M-05 — NOT the scene UndoRedoManager) ──

        private void OnUndoClicked(object sender, EventArgs e) { DoUndo(); }
        private void OnRedoClicked(object sender, EventArgs e) { DoRedo(); }

        private void DoUndo()
        {
            if (effect == null || undoStack.Count == 0) return;
            LeafEdit edit = undoStack.Pop();
            try { effect.EditLeafPayload(edit.Leaf, edit.Before); }
            catch { /* defensive */ }
            redoStack.Push(edit);
            isDirty = undoStack.Count > 0;
            AfterModelMutated();
        }

        private void DoRedo()
        {
            if (effect == null || redoStack.Count == 0) return;
            LeafEdit edit = redoStack.Pop();
            try { effect.EditLeafPayload(edit.Leaf, edit.After); }
            catch { /* defensive */ }
            undoStack.Push(edit);
            isDirty = true;
            AfterModelMutated();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z)) { DoUndo(); return true; }
            if (keyData == (Keys.Control | Keys.Y)) { DoRedo(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ── Explain effect (AI read-assist, D-07/D-08 — read-only; reuses the 15-04 read path) ──

        private async void OnExplainClicked(object sender, EventArgs e)
        {
            if (effect == null) return;

            // The read path reads a FILE. Resolve the .prt path: the last-saved path, or the
            // open-from-loose path. (Read-assist ONLY — this never writes the file.)
            string path = lastSavedPath;
            if (string.IsNullOrEmpty(path) && Source is OpenSource.LooseFile lf) path = lf.Path;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                txtAiResults.ForeColor = Color.Red;
                txtAiResults.Text = "Couldn't read this effect — save it to a file first, then try again.";
                return;
            }

            btnExplain.Enabled = false;
            lblStatus.Text = "Reading effect…";
            lblStatus.ForeColor = Colors.Font();
            txtAiResults.ForeColor = Colors.Font();
            txtAiResults.Text = "Reading effect…";

            ParticleReadAssistResult result = await ParticleReadAssist.ExplainAsync(path).ConfigureAwait(true);

            if (result.Ok)
            {
                txtAiResults.ForeColor = Colors.Font();
                txtAiResults.Text = result.Text;
                lblStatus.Text = "Read effect.";
                lblStatus.ForeColor = Colors.Font();
            }
            else
            {
                txtAiResults.ForeColor = Color.Red;
                txtAiResults.Text = result.Text; // already "Couldn't read this effect — {reason}."
                lblStatus.Text = result.Text;
                lblStatus.ForeColor = Color.Red;
            }
            btnExplain.Enabled = effect != null;
        }

        // ── Preview in client (D-09 hot-retrigger — state-encoded, honest candor) ──

        // Bucket B (Phase 24 / v7): the native hot-retrigger hook is now wired. On the advertised
        // client the ParticlePreview seam resolves particlePreview::retrigger and IsRetriggerAvailable
        // returns true once a scene is safe to touch; on SWGEmu / pre-v7 it stays false and the reload
        // badge degrades to the LOCKED tier-(b) candor. This single seam means the button logic below is
        // unchanged — it just reads the real native predicate now.
        private static bool IsRetriggerHookReachable()
        {
            // The native predicate (ParticlePreview.IsRetriggerAvailable) is a P/Invoke into UtinniCore.dll
            // that throws when TJT runs without an injected client; mirror the defensive Game.IsRunning
            // pattern used throughout this form and degrade to false on any failure.
            try { return ParticlePreview.IsRetriggerAvailable; }
            catch { return false; }
        }

        // The appearance-template name the engine matches live particle instances by
        // (Appearance::getAppearanceTemplateName(), e.g. "appearance/pt_smoke.prt"). Prefers the TRE
        // logical path captured at open (the realistic TRE-Browser path); falls back to extracting the
        // "appearance/…" tail from the saved/loose path; returns null when neither is available, in which
        // case the native seam degrades honestly (NotReachable) rather than retriggering the wrong name.
        private string ResolveAppearanceLogicalName()
        {
            if (!string.IsNullOrEmpty(treLogicalName))
            {
                return treLogicalName.Replace('\\', '/');
            }

            string path = lastSavedPath;
            if (string.IsNullOrEmpty(path) && Source is OpenSource.LooseFile lf) path = lf.Path;
            if (string.IsNullOrEmpty(path)) return null;

            string norm = path.Replace('\\', '/');
            int idx = norm.IndexOf("/appearance/", StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? norm.Substring(idx + 1) : null; // drop the leading slash
        }

        private bool PreviewAvailable()
        {
            bool clientUp = false;
            try { clientUp = Game.IsRunning; }
            catch { clientUp = false; }
            return clientUp && IsRetriggerHookReachable();
        }

        private void OnPreviewClicked(object sender, EventArgs e)
        {
            if (effect == null) return;

            // B6 (15-19): the button is now ENABLED whenever a doc is open (RefreshButtonsState gates on
            // hasDoc only), because WinForms never renders a ToolTip over a disabled control — so the
            // honest no-hook reason was unreachable. Branch here on the actual reachability: when the
            // native hot-retrigger hook is reachable AND the client is up, run the real path; otherwise
            // surface the LOCKED degraded candor (reachable now by CLICK, and by hover via the tooltip),
            // performing NO retrigger and NEVER implying a live hook exists (T-15-19-01).
            bool clientUp = false;
            try { clientUp = Game.IsRunning; }
            catch { clientUp = false; }

            if (!(clientUp && IsRetriggerHookReachable()))
            {
                // Honest degraded path (never over-promise). Keep no-client distinct from no-hook, and use
                // the dimmed/informational styling so it reads as candor, not an error. No retrigger.
                lblStatus.Text = clientUp ? PreviewNoHookTooltip : PreviewUnavailableTooltip;
                lblStatus.ForeColor = Colors.FontDisabled();
                return;
            }

            // Live-capable path (Bucket B / v7): marshal the retrigger onto the game thread, heap-free per
            // project_rh_snapshot_no_heap_alloc (once per preview, never per frame). The native seam walks
            // ClientEffectManager::m_particleSystems and restarts instances whose appearance-template name
            // matches; a null/empty name degrades to NotReachable inside the seam (no wrong-name retrigger).
            string logicalName = ResolveAppearanceLogicalName();
            GameCallbacks.AddMainLoopCall(() =>
            {
                // P/Invoke into the injected client; never let a teardown race crash the editor UI.
                try { ParticlePreview.RetriggerLiveEffectInstances(logicalName); }
                catch { /* injected-client unavailable mid-call — honest no-op */ }
            });
            lblStatus.Text = "Re-triggered live instance(s).";
            lblStatus.ForeColor = Colors.Font();
            PulseReloadBadge();
        }

        private void PulseReloadBadge()
        {
            lblReloadBadge.ForeColor = Colors.Secondary();
            var pulse = new Timer { Interval = 1000 };
            pulse.Tick += (s, ev) =>
            {
                lblReloadBadge.ForeColor = Colors.Font();
                pulse.Stop();
                pulse.Dispose();
            };
            pulse.Start();
        }

        // ── Reload candor (the editor STATES the reload; it never triggers one this phase) ──

        private void OnReloadClicked(object sender, EventArgs e)
        {
            if (PreviewAvailable())
            {
                lblReloadBadge.Text = ReloadBadgeLiveCapable;
                lblStatus.Text = "Use Preview in client to re-trigger live instances.";
            }
            else
            {
                lblReloadBadge.Text = ReloadBadgeDegraded;
                lblStatus.Text = ReloadBadgeDegraded;
            }
            lblStatus.ForeColor = Colors.Font();
        }

        // ── Save▾ drop-down (modes 1/2/4 via ParticleSaveTargets; mode 3 disabled CF-03) ──

        private void BuildSaveMenu()
        {
            saveMenu = new UtinniContextMenuStrip();
            miSaveInPlace = new ToolStripMenuItem("Save (in place)");
            miSaveInPlace.Click += OnSaveInPlaceClick;
            miSaveLooseOverride = new ToolStripMenuItem("Save as loose override");
            miSaveLooseOverride.Click += OnSaveLooseOverrideClick;
            miSaveAs = new ToolStripMenuItem("Save As…");
            miSaveAs.Click += OnSaveAsClick;
            miPatchLive = new ToolStripMenuItem("Patch live client (in memory)");
            miPatchLive.Enabled = false; // CF-03 — Mode 3 disabled.
            miPatchLive.ToolTipText = PatchLiveDisabledTooltip;
            miPatchLive.Click += (s, e) => SetFailure("Live patch is disabled in this phase.");
            miRepackTre = new ToolStripMenuItem("Repack into source .tre…");
            miRepackTre.Enabled = false;
            miRepackTre.ToolTipText = "Open from a packed .tre to repack the source archive.";
            miRepackTre.Click += OnRepackTreClick;
            saveMenu.Items.AddRange(new ToolStripItem[]
            {
                miSaveInPlace, miSaveLooseOverride, miSaveAs,
                new ToolStripSeparator(),
                miPatchLive, miRepackTre,
            });
        }

        private void OnSaveButtonClick(object sender, EventArgs e)
        {
            if (saveMenu == null) return;
            saveMenu.Show(btnSave, new Point(0, btnSave.Height));
        }

        private void RefreshSaveMenuEnabledState()
        {
            bool hasDoc = effect != null;
            bool isLooseFile = Source is OpenSource.LooseFile;
            bool isTreArchive = Source is OpenSource.TreArchive;
            bool isUnknown = Source is OpenSource.Unknown;

            if (miSaveInPlace != null)
            {
                miSaveInPlace.Enabled = hasDoc && isLooseFile && !saveInFlight;
                miSaveInPlace.ToolTipText = isLooseFile
                    ? ""
                    : "Cannot save in place — file came from .tre or unknown source. Use Save as loose override or Save As.";
            }
            if (miSaveLooseOverride != null)
            {
                miSaveLooseOverride.Enabled = hasDoc && (isLooseFile || isTreArchive) && !saveInFlight;
            }
            if (miSaveAs != null)
            {
                miSaveAs.Enabled = hasDoc && !saveInFlight;
            }
            if (miPatchLive != null)
            {
                miPatchLive.Enabled = false; // CF-03.
                miPatchLive.ToolTipText = PatchLiveDisabledTooltip;
            }
            if (miRepackTre != null)
            {
                miRepackTre.Enabled = hasDoc && isTreArchive && !saveInFlight;
            }

            btnSave.Enabled = hasDoc && !saveInFlight && (isLooseFile || isTreArchive || isUnknown);
        }

        private async void OnSaveInPlaceClick(object sender, EventArgs e)
        {
            if (effect == null) return;
            if (!(Source is OpenSource.LooseFile))
            {
                SetFailure("Cannot save in place — file came from .tre or unknown source. Use Save as loose override or Save As.");
                return;
            }
            await DoFileSaveAsync(() => ParticleSaveTargets.SaveInPlace(effect, Source), "in place");
        }

        private async void OnSaveLooseOverrideClick(object sender, EventArgs e)
        {
            if (effect == null) return;
            string clientRoot = ResolveClientRoot();
            if (string.IsNullOrEmpty(clientRoot))
            {
                SetFailure("Could not locate the client root — use Save As… and we'll remember the directory.");
                return;
            }
            string subDir = ini.GetString(SettingsSection, "looseOverrideDir");
            if (string.IsNullOrEmpty(subDir)) subDir = "loose";
            await DoFileSaveAsync(
                () => ParticleSaveTargets.SaveLooseOverride(effect, Source, clientRoot, subDir),
                "loose override");
        }

        private async void OnSaveAsClick(object sender, EventArgs e)
        {
            if (effect == null) return;
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Save particle effect as…";
                sfd.Filter = "Particle effects (*.prt)|*.prt|All files (*.*)|*.*";
                sfd.FileName = !string.IsNullOrEmpty(displayName) ? displayName : "untitled.prt";
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                string path = sfd.FileName;
                await DoFileSaveAsync(() => ParticleSaveTargets.SaveToPath(effect, path), "save-as");
            }
        }

        private async void OnRepackTreClick(object sender, EventArgs e)
        {
            if (effect == null) return;
            var ta = Source as OpenSource.TreArchive;
            if (ta == null)
            {
                SetFailure("Open from a packed .tre to repack the source archive.");
                return;
            }

            string archiveName = Path.GetFileName(ta.TrePath ?? "");
            bool backupRequested;
            using (var dlg = new FormSaveConfirmDialog(
                heading: "Repack " + archiveName + "?",
                body: "This rewrites the entire " + archiveName + " archive on disk and replaces it "
                    + "atomically. Untouched entries are preserved byte-for-byte; only the edited entry "
                    + "recompresses. If the client holds the archive open, the repack is refused without "
                    + "a partial-write. Continue?",
                acceptVerb: "Repack",
                cancelVerb: "Cancel",
                showBackupCheckbox: true,
                backupCheckboxLabel: "Create a timestamped backup (" + archiveName + ".<yyyyMMdd-HHmmss>.bak) first"))
            {
                dlg.ShowDialog(this);
                if (dlg.Outcome != FormSaveConfirmDialog.ConfirmOutcome.Accepted) return;
                backupRequested = dlg.BackupRequested;
            }

            saveInFlight = true;
            RefreshButtonsState();
            lblStatus.Text = "Saving (repack " + archiveName + ")…";
            lblStatus.ForeColor = Colors.Font();

            TreRepackSaveTarget.TreRepackResult result;
            try
            {
                result = await ParticleSaveTargets.RepackIntoSourceTre(effect, ta, backupRequested).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetFailure("Repack failed: " + ex.Message + " Your edits are kept in the editor.");
                saveInFlight = false;
                RefreshButtonsState();
                return;
            }

            saveInFlight = false;
            switch (result)
            {
                case TreRepackSaveTarget.TreRepackResult.Replaced:
                case TreRepackSaveTarget.TreRepackResult.BackedUpThenReplaced:
                    lastSavedPath = ta.TrePath;
                    lblStatus.Text = result == TreRepackSaveTarget.TreRepackResult.BackedUpThenReplaced
                        ? "Repacked " + archiveName + " (backup created)"
                        : "Repacked " + archiveName;
                    lblStatus.ForeColor = Colors.Font();
                    isDirty = false;
                    UpdateDirtyVisuals();
                    break;

                case TreRepackSaveTarget.TreRepackResult.RefusedClientHoldsArchive_LooseOverrideRecommended:
                    lblStatus.Text = "Client holds the archive — try Save as loose override instead.";
                    lblStatus.ForeColor = Color.Red;
                    break;

                default:
                    lblStatus.Text = "Repack failed — your edits are retained. If this is a V6000 (encrypted) archive, use Save as loose override.";
                    lblStatus.ForeColor = Color.Red;
                    break;
            }
            RefreshButtonsState();
        }

        private async Task<bool> DoFileSaveAsync(Func<Task<IffSaveTargets.SaveResult>> saveOp, string modeLabel)
        {
            saveInFlight = true;
            RefreshButtonsState();
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
                isDirty = false;
                UpdateDirtyVisuals();
            }
            else
            {
                lblStatus.Text = (result.Message ?? "Save failed.") + " Your edits are kept in the editor — try another save target.";
                lblStatus.ForeColor = Color.Red;
            }
            RefreshButtonsState();
            return result.Ok;
        }

        // Resolves the client install root (process module, GetWorkingDirectory(), then [TreBrowser]
        // clientDir ini fallback) — mirrors FormObjectTemplateEditor.ResolveClientRoot.
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
                string configured = ini.GetString("TreBrowser", "clientDir");
                if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured)) return configured;
            }
            catch { /* ini may not have the key yet */ }
            return null;
        }

        // ── Enabled-state + visuals ──────────────────────────────────────────

        private void EnableDocumentControls()
        {
            RefreshButtonsState();
        }

        private void RefreshButtonsState()
        {
            bool hasDoc = effect != null;
            btnUndo.Enabled = hasDoc && undoStack.Count > 0;
            btnRedo.Enabled = hasDoc && redoStack.Count > 0;
            btnExplain.Enabled = hasDoc;

            bool clientUp = false;
            try { clientUp = Game.IsRunning; }
            catch { clientUp = false; }

            bool previewAvail = hasDoc && PreviewAvailable();
            // B6 (15-19): keep Preview ENABLED whenever a doc is open — NOT gated on clientUp/hookReachable.
            // WinForms does not render a ToolTip over a disabled control, so the honest no-hook/no-client
            // reason was unreachable when the button was disabled (15-18 live defect). Enabling the button
            // makes the candor reachable on BOTH hover (tooltip below) and click (OnPreviewClicked surfaces
            // the same LOCKED copy via lblStatus). OnPreviewClicked still performs NO retrigger unless the
            // hook is genuinely reachable, so this never over-promises a live hook (T-15-19-01).
            btnPreview.Enabled = hasDoc;
            // When the live retrigger is available -> the live-capable hint; running-but-no-hook (the actual
            // state this phase) -> the no-hook copy that does not imply a hook exists and matches the LOCKED
            // degraded reload-badge wording; no client -> the no-client copy. no-client stays distinct from
            // no-hook.
            toolTip.SetToolTip(btnPreview, previewAvail
                ? "Re-trigger this effect's live instances in the client."
                : (clientUp ? PreviewNoHookTooltip : PreviewUnavailableTooltip));
            btnReload.Enabled = hasDoc && clientUp && !saveInFlight;

            // Honest reload badge: live-capable copy ONLY when injected + hook reachable; else degraded.
            lblReloadBadge.Text = previewAvail ? ReloadBadgeLiveCapable : ReloadBadgeDegraded;
            lblReloadBadge.Visible = hasDoc;

            RefreshSaveMenuEnabledState();
        }

        private void SetTitle(string prefix)
        {
            this.Text = string.IsNullOrEmpty(prefix) ? BaseTitle : prefix + " " + BaseTitle;
            this.Invalidate();
        }

        private void UpdateDirtyVisuals()
        {
            lblDirty.Text = isDirty ? "Unsaved changes" : "";
            SetTitle(isDirty ? "●" : null);
        }

        private void UpdateCounters()
        {
            if (effect == null)
            {
                lblCounters.Text = "0 groups · 0 emitters · 0 raw-preserved";
                lblCounters.ForeColor = Colors.FontDisabled();
                return;
            }

            int groups = effect.Groups.Count;
            int emitters = 0;
            int rawPreserved = effect.IsRawPreserved ? 1 : 0;
            foreach (MutableParticleEmitterGroup g in effect.Groups)
            {
                foreach (ParticleEmitterDescription emt in g.Emitters)
                {
                    emitters++;
                    if (emt.IsRawPreserved) rawPreserved++;
                }
            }
            lblCounters.Text = groups + " groups · " + emitters + " emitters · " + rawPreserved + " raw-preserved";
            lblCounters.ForeColor = rawPreserved > 0 ? Colors.Font() : Colors.FontDisabled();
        }

        private void SetFailure(string message)
        {
            lblStatus.Text = message;
            lblStatus.ForeColor = Color.Red;
        }

        // Routes a dirty-discard through the shipped Phase-8 confirm modal before a destructive view swap.
        private bool ConfirmDiscardIfDirty(string actionLabel)
        {
            if (!isDirty) return true;
            using (var dlg = new FormSaveConfirmDialog(
                heading: "Discard unsaved changes?",
                body: (displayName ?? "This effect") + " has unsaved edits. Save before " + actionLabel + "?",
                acceptVerb: "Discard",
                cancelVerb: "Cancel",
                showBackupCheckbox: false,
                backupCheckboxLabel: null))
            {
                dlg.ShowDialog(this);
                return dlg.Outcome == FormSaveConfirmDialog.ConfirmOutcome.Accepted;
            }
        }

        // ── Singleton hide-not-dispose (MANDATORY FROM COMMIT 1, D-03) ───────

        private void FormParticleEditor_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                ini.AddSetting(SettingsSection, "width", Width.ToString(), UtINI.Value.Types.VtInt);
                ini.AddSetting(SettingsSection, "height", Height.ToString(), UtINI.Value.Types.VtInt);
                if (mainSplit != null)
                {
                    ini.AddSetting(SettingsSection, "splitterDistance", mainSplit.SplitterDistance.ToString(), UtINI.Value.Types.VtInt);
                }
                ini.Save();
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
    }
}
