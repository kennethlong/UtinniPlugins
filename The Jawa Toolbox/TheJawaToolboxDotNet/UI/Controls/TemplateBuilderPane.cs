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

// Phase 23 (23-07) — the Tier-B in-place hex-driven template BUILDER pane (D-02), the heart of the
// phase. It is the fourth leaf-pane MODE of FormIffEditor.pnlLeafEditor (DEC-C4 — inside The Jawa
// Toolbox). The user selects a byte range in the raw-byte grid -> assigns a type+name -> the template
// grows, the decode preview updates live, a byte-exact round-trip indicator runs continuously, and
// un-annotated bytes stay visibly raw.
//
// IMPORTANT (DEC-V2 / D-16): this pane is UI-ONLY. It owns NO format logic. Every decode / fit-check is
// delegated to the headless engine in UtinniCoreDotNet.Formats.Template (KernelCodec / FitChecker /
// TemplateResolver). The template JSON it builds is the SAME D-01 artifact the verbs emit. This is the
// legitimate non-exception to DEC-V2-VERBS-FIRST: an interactive authoring gesture is an interaction,
// not a batch capability.
//
// Pitfall 8 (LOCKED — [[feedback_winforms_dockfill_zorder]] + [[feedback_caller_attrs_binary_compat]]):
//   - every Dock.Fill child is added to its parent FIRST (front-most); docked siblings added after.
//   - the nested SplitContainer sets Size BEFORE SplitterDistance (a fresh SplitContainer is 150px;
//     setting a larger distance first throws InvalidOperationException, which from a ctor would fail the
//     whole plugin's MEF load).
//   - the ctor MUST NOT throw — defaults survive a bad ini; the splitter restore is guarded + try/catch.
//
// 23-08 (PROD-IFFT-03 manage half) extends this pane with the pack-management surface: a "Save▾" menu on
// the status strip that saves the in-progress template into a WRITABLE pack (the shipped pack is shown
// disabled — read-only), selects among scanned packs, and imports/exports a template pack/template. Save
// serializes the current TemplateModel via TemplateJson into the chosen pack dir, resolving the write path
// WITHIN the pack root (LooseOverridePath containment — threat T-23-08-PATH); overwrite + delete route
// through FormSaveConfirmDialog (threat T-23-08-DESTRUCT). Applying/editing stay non-destructive — nothing
// touches disk until an explicit Save▾.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using TJT.UI.Forms;
using UtinniCoreDotNet.Formats.Template;
using UtinniCoreDotNet.Saving;
using UtinniCoreDotNet.UI.Controls;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Controls
{
    /// <summary>
    /// The Tier-B hex-driven template builder pane: a raw-byte monospace grid (top) + the decoded
    /// FieldRecord list (bottom) + a live byte-exact round-trip indicator (status strip). It holds an
    /// in-progress <see cref="TemplateModel"/> and re-runs the headless <see cref="KernelCodec.Decode"/>
    /// + <see cref="FitChecker.FitCheck"/> on every change. It NEVER decodes/encodes bytes itself — all
    /// format logic lives in the kernel (23-03).
    ///
    /// <para>Task 2 (FormIffEditor wiring) drives the pane: it sets the bound payload via
    /// <see cref="SetPayload"/>, installs the assign-field context menu on <see cref="ByteGrid"/>, reads
    /// <see cref="SelectionStart"/>/<see cref="SelectionLength"/> for the select-&gt;assign gesture, and
    /// appends/edits fields via <see cref="AppendField"/> / <see cref="ReplaceFields"/>. The pane raises
    /// <see cref="FieldValueCommitRequested"/> when an inline value edit should drive a document edit
    /// through the host's IffEditController.</para>
    /// </summary>
    public sealed class TemplateBuilderPane : UserControl
    {
        // ── child controls (Pitfall-8 ordered) ───────────────────────────────────
        private readonly Panel pnlTemplate;          // Dock.Fill (front-most), added FIRST
        private readonly Panel pnlTemplateStatus;    // Dock.Bottom, 22px
        private readonly SplitContainer splitTemplate; // nested, Orientation=Horizontal
        private readonly TextBox txtBytes;           // TOP — raw-byte monospace grid
        private readonly ListView lvFields;          // BOTTOM — decoded FieldRecord rows
        private readonly Label lblTemplateStatus;    // the live round-trip indicator
        private readonly UtinniButton btnSaveTemplate; // 23-08 — the "Save▾" pack-management drop-down

        // ── the in-progress template + its bound payload (UI state only) ──────────
        private TemplateModel template;
        private byte[] payload = new byte[0];

        // 23-08: optional project-local pack dir (the open mod's own pack) — null until the host sets it.
        private string projectLocalDir;

        // Per-line byte offsets so a TextBox selection (caret index) maps back to a byte index. Index i
        // holds the byte offset at the start of display line i; ColZeroByte/CharsPerByte size the hex cols.
        private const int BytesPerLine = 16;
        private const int OffsetColWidth = 10; // "XXXXXXXX:  " => 8 hex + ':' + two spaces

        /// <summary>The VS-style status green — the ONE new sanctioned direct color (UI-SPEC §Color).</summary>
        private static readonly Color StatusGreen = Color.FromArgb(78, 201, 76);

        /// <summary>Raised when a row's value is edited inline and should be committed as a document edit
        /// (the host re-encodes via <see cref="KernelCodec.Encode"/> and applies through IffEditController).
        /// Carries the field name + the new text the user typed.</summary>
        public event EventHandler<FieldValueCommitEventArgs> FieldValueCommitRequested;

        /// <summary>23-08: raised when the user picks a template from the "Select a template…" list. The
        /// host clones it onto the current leaf (the same auto-apply path the resolver takes) so applying a
        /// template stays non-destructive (undoable; nothing on disk until an explicit Save▾). Carries the
        /// chosen <see cref="TemplateModel"/>.</summary>
        public event EventHandler<TemplateSelectedEventArgs> TemplateSelected;

        public TemplateBuilderPane()
        {
            // The UserControl itself fills its mount point.
            Dock = DockStyle.Fill;
            BackColor = Colors.Primary();

            // ── status strip (Dock.Bottom, 22px, 3px inset) — created before mounting ──
            lblTemplateStatus = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = EmptyPrompt,
                ForeColor = Colors.FontDisabled(),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            // 23-08: the "Save▾" pack-management drop-down lives at the right edge of the status strip
            // (Dock.Right, 90px). Clicking it opens the per-pack save / select / import / export menu.
            btnSaveTemplate = new UtinniButton
            {
                Dock = DockStyle.Right,
                Width = 90,
                Text = "Save ▾",
                UseDisableColor = true,
                UseVisualStyleBackColor = true,
            };
            btnSaveTemplate.Click += OnSaveMenuButtonClick;
            pnlTemplateStatus = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                Padding = new Padding(3, 3, 3, 3),
                BackColor = Colors.Primary(),
            };
            // Fill child (the status label) added FIRST (front-most), then the Right-docked Save button.
            pnlTemplateStatus.Controls.Add(lblTemplateStatus);
            pnlTemplateStatus.Controls.Add(btnSaveTemplate);

            // ── raw-byte grid (TOP) — Consolas 9pt monospace, read-only, selectable ──
            txtBytes = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9F),
                BackColor = Colors.PrimaryHighlight(),
                ForeColor = Colors.Font(),
                ShortcutsEnabled = false, // mirror txtHex — the Form owns Ctrl+Z/Y/S
            };

            // ── decoded field list (BOTTOM) — name / type / value / span ──
            lvFields = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                BackColor = Colors.PrimaryHighlight(),
                ForeColor = Colors.Font(),
                GridLines = false,
            };
            lvFields.Columns.Add("Name", 150);
            lvFields.Columns.Add("Type", 90);
            lvFields.Columns.Add("Value", 220);
            lvFields.Columns.Add("Span", 110);

            // ── nested SplitContainer (Pitfall 8: Size BEFORE SplitterDistance) ──
            splitTemplate = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal, // horizontal splitter => top/bottom panels
            };
            // A fresh SplitContainer is 150px tall; set a realistic Size first so the SplitterDistance
            // below lands inside [Panel1MinSize, Height - Panel2MinSize] and cannot throw from the ctor.
            splitTemplate.Size = new Size(640, 560);
            splitTemplate.SplitterWidth = 4;
            splitTemplate.Panel1MinSize = 120;
            splitTemplate.Panel2MinSize = 120;
            try
            {
                splitTemplate.SplitterDistance = 320;
            }
            catch
            {
                // keep the default — a bad value must never bubble out of construction (MEF guard).
            }
            // Add the Fill child of each split panel first (front-most), then nothing else docks there.
            splitTemplate.Panel1.Controls.Add(txtBytes);
            splitTemplate.Panel2.Controls.Add(lvFields);

            // ── pnlTemplate (Dock.Fill, front-most) — Fill child added FIRST, then the Bottom strip ──
            pnlTemplate = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Colors.Primary(),
            };
            pnlTemplate.Controls.Add(splitTemplate);       // Fill — front-most (added first)
            pnlTemplate.Controls.Add(pnlTemplateStatus);   // Bottom (added after)

            // Mount: pnlTemplate is the single Dock.Fill child of this UserControl.
            Controls.Add(pnlTemplate);

            // Inline value edit on double-click a row (commit on Leave/Validating, never per-keystroke;
            // the editor TextBox is created on demand in BeginEditValue so there is no persistent control).
            lvFields.MouseDoubleClick += OnFieldRowDoubleClick;
        }

        // ── public surface the host (Task 2) drives ───────────────────────────────

        /// <summary>The raw-byte grid — the host installs the "Assign type &amp; name…" context menu here.</summary>
        public Control ByteGrid { get { return txtBytes; } }

        /// <summary>The in-progress template (null until the host seeds it). The host reads it to emit the
        /// D-01 JSON on Save and to feed <see cref="KernelCodec.Encode"/> on a value edit.</summary>
        public TemplateModel Template { get { return template; } }

        /// <summary>The bound leaf payload the pane renders + fit-checks against (a copy; never mutated here).</summary>
        public byte[] Payload { get { return payload; } }

        /// <summary>Byte offset of the start of the current grid selection (-1 when nothing is selected).</summary>
        public int SelectionStart { get; private set; } = -1;

        /// <summary>Length in bytes of the current grid selection (0 when nothing is selected).</summary>
        public int SelectionLength { get; private set; }

        /// <summary>
        /// Seeds the pane with a leaf payload + the template to author against. Pass an empty template
        /// (no fields) to start a fresh template over raw bytes; pass a resolved template to show an
        /// auto-applied decode. Re-renders the grid + field list + indicator. Owns NO format logic — it
        /// calls the headless engine for the decode/fit.
        /// </summary>
        public void SetPayload(byte[] leafPayload, TemplateModel model)
        {
            payload = leafPayload ?? new byte[0];
            template = model ?? new TemplateModel { Version = 1, Fields = new List<FieldRecord>() };
            if (template.Fields == null) template.Fields = new List<FieldRecord>();
            SelectionStart = -1;
            SelectionLength = 0;
            RenderBytes();
            RefreshDecode();
        }

        /// <summary>Appends a field to the in-progress template (in file byte order) and re-renders.</summary>
        public void AppendField(FieldRecord field)
        {
            if (field == null) return;
            if (template == null) template = new TemplateModel { Version = 1, Fields = new List<FieldRecord>() };
            if (template.Fields == null) template.Fields = new List<FieldRecord>();
            template.Fields.Add(field);
            RefreshDecode();
        }

        /// <summary>Replaces the entire field list (used by the field-list editor for remove/reorder) and
        /// re-renders. The host owns the authoring-local undo stack; this just swaps the model.</summary>
        public void ReplaceFields(List<FieldRecord> fields)
        {
            if (template == null) template = new TemplateModel { Version = 1, Fields = new List<FieldRecord>() };
            template.Fields = fields ?? new List<FieldRecord>();
            RefreshDecode();
        }

        /// <summary>The currently-selected field row's name (null when no row selected). The host uses this
        /// to target Remove / Move up / Move down on the field list.</summary>
        public string SelectedFieldName()
        {
            if (lvFields.SelectedItems.Count == 0) return null;
            return lvFields.SelectedItems[0].Tag as string;
        }

        /// <summary>
        /// Surfaces the D-10 length-changing feedback (UI-SPEC Copywriting): after a value edit resized
        /// the payload + recomputed a count field, the host calls this to show the transient strip before
        /// the steady consumed-count line settles back on the next <see cref="RefreshDecode"/>.
        /// </summary>
        public void ShowLengthChangeFeedback(int oldLen, int newLen)
        {
            lblTemplateStatus.Text =
                "Edit applied · payload " + oldLen + "→" + newLen + " bytes · count field recomputed.";
            lblTemplateStatus.ForeColor = Colors.Font();
        }

        /// <summary>23-08: sets the optional project-local pack dir (the open mod's own pack). When set, the
        /// Save menu offers a "Project pack ({name})" target in addition to "User templates"; null hides it.
        /// The host supplies it from the open document's mod directory when one is known.</summary>
        public void SetProjectLocalPackDir(string dirOrNull)
        {
            projectLocalDir = string.IsNullOrEmpty(dirOrNull) ? null : dirOrNull;
        }

        // ── 23-08: pack management (save / select / import / export over TemplatePackStore) ──────────
        //
        // The "Save▾" button builds its menu on demand against the live scanned-pack allow-list (D-12) so a
        // pack added/removed since the pane opened is reflected. Save serializes the current TemplateModel
        // via TemplateJson into the chosen WRITABLE pack dir; the write path is resolved WITHIN the pack
        // root via LooseOverridePath (containment — T-23-08-PATH). The shipped pack is shown DISABLED
        // ("Shipped (read-only)") and is never a write target. Overwrite + delete route through
        // FormSaveConfirmDialog with the exact UI-SPEC headings/bodies/accept verbs (T-23-08-DESTRUCT).

        // Builds + drops the Save▾ menu under the button. Built fresh each click so the pack list is current.
        private void OnSaveMenuButtonClick(object sender, EventArgs e)
        {
            UtinniContextMenuStrip menu = BuildSaveMenu();
            menu.Show(btnSaveTemplate, new Point(0, btnSaveTemplate.Height));
        }

        // The pack-management menu (UI-SPEC §Copywriting Save/Import/Export rows). Save sub-items per
        // writable scanned dir; the shipped pack disabled; then Select / Import / Export.
        private UtinniContextMenuStrip BuildSaveMenu()
        {
            var menu = new UtinniContextMenuStrip();

            // "Save template to pack…" → a sub-item per writable pack; shipped shown disabled.
            var miSave = new ToolStripMenuItem("Save template to pack…");
            string userDir = UserTemplatesDir();
            if (!string.IsNullOrEmpty(userDir))
            {
                var miUser = new ToolStripMenuItem("User templates");
                miUser.Click += (s, a) => SaveToPack(userDir);
                miSave.DropDownItems.Add(miUser);
            }
            if (!string.IsNullOrEmpty(projectLocalDir))
            {
                var miProject = new ToolStripMenuItem("Project pack (" + Path.GetFileName(projectLocalDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + ")");
                miProject.Click += (s, a) => SaveToPack(projectLocalDir);
                miSave.DropDownItems.Add(miProject);
            }
            // The shipped pack is read-only: shown disabled, never a write target.
            var miShipped = new ToolStripMenuItem("Shipped (read-only)") { Enabled = false };
            miSave.DropDownItems.Add(miShipped);
            menu.Items.Add(miSave);

            menu.Items.Add(new ToolStripSeparator());

            // "Select a template…" → lists templates across the scanned packs (load-order) and applies one.
            var miSelect = new ToolStripMenuItem("Select a template…");
            miSelect.Click += (s, a) => ShowSelectTemplate();
            menu.Items.Add(miSelect);

            menu.Items.Add(new ToolStripSeparator());

            // Import / export (file-dialog driven — no new file widget, mirrors the host's existing usage).
            var miImport = new ToolStripMenuItem("Import template pack…");
            miImport.Click += (s, a) => ImportTemplatePack();
            menu.Items.Add(miImport);
            var miExport = new ToolStripMenuItem("Export this template…");
            miExport.Click += (s, a) => ExportTemplate();
            menu.Items.Add(miExport);

            return menu;
        }

        // The app-data user pack dir (%APPDATA%/Utinni/templates) — created on first save. This is the
        // documented per-user writable pack root (TemplatePackStore.DefaultRoots app-data root).
        private static string UserTemplatesDir()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(appData)) return null;
                return Path.Combine(appData, "Utinni", "templates");
            }
            catch
            {
                return null;
            }
        }

        // Serializes the current template (D-01 JSON) into packRoot/<key>.json. The file name derives from
        // the match key; the write path resolves WITHIN packRoot (LooseOverridePath containment — escapes
        // are rejected before any write). An existing same-key file routes through FormSaveConfirmDialog
        // (overwrite confirm) before the write.
        private void SaveToPack(string packRoot)
        {
            if (template == null || template.Fields == null || template.Fields.Count == 0)
            {
                lblTemplateStatus.Text = "Nothing to save yet — assign at least one field first.";
                lblTemplateStatus.ForeColor = Color.Red;
                return;
            }
            try
            {
                Directory.CreateDirectory(packRoot);
                string canonicalRoot = Path.GetFullPath(packRoot);
                string fileName = PackFileName(template);
                // Resolve WITHIN the pack root — the containment gate the save-path uses everywhere.
                string fullPath = LooseOverridePath.Resolve(canonicalRoot, fileName);

                string packLabel = Path.GetFileName(canonicalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                string keyName = DisplayName(template);
                if (File.Exists(fullPath))
                {
                    // Destructive: overwrite an existing same-key template → confirm (UI-SPEC §Destructive).
                    using (var dlg = new FormSaveConfirmDialog(
                        "Overwrite '" + keyName + "' in " + packLabel + "?",
                        "A template with this match key already exists in this pack. Overwriting replaces its field definitions.",
                        "Overwrite", "Cancel"))
                    {
                        if (dlg.ShowDialog(this) != DialogResult.OK ||
                            dlg.Outcome != FormSaveConfirmDialog.ConfirmOutcome.Accepted)
                        {
                            return;
                        }
                    }
                }

                if (template.Version <= 0) template.Version = TemplateModelDefaults.CurrentVersion;
                string json = TemplateJson.Serialize(template);
                File.WriteAllText(fullPath, json);
                lblTemplateStatus.Text = "Saved '" + keyName + "' to " + packLabel + ".";
                lblTemplateStatus.ForeColor = StatusGreen;
            }
            catch (Exception ex)
            {
                lblTemplateStatus.Text = "Save failed: " + ex.Message;
                lblTemplateStatus.ForeColor = Color.Red;
            }
        }

        // The select-a-template list: enumerates the scanned packs (load-order) and applies the picked one
        // to the current leaf via TemplateSelected (the host clones + auto-applies; non-destructive). Per-row
        // Delete routes through FormSaveConfirmDialog and removes the JSON from its pack.
        private void ShowSelectTemplate()
        {
            List<TemplateLoadResult> all;
            try
            {
                all = TemplatePackStore.DefaultRoots(projectLocalDir).LoadAll();
            }
            catch (Exception ex)
            {
                lblTemplateStatus.Text = "Could not scan template packs: " + ex.Message;
                lblTemplateStatus.ForeColor = Color.Red;
                return;
            }

            var loaded = new List<TemplateLoadResult>();
            foreach (TemplateLoadResult r in all)
            {
                if (r != null && r.Loaded) loaded.Add(r);
            }

            using (var dlg = new UtinniCoreDotNet.UI.Forms.UtinniForm())
            {
                dlg.Text = "Select a template";
                dlg.DrawName = true;
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.ClientSize = new Size(460, 280);
                dlg.StartPosition = FormStartPosition.CenterParent;

                var lbl = new UtinniLabel
                {
                    Text = loaded.Count + " templates across the scanned packs. Apply one to this chunk:",
                    Left = 12, Top = 40, Width = 436, Height = 20, ForeColor = Colors.Font(),
                };
                var list = new ListBox
                {
                    Left = 12, Top = 64, Width = 436, Height = 150,
                    BackColor = Colors.PrimaryHighlight(), ForeColor = Colors.Font(),
                    BorderStyle = BorderStyle.FixedSingle,
                };
                foreach (TemplateLoadResult r in loaded)
                {
                    list.Items.Add(DisplayName(r.Template) + "  ·  " + Path.GetFileName(r.Path));
                }
                if (list.Items.Count > 0) list.SelectedIndex = 0;

                var btnApply = new UtinniButton
                {
                    Text = "Apply", Left = 200, Top = 226, Width = 70,
                    UseDisableColor = true, UseVisualStyleBackColor = true,
                };
                btnApply.Click += (s, a) => { dlg.DialogResult = DialogResult.OK; dlg.Close(); };
                var btnDelete = new UtinniButton
                {
                    Text = "Delete", Left = 278, Top = 226, Width = 80,
                    UseDisableColor = true, UseVisualStyleBackColor = true,
                };
                btnDelete.Click += (s, a) =>
                {
                    int i = list.SelectedIndex;
                    if (i < 0 || i >= loaded.Count) return;
                    if (DeleteTemplate(loaded[i]))
                    {
                        loaded.RemoveAt(i);
                        list.Items.RemoveAt(i);
                        if (list.Items.Count > 0) list.SelectedIndex = 0;
                    }
                };
                var btnCancel = new UtinniButton
                {
                    Text = "Cancel", Left = 366, Top = 226, Width = 80,
                    UseDisableColor = true, UseVisualStyleBackColor = true,
                };
                btnCancel.Click += (s, a) => { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };

                dlg.Controls.Add(lbl);
                dlg.Controls.Add(list);
                dlg.Controls.Add(btnApply);
                dlg.Controls.Add(btnDelete);
                dlg.Controls.Add(btnCancel);
                dlg.AcceptButton = btnApply;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                int sel = list.SelectedIndex;
                if (sel < 0 || sel >= loaded.Count) return;
                var handler = TemplateSelected;
                if (handler != null)
                {
                    handler(this, new TemplateSelectedEventArgs(loaded[sel].Template));
                }
            }
        }

        // Destructive: delete a template's JSON from its pack → confirm (UI-SPEC §Destructive), then File.Delete
        // (only when the path is contained under one of the scanned roots — never deletes outside a pack).
        private bool DeleteTemplate(TemplateLoadResult r)
        {
            if (r == null || string.IsNullOrEmpty(r.Path)) return false;
            string name = DisplayName(r.Template);
            string packLabel = Path.GetFileName(Path.GetDirectoryName(r.Path) ?? "");
            using (var dlg = new FormSaveConfirmDialog(
                "Delete template '" + name + "'?",
                "This removes the JSON file from " + packLabel + ". Chunks it auto-applied to will fall back to raw hex.",
                "Delete", "Cancel"))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK ||
                    dlg.Outcome != FormSaveConfirmDialog.ConfirmOutcome.Accepted)
                {
                    return false;
                }
            }
            try
            {
                if (!IsUnderAScannedRoot(r.Path))
                {
                    lblTemplateStatus.Text = "Refused to delete — file is outside the template packs.";
                    lblTemplateStatus.ForeColor = Color.Red;
                    return false;
                }
                File.Delete(r.Path);
                lblTemplateStatus.Text = "Deleted '" + name + "' from " + packLabel + ".";
                lblTemplateStatus.ForeColor = Colors.Font();
                return true;
            }
            catch (Exception ex)
            {
                lblTemplateStatus.Text = "Delete failed: " + ex.Message;
                lblTemplateStatus.ForeColor = Color.Red;
                return false;
            }
        }

        // Import: pick a template JSON via OpenFileDialog and copy it into the user pack (validated as a real
        // template first; the copy target resolves WITHIN the user pack root — containment).
        private void ImportTemplatePack()
        {
            string userDir = UserTemplatesDir();
            if (string.IsNullOrEmpty(userDir)) return;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Import template pack";
                ofd.Filter = "Template JSON (*.json)|*.json|All files (*.*)|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string json = File.ReadAllText(ofd.FileName);
                    TemplateModel imported = TemplateJson.Deserialize(json); // validate before copying
                    Directory.CreateDirectory(userDir);
                    string canonicalRoot = Path.GetFullPath(userDir);
                    string fullPath = LooseOverridePath.Resolve(canonicalRoot, PackFileName(imported));
                    if (File.Exists(fullPath))
                    {
                        using (var dlg = new FormSaveConfirmDialog(
                            "Overwrite '" + DisplayName(imported) + "' in User templates?",
                            "A template with this match key already exists in this pack. Overwriting replaces its field definitions.",
                            "Overwrite", "Cancel"))
                        {
                            if (dlg.ShowDialog(this) != DialogResult.OK ||
                                dlg.Outcome != FormSaveConfirmDialog.ConfirmOutcome.Accepted)
                            {
                                return;
                            }
                        }
                    }
                    File.WriteAllText(fullPath, TemplateJson.Serialize(imported));
                    lblTemplateStatus.Text = "Imported '" + DisplayName(imported) + "' into User templates.";
                    lblTemplateStatus.ForeColor = StatusGreen;
                }
                catch (Exception ex)
                {
                    lblTemplateStatus.Text = "Import failed: " + ex.Message;
                    lblTemplateStatus.ForeColor = Color.Red;
                }
            }
        }

        // Export: write the current template (D-01 JSON) to a user-chosen path via SaveFileDialog.
        private void ExportTemplate()
        {
            if (template == null || template.Fields == null || template.Fields.Count == 0)
            {
                lblTemplateStatus.Text = "Nothing to export yet — assign at least one field first.";
                lblTemplateStatus.ForeColor = Color.Red;
                return;
            }
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Export this template";
                sfd.Filter = "Template JSON (*.json)|*.json|All files (*.*)|*.*";
                sfd.FileName = PackFileName(template);
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (template.Version <= 0) template.Version = TemplateModelDefaults.CurrentVersion;
                    File.WriteAllText(sfd.FileName, TemplateJson.Serialize(template));
                    lblTemplateStatus.Text = "Exported to " + Path.GetFileName(sfd.FileName) + ".";
                    lblTemplateStatus.ForeColor = StatusGreen;
                }
                catch (Exception ex)
                {
                    lblTemplateStatus.Text = "Export failed: " + ex.Message;
                    lblTemplateStatus.ForeColor = Color.Red;
                }
            }
        }

        // True iff path lies within one of the scanned pack roots (defense for delete — never touch a file
        // outside a pack). Routes through the SAME containment predicate the loader uses.
        private bool IsUnderAScannedRoot(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                foreach (TemplatePackRoot root in TemplatePackStore.DefaultRoots(projectLocalDir).Roots)
                {
                    if (LooseOverridePath.IsContainedUnderRoot(root.Directory, full)) return true;
                }
            }
            catch
            {
            }
            return false;
        }

        // A stable, filesystem-safe file name derived from the template's match key (ancestor·tag) so the
        // same template overwrites its prior save rather than spawning duplicates.
        private static string PackFileName(TemplateModel t)
        {
            string baseName = DisplayName(t);
            var sb = new StringBuilder();
            foreach (char c in baseName)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            string clean = sb.ToString().Trim('_');
            if (clean.Length == 0) clean = "template";
            return clean + ".json";
        }

        // A human display name for a template = its match key (tag under ancestor path, or tag-only).
        private static string DisplayName(TemplateModel t)
        {
            if (t == null) return "template";
            string tag = (t.MatchLeafTag ?? "").Trim();
            if (t.MatchTagOnly || string.IsNullOrEmpty(t.MatchAncestorPath))
            {
                return string.IsNullOrEmpty(tag) ? "template" : tag;
            }
            return tag + " under " + t.MatchAncestorPath;
        }

        // ── rendering ─────────────────────────────────────────────────────────────

        // Builds the monospace hex dump and remembers, per display line, the byte offset it starts at so
        // a caret index can be mapped back to a byte index. Reuses the FormIffEditor HexDump format
        // verbatim (offset col, hex pairs, ASCII gutter) so the pane is visually one with the host.
        private void RenderBytes()
        {
            txtBytes.Text = HexDump(payload);
        }

        // Re-runs the headless decode + fit-check and repaints the field list + indicator. Catch-all guard
        // so a malformed in-progress template can never throw into the WinForms message pump (it surfaces
        // as the red does-not-fit flag instead).
        private void RefreshDecode()
        {
            lvFields.BeginUpdate();
            lvFields.Items.Clear();
            FitReport fit = null;
            try
            {
                fit = FitChecker.FitCheck(template, payload);
                DecodedTemplate decoded = KernelCodec.Decode(template, payload);
                PopulateFieldRows(decoded);
            }
            catch (Exception)
            {
                // A template that over-reads (or any in-progress inconsistency) surfaces as does-not-fit;
                // the field rows show as far as the safe walk got (none on a hard failure). Never throws out.
            }
            finally
            {
                lvFields.EndUpdate();
            }
            UpdateIndicator(fit);
        }

        // Projects the decoded values into the field-list rows: name (chrome font) · type · value
        // (Consolas via the list font fallback) · span. Arrays/structs render a compact summary; scalars
        // render their CLR value. Enum/flags decoration renders raw value(name) per UI-SPEC.
        private void PopulateFieldRows(DecodedTemplate decoded)
        {
            if (template == null || template.Fields == null) return;
            int runningOffset = 0;
            foreach (FieldRecord f in template.Fields)
            {
                if (f == null) continue;
                object value = null;
                if (decoded != null && decoded.Values != null && !string.IsNullOrEmpty(f.Name))
                {
                    decoded.Values.TryGetValue(f.Name, out value);
                }
                string valueText = FormatValue(f, value);
                int width = FieldDisplayWidth(f, value);
                string span = width > 0
                    ? "0x" + runningOffset.ToString("X") + " · " + width + "b"
                    : "0x" + runningOffset.ToString("X");
                var row = new ListViewItem(new[]
                {
                    f.Name ?? "(unnamed)",
                    TypeLabel(f),
                    valueText,
                    span
                });
                row.Tag = f.Name;
                lvFields.Items.Add(row);
                runningOffset += width;
            }
        }

        // The live round-trip indicator (UI-SPEC §States). Three states, exact copy strings:
        //   green  — consumed exactly: "Round-trip OK · M of M bytes consumed" (the one sanctioned green).
        //   neutral— partially annotated (authoring): "N of M consumed — R bytes still raw" (FontDisabled).
        //   red    — does not fit (over-read / decode stopped): "Template doesn't fit these bytes…" (Red).
        private void UpdateIndicator(FitReport fit)
        {
            if (template == null || template.Fields == null || template.Fields.Count == 0)
            {
                lblTemplateStatus.Text = EmptyPrompt;
                lblTemplateStatus.ForeColor = Colors.FontDisabled();
                return;
            }
            if (fit == null)
            {
                // Decode threw a hard failure — does-not-fit, stop offset unknown -> report 0.
                lblTemplateStatus.Text =
                    "Template doesn't fit these bytes — decode stopped at offset 0x0. Showing raw.";
                lblTemplateStatus.ForeColor = Color.Red;
                return;
            }
            int total = fit.BytesTotal;
            int consumed = fit.BytesConsumed;
            if (fit.ConsumedExactly)
            {
                lblTemplateStatus.Text = "Round-trip OK · " + total + " of " + total + " bytes consumed";
                lblTemplateStatus.ForeColor = StatusGreen;
            }
            else if (consumed < total)
            {
                // Two sub-cases share the BytesConsumed<BytesTotal report from FitChecker: a still-being-
                // authored template (tail bytes simply unclaimed) vs a template that over-read and the
                // checker clamped consumed==total. The clamp case is handled above (consumed==total in the
                // over-read path actually reports consumed==total==len). Here consumed<total => raw tail.
                int raw = total - consumed;
                lblTemplateStatus.Text = consumed + " of " + total + " bytes consumed — " + raw + " bytes still raw";
                lblTemplateStatus.ForeColor = Colors.FontDisabled();
            }
            else
            {
                // consumed >= total but NOT ConsumedExactly => the over-read clamp (FitChecker sets
                // BytesConsumed = len, BytesTotal = len on a DecoderException). The honest does-not-fit flag.
                lblTemplateStatus.Text =
                    "Template doesn't fit these bytes — decode stopped at offset 0x" + consumed.ToString("X") + ". Showing raw.";
                lblTemplateStatus.ForeColor = Color.Red;
            }
        }

        // ── inline value edit (commit on Leave/Validating, never per-keystroke) ──

        private void OnFieldRowDoubleClick(object sender, MouseEventArgs e)
        {
            ListViewHitTestInfo hit = lvFields.HitTest(e.Location);
            if (hit.Item == null) return;
            string fieldName = hit.Item.Tag as string;
            if (string.IsNullOrEmpty(fieldName)) return;
            BeginEditValue(hit.Item, fieldName);
        }

        // Spawns a transient TextBox over the Value cell; commits the typed text via
        // FieldValueCommitRequested on Leave/Validating (the host re-encodes through IffEditController).
        private void BeginEditValue(ListViewItem item, string fieldName)
        {
            Rectangle cell = item.SubItems.Count > 2 ? item.SubItems[2].Bounds : item.Bounds;
            var editor = new TextBox
            {
                Font = new Font("Consolas", 9F),
                BackColor = Colors.PrimaryHighlight(),
                ForeColor = Colors.Font(),
                BorderStyle = BorderStyle.FixedSingle,
                Text = item.SubItems.Count > 2 ? item.SubItems[2].Text : "",
                Bounds = new Rectangle(cell.Left, cell.Top, Math.Max(cell.Width, 120), cell.Height),
            };
            bool committed = false;
            EventHandler commit = (s, a) =>
            {
                if (committed) return;
                committed = true;
                string newText = editor.Text;
                if (editor.Parent != null) editor.Parent.Controls.Remove(editor);
                editor.Dispose();
                var handler = FieldValueCommitRequested;
                if (handler != null)
                {
                    handler(this, new FieldValueCommitEventArgs(fieldName, newText));
                }
            };
            editor.Leave += commit;
            editor.KeyDown += (s, a) =>
            {
                if (a.KeyCode == Keys.Enter) { a.SuppressKeyPress = true; commit(s, EventArgs.Empty); }
                else if (a.KeyCode == Keys.Escape)
                {
                    a.SuppressKeyPress = true;
                    committed = true;
                    if (editor.Parent != null) editor.Parent.Controls.Remove(editor);
                    editor.Dispose();
                }
            };
            lvFields.Controls.Add(editor);
            editor.Focus();
            editor.SelectAll();
        }

        // ── value/type formatting (display only — no decode logic) ────────────────

        private static string TypeLabel(FieldRecord f)
        {
            if (f == null) return "";
            if (f.Type == KernelType.Array && f.Repeat != null)
            {
                return "Array[" + f.Repeat.Kind + "]";
            }
            return f.Type.ToString();
        }

        private string FormatValue(FieldRecord f, object value)
        {
            if (value == null) return "";
            if (f != null && f.ValueMap != null && f.ValueMap.Entries != null && value is long)
            {
                return DecorateNamedValue(f.ValueMap, (long)value);
            }
            if (value is byte[])
            {
                byte[] b = (byte[])value;
                var sb = new StringBuilder();
                for (int i = 0; i < b.Length && i < 16; i++) sb.Append(b[i].ToString("X2")).Append(' ');
                if (b.Length > 16) sb.Append("…");
                return sb.ToString().TrimEnd();
            }
            if (value is System.Collections.ICollection)
            {
                return "[" + ((System.Collections.ICollection)value).Count + " elements]";
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        // Renders an enum value as "raw(name)" and a flags value as "raw(walk|run)" — Pitfall 4: a flags
        // entry is a bit POSITION (1..32) so the displayed mask bit is (1 << (pos-1)).
        private static string DecorateNamedValue(NamedValueMap map, long raw)
        {
            if (map.IsFlags)
            {
                var names = new List<string>();
                foreach (KeyValuePair<string, long> e in map.Entries)
                {
                    long bit = 1L << (int)(e.Value - 1);
                    if ((raw & bit) != 0) names.Add(e.Key);
                }
                return raw.ToString(CultureInfo.InvariantCulture) + "(" + string.Join("|", names) + ")";
            }
            foreach (KeyValuePair<string, long> e in map.Entries)
            {
                if (e.Value == raw)
                {
                    return raw.ToString(CultureInfo.InvariantCulture) + "(" + e.Key + ")";
                }
            }
            return raw.ToString(CultureInfo.InvariantCulture);
        }

        // Display-only fixed-width estimate for the span column (NOT a decode — the engine owns the real
        // widths; this is the at-a-glance scalar width so the user can eyeball byte boundaries).
        private static int FieldDisplayWidth(FieldRecord f, object value)
        {
            if (f == null) return 0;
            switch (f.Type)
            {
                case KernelType.I8: case KernelType.U8: return 1;
                case KernelType.I16: case KernelType.U16: return 2;
                case KernelType.I32: case KernelType.U32: case KernelType.F32: return 4;
                case KernelType.F64: return 8;
                case KernelType.FixedChar: case KernelType.RawBytes: case KernelType.Pad: return f.ByteWidth;
                case KernelType.CString:
                    return value is string ? ((string)value).Length + 1 : 0;
                default:
                    return 0; // struct/array width is variable — left blank in the span column
            }
        }

        private const string EmptyPrompt =
            "No template for this chunk. Select bytes and assign a type to start one.";

        // The HexDump format mirrors FormIffEditor.HexDump verbatim (offset col + hex pairs + |ascii|) so
        // the grid is visually identical to the host's read-only hex view. Un-annotated bytes render plain
        // (default font on the inset background) — the candor-critical "visibly raw" rendering (D-02): any
        // plain, un-boxed run of hex at the tail is obviously unclaimed.
        private static string HexDump(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < bytes.Length; i += BytesPerLine)
            {
                sb.Append(i.ToString("X8")).Append(":  ");
                int end = Math.Min(i + BytesPerLine, bytes.Length);
                for (int j = i; j < end; j++)
                {
                    sb.Append(bytes[j].ToString("X2")).Append(' ');
                }
                for (int j = end; j < i + BytesPerLine; j++) sb.Append("   ");
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

        /// <summary>
        /// Maps the current grid TextBox selection (caret char range) back to a byte range and records it
        /// in <see cref="SelectionStart"/>/<see cref="SelectionLength"/>. The host calls this before opening
        /// the assign-field popup so offsets are SELECTIONS, not arithmetic (D-02). Returns true when a
        /// non-empty byte range was resolved. Tolerant: a selection that lands in the offset column or the
        /// ASCII gutter clamps to the nearest whole bytes on its line.
        /// </summary>
        public bool CaptureSelection()
        {
            SelectionStart = -1;
            SelectionLength = 0;
            int selStart = txtBytes.SelectionStart;
            int selLen = txtBytes.SelectionLength;
            if (selLen <= 0) return false;

            int firstByte = CaretToByteIndex(selStart, roundUp: false);
            int lastByte = CaretToByteIndex(selStart + selLen - 1, roundUp: true);
            if (firstByte < 0 || lastByte < firstByte) return false;
            if (firstByte >= payload.Length) return false;
            if (lastByte >= payload.Length) lastByte = payload.Length - 1;

            SelectionStart = firstByte;
            SelectionLength = lastByte - firstByte + 1;
            return SelectionLength > 0;
        }

        // Maps a caret char index in the hex dump to the byte index under it. Each display line is
        // OffsetColWidth chars of offset prefix + BytesPerLine * 3 chars of hex pairs (then the gutter).
        // A caret in the offset prefix clamps to the first byte of the line; a caret in/after the gutter
        // clamps to the last byte of the line.
        private int CaretToByteIndex(int caret, bool roundUp)
        {
            if (caret < 0) return -1;
            string text = txtBytes.Text;
            if (caret >= text.Length) caret = text.Length - 1;

            // Find the line + column of the caret by counting newlines (the dump uses Environment.NewLine).
            int lineStart = 0;
            int lineIndex = 0;
            for (int i = 0; i < caret && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lineStart = i + 1;
                    lineIndex++;
                }
            }
            int col = caret - lineStart;
            int lineFirstByte = lineIndex * BytesPerLine;
            if (lineFirstByte >= payload.Length) return payload.Length - 1;

            int hexCol = col - OffsetColWidth;
            if (hexCol < 0) return lineFirstByte; // caret in the offset prefix -> first byte of line
            int byteInLine = hexCol / 3;          // each byte is "XX " => 3 chars
            if (byteInLine >= BytesPerLine) byteInLine = BytesPerLine - 1; // gutter -> last byte of line
            int idx = lineFirstByte + byteInLine;
            if (idx >= payload.Length) idx = payload.Length - 1;
            return idx;
        }
    }

    /// <summary>Carries an inline field-value edit out to the host for a document commit.</summary>
    public sealed class FieldValueCommitEventArgs : EventArgs
    {
        public FieldValueCommitEventArgs(string fieldName, string newText)
        {
            FieldName = fieldName;
            NewText = newText;
        }

        /// <summary>Name of the field whose value was edited.</summary>
        public string FieldName { get; private set; }

        /// <summary>The raw text the user typed (the host coerces it to the field's CLR type before encode).</summary>
        public string NewText { get; private set; }
    }

    /// <summary>23-08: carries a template the user picked from the "Select a template…" list out to the host,
    /// which clones it onto the current leaf (the non-destructive auto-apply path).</summary>
    public sealed class TemplateSelectedEventArgs : EventArgs
    {
        public TemplateSelectedEventArgs(TemplateModel template)
        {
            Template = template;
        }

        /// <summary>The selected template (a pack-loaded instance; the host clones before applying).</summary>
        public TemplateModel Template { get; private set; }
    }
}
