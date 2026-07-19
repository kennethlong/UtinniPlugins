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
// 15-01 WorldSnapshot placements editor (PROD-W2-WS / D-01/D-02/D-03) — a resizable companion
// UtinniForm hosting a flat placements TABLE + multi-select bulk operations (move / delete /
// retemplate) over the loaded world snapshot. The table is a NEW VIEW over the native
// WorldSnapshotReaderWriter node list (zero new format code, D-02); bulk ops compose the shipped
// WorldSnapshotCommands IUndoCommands via WorldSnapshotImpl.BulkMove/BulkDelete/BulkRetemplate so
// the whole bulk lands atomically on one game-frame and is undoable. Shell shape cloned from
// FormDatatableEditor / FormObjectTemplateEditor (Phases 9/11). Singleton hide-not-dispose via the
// framework SingletonFormClosePolicy (mandatory for editor forms). World-snapshot/node-list
// semantics were studied from the shipped Utinni reader only — no SOE code/identifiers copied.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TJT.SWG;
using TJT.UI.Controls;
using UtinniCore.Utinni;
using UtinniCoreDotNet.PluginFramework;
using UtinniCoreDotNet.UI;
using UtinniCoreDotNet.UI.Controls;
using UtinniCoreDotNet.UI.Forms;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Forms
{
    /// <summary>
    /// Companion resizable placements-table window for the WorldSnapshot editor. Launched from the
    /// shipped 417px <see cref="TJT.UI.SubPanels.SnapshotPanel"/> via its new <c>Placements…</c>
    /// button (the SubPanel is too narrow for a real table — the Wave-1 "narrow controls SubPanel +
    /// wide grid UtinniForm" split). The table is a VIEW over the SAME loaded snapshot the panel
    /// drives: <see cref="RefreshTable"/> re-reads the native node list; closing the window does NOT
    /// unload the snapshot.
    ///
    /// <para><b>Singleton hide-not-dispose:</b> <see cref="FormSnapshotPlacements_FormClosing"/>
    /// delegates its CloseReason decision to
    /// <see cref="SingletonFormClosePolicy.ShouldHideInsteadOfDispose"/> — on user-initiated close
    /// the window hides (so the next launch re-Shows this same instance) instead of disposing.</para>
    /// </summary>
    public partial class FormSnapshotPlacements : UtinniForm
    {
        private const string SettingsSection = "Snapshot";

        // Locked copy (15-UI-SPEC § Reload Candor Contract + § Copywriting) — do not soften.
        private const string ReloadBadgeCopy = "Placements re-resolve on the next scene change.";

        // Locked DEC-A3 preview-vs-author sentence (D-10, surfaced verbatim as a dimmed footer).
        private const string PreviewVsAuthorCopy =
            "Utinni places, transforms, and retemplates existing object templates — authoring the templates or their meshes is the Blender lane.";

        // Locked empty-state copy (15-UI-SPEC § States / § Copywriting).
        private const string EmptyHeading = "No snapshot loaded";
        private const string EmptyBody = "Load a snapshot from the Snapshot panel to see its placements.";

        private readonly WorldSnapshotImpl worldSnapshot;
        private readonly IEditorPlugin editorPlugin;
        private readonly UtINI ini;
        private readonly ToolTip toolTip = new ToolTip();

        // One flat placements row, captured from the native node list ON THE GAME THREAD and projected
        // to the read-only grid. We never hold native Node pointers on the WinForms thread.
        // Ids are int64: the advertised client's WorldSnapshotLive rows key by full NetworkId value
        // (authored ids stay int32-range by the save contract; SWGEmu ids widen losslessly).
        private sealed class PlacementRow
        {
            public long Id;
            public long ParentId;
            public string ObjectTemplate;
            public string Cell;
            public float X;
            public float Y;
            public float Z;
        }

        private readonly List<PlacementRow> allRows = new List<PlacementRow>();
        private bool hasSnapshot;
        private string snapshotName = "";

        public FormSnapshotPlacements(WorldSnapshotImpl worldSnapshot, IEditorPlugin editorPlugin)
        {
            InitializeComponent();

            // TJT brand icon, guarded (06-05 pattern).
            string tjtIconPath = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "Resources", "TJT.ico");
            if (File.Exists(tjtIconPath))
            {
                this.Icon = new Icon(tjtIconPath);
            }

            this.worldSnapshot = worldSnapshot;
            this.editorPlugin = editorPlugin;
            this.ini = editorPlugin.GetConfig();

            CreateSettings();
            ini.Load();

            Width = ini.GetInt(SettingsSection, "placementsWidth");
            Height = ini.GetInt(SettingsSection, "placementsHeight");

            // Theme via Colors.*() accessors only (no raw ARGB literals; Color.Red is the sole allowed
            // raw literal, used for failure status).
            toolbar.BackColor = Colors.Primary();
            filterRow.BackColor = Colors.Primary();
            pnlStatus.BackColor = Colors.Primary();
            pnlFooter.BackColor = Colors.Primary();
            sep1.BackColor = Colors.Primary();
            lblFilter.ForeColor = Colors.Font();
            lblRowCount.ForeColor = Colors.FontDisabled();
            lblSelCount.ForeColor = Colors.FontDisabled();
            lblReloadBadge.ForeColor = Colors.FontDisabled();
            lblStatus.ForeColor = Colors.Font();
            lblFooter.ForeColor = Colors.FontDisabled();
            lblEmptyState.ForeColor = Colors.FontDisabled();

            lblReloadBadge.Text = ReloadBadgeCopy;
            lblFooter.Text = PreviewVsAuthorCopy;

            // Tooltips.
            toolTip.SetToolTip(btnMove, "Move the selected placements by an X/Y/Z delta.");
            toolTip.SetToolTip(btnDelete, "Delete the selected placements (undoable until you save).");
            toolTip.SetToolTip(btnRetemplate, "Swap the object template of the selected placements.");
            toolTip.SetToolTip(btnRefresh, "Re-read the placements from the loaded snapshot.");

            ConfigureGridColumns();

            btnMove.Click += OnMoveSelectedClicked;
            btnDelete.Click += OnDeleteSelectedClicked;
            btnRetemplate.Click += OnRetemplateSelectedClicked;
            btnRefresh.Click += (s, e) => RefreshTable();
            txtFilter.TextChanged += OnFilterChanged;
            txtFilter.KeyDown += OnFilterKeyDown;

            grid.SelectionChanged += OnGridSelectionChanged;
            grid.KeyDown += OnGridKeyDown;

            // No snapshot until SetSnapshot(...) is called by the panel.
            ApplyEmptyState();
        }

        // ── Settings ─────────────────────────────────────────────────────────

        private void CreateSettings()
        {
            ini.AddSetting(SettingsSection, "placementsWidth", "900", UtINI.Value.Types.VtInt);
            ini.AddSetting(SettingsSection, "placementsHeight", "600", UtINI.Value.Types.VtInt);
        }

        // ── Grid columns ──────────────────────────────────────────────────────

        private void ConfigureGridColumns()
        {
            grid.AutoGenerateColumns = false;
            grid.MultiSelect = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;

            var colId = new DataGridViewTextBoxColumn
            {
                Name = "colId",
                HeaderText = "Id",
                Width = 90,
            };
            colId.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            var colTemplate = new DataGridViewTextBoxColumn
            {
                Name = "colTemplate",
                HeaderText = "Object template",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 100,
            };

            var colCell = new DataGridViewTextBoxColumn
            {
                Name = "colCell",
                HeaderText = "Cell",
                Width = 120,
            };

            var colPosition = new DataGridViewTextBoxColumn
            {
                Name = "colPosition",
                HeaderText = "Position",
                Width = 180,
            };

            grid.Columns.AddRange(colId, colTemplate, colCell, colPosition);
        }

        // ── Public surface driven by the SnapshotPanel ─────────────────────────

        /// <summary>
        /// Called by the panel when a snapshot is loaded / reloaded. Re-baselines the table to the
        /// named snapshot and re-reads the native node list. Pass null/empty to clear (unload).
        /// </summary>
        public void SetSnapshot(string name)
        {
            snapshotName = name ?? "";
            hasSnapshot = !string.IsNullOrEmpty(snapshotName);
            UpdateTitle();

            if (hasSnapshot)
            {
                RefreshTable();
            }
            else
            {
                allRows.Clear();
                ApplyEmptyState();
            }
        }

        private void UpdateTitle()
        {
            this.Text = hasSnapshot
                ? "Snapshot Placements — " + snapshotName
                : "Snapshot Placements";
        }

        // ── Read the native node list (GAME THREAD) → populate the grid (UI THREAD) ──

        /// <summary>
        /// Re-reads the placements from the loaded snapshot. The native read runs on the SWG game
        /// thread (allocator-safety); the captured plain rows marshal back to the UI thread to bind.
        /// </summary>
        public void RefreshTable()
        {
            if (!hasSnapshot)
            {
                ApplyEmptyState();
                return;
            }

            SetStatus("Reading placements…", false);

            UtinniCoreDotNet.Callbacks.GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                var captured = new List<PlacementRow>();
                int generation = 0;
                try
                {
                    if (WorldSnapshotLive.IsAvailable)
                    {
                        // Goal B Wave 1 (v17): the advertised client reads the ENGINE-loaded live
                        // snapshot through the id-keyed WorldSnapshotLive rows (the raw reader below is
                        // SWGEmu-only 2002-layout). Top-level enumeration is authored-only by contract —
                        // counts are SMALLER than SWGEmu raw walks (buildout rows are filtered), which
                        // is the provenance contract working, not missing data.
                        generation = WorldSnapshotLive.Generation;
                        int count = WorldSnapshotLive.TopNodeCount;
                        for (int i = 0; i < count; i++)
                        {
                            long id = WorldSnapshotLive.GetTopNodeIdAt(i);
                            if (id == 0)
                            {
                                continue;
                            }

                            using (var info = new WorldSnapshotNodeInfo())
                            {
                                if (!WorldSnapshotLive.GetNodeInfo(id, info))
                                {
                                    continue;
                                }

                                var pos = info.Transform.Position;
                                captured.Add(new PlacementRow
                                {
                                    Id = id,
                                    ParentId = info.ContainedById,
                                    ObjectTemplate = WorldSnapshotLive.GetNodeTemplateName(id) ?? "",
                                    Cell = ResolveCellName(info.ContainedById),
                                    X = pos.X,
                                    Y = pos.Y,
                                    Z = pos.Z,
                                });
                            }
                        }
                    }
                    else
                    {
                        var rw = WorldSnapshotReaderWriter.Get();
                        if (rw != null)
                        {
                            int count = rw.NodeCount;
                            for (int i = 0; i < count; i++)
                            {
                                var node = rw.GetNodeAt(i);
                                if (node == null)
                                {
                                    continue;
                                }

                                var pos = node.Transform.Position;
                                captured.Add(new PlacementRow
                                {
                                    Id = node.Id,
                                    ParentId = node.ParentId,
                                    ObjectTemplate = node.ObjectTemplateName ?? "",
                                    Cell = ResolveCellName(node.ParentId),
                                    X = pos.X,
                                    Y = pos.Y,
                                    Z = pos.Z,
                                });
                            }
                        }
                    }
                }
                catch
                {
                    // A read failure must never crash the game thread; surface as a UI status below.
                    captured = null;
                }

                // Marshal back to the UI thread to bind.
                if (IsHandleCreated)
                {
                    BeginInvoke((Action)(() => OnRowsCaptured(captured, generation)));
                }
            });
        }

        // World-cell placements (parentId 0) have no cell name. For child nodes we surface the parent
        // id as a stable hint (a full parent-id→cell-name table is a polish follow-up — the column is
        // honest about what it shows).
        private static string ResolveCellName(long parentId)
        {
            return parentId == 0 ? "" : "cell " + parentId.ToString(CultureInfo.InvariantCulture);
        }

        private void OnRowsCaptured(List<PlacementRow> captured, int generation)
        {
            allRows.Clear();
            if (captured == null)
            {
                SetStatus("Couldn't read the snapshot placements.", true);
                ApplyEmptyState();
                return;
            }

            allRows.AddRange(captured);
            ApplyLoadedState();
            BindGrid();
            // The generation suffix (advertised client only) makes snapshot-boundary invalidation
            // visible: it changes when the engine load/unload/clears the snapshot — compare !=, not +1.
            SetStatus(
                generation > 0
                    ? allRows.Count + " placements loaded (gen " + generation.ToString(CultureInfo.InvariantCulture) + ")."
                    : allRows.Count + " placements loaded.",
                false);
        }

        // ── Grid binding + filter ──────────────────────────────────────────────

        private void BindGrid()
        {
            string filter = (txtFilter.Text ?? "").Trim();
            grid.SelectionChanged -= OnGridSelectionChanged; // Pattern 2: detach around programmatic mutation
            grid.Rows.Clear();

            int shown = 0;
            foreach (var row in allRows)
            {
                if (!MatchesFilter(row, filter))
                {
                    continue;
                }

                int idx = grid.Rows.Add(
                    row.Id.ToString(CultureInfo.InvariantCulture),
                    row.ObjectTemplate,
                    row.Cell,
                    FormatPosition(row));
                grid.Rows[idx].Tag = row.Id; // stable selection key
                shown++;
            }

            grid.ClearSelection();
            grid.SelectionChanged += OnGridSelectionChanged;

            lblRowCount.Text = shown + " / " + allRows.Count + " nodes";
            UpdateSelectionCount();
        }

        private static string FormatPosition(PlacementRow row)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.0}, {1:0.0}, {2:0.0})", row.X, row.Y, row.Z);
        }

        private static bool MatchesFilter(PlacementRow row, string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            string f = filter.ToLowerInvariant();
            return row.Id.ToString(CultureInfo.InvariantCulture).Contains(f)
                || (row.ObjectTemplate != null && row.ObjectTemplate.ToLowerInvariant().Contains(f))
                || (row.Cell != null && row.Cell.ToLowerInvariant().Contains(f));
        }

        private void OnFilterChanged(object sender, EventArgs e)
        {
            if (hasSnapshot)
            {
                BindGrid();
            }
        }

        private void OnFilterKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                txtFilter.Text = "";
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        // ── Selection sync (Pattern 2 — single-row drives the gizmo) ───────────

        private void OnGridSelectionChanged(object sender, EventArgs e)
        {
            int selected = grid.SelectedRows.Count;
            UpdateSelectionCount();

            // A single-row selection drives the shipped gizmo + per-node panel controls. A
            // multi-selection only updates the count (the single-node gizmo is NOT driven).
            // (On the advertised client the raw-reader select degrades to a no-op — selection-driven
            // gizmo/controls are Wave-2 territory; the Wave-1 table is read/browse only.)
            if (selected == 1)
            {
                var tag = grid.SelectedRows[0].Tag;
                if (tag is long nodeId)
                {
                    worldSnapshot.SelectNodeById(nodeId);
                }
            }
        }

        private void UpdateSelectionCount()
        {
            lblSelCount.Text = grid.SelectedRows.Count + " selected";
        }

        private List<int> GetSelectedIds()
        {
            // Bulk ops still take int32 ids (the SWGEmu-era raw-reader path; they no-op per-id on the
            // advertised client until Wave 2). Authored ids round-trip int32 by the save contract, so
            // the narrowing is lossless for every row this table can show.
            var ids = new List<int>();
            foreach (DataGridViewRow row in grid.SelectedRows)
            {
                if (row.Tag is long id)
                {
                    ids.Add(unchecked((int)id));
                }
            }
            return ids;
        }

        private void OnGridKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                grid.SelectAll();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete)
            {
                OnDeleteSelectedClicked(sender, EventArgs.Empty);
                e.Handled = true;
            }
        }

        // ── 15-10 GAP 1.3: route Ctrl+Z / Ctrl+Y from this child window to the EDITOR undo manager ──
        //
        // The single shared UndoRedoManager lives in FormMain; this plugin form reaches it through the
        // IEditorPlugin.Undo / .Redo seam (wired by FormMain, null-checked here). Without this override
        // Ctrl+Z in the placements window was a silent no-op.
        //
        // ORDERING (FIFO update-loop): the WS undo command bodies marshal their work through
        // GroundSceneCallbacks.AddUpdateLoopCall (game thread), and RefreshTable() ALSO enqueues its
        // native node-list re-read on the SAME update loop. So we invoke Undo FIRST (enqueues the
        // command body) and call RefreshTable() SECOND (enqueues the re-read) — FIFO guarantees the
        // grid is re-read AFTER the undo applies, reflecting the reverted state, not a stale pre-undo
        // snapshot. We never refresh before the undo is enqueued, and never refresh synchronously
        // outside the update loop.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z))
            {
                editorPlugin.Undo?.Invoke(); // enqueues the undo command body on the update loop
                RefreshTable();              // enqueued AFTER → re-reads the post-undo node list (FIFO)
                return true;
            }

            if (keyData == (Keys.Control | Keys.Y))
            {
                editorPlugin.Redo?.Invoke(); // enqueues the redo command body on the update loop
                RefreshTable();              // enqueued AFTER → re-reads the post-redo node list (FIFO)
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ── Bulk operations ────────────────────────────────────────────────────

        private void OnMoveSelectedClicked(object sender, EventArgs e)
        {
            var ids = GetSelectedIds();
            if (ids.Count == 0)
            {
                SetStatus("Select one or more placements to move.", false);
                return;
            }

            using (var dlg = new FormSnapshotBulkMoveDialog(ids.Count))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                worldSnapshot.BulkMove(ids, dlg.DeltaX, dlg.DeltaY, dlg.DeltaZ);
                SetStatus("Moved " + ids.Count + " placements.", false);
                ScheduleRefresh();
            }
        }

        private void OnDeleteSelectedClicked(object sender, EventArgs e)
        {
            var ids = GetSelectedIds();
            if (ids.Count == 0)
            {
                SetStatus("Select one or more placements to delete.", false);
                return;
            }

            using (var dlg = new FormSaveConfirmDialog(
                "Delete " + ids.Count + " placements?",
                "This removes " + ids.Count + " object placements from the snapshot. This is undoable in the editor until you save."
                    + " (Live-mutation clients despawn the object immediately; on SWGEmu it stays visible until the next scene change.)",
                "Delete",
                "Cancel"))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK ||
                    dlg.Outcome != FormSaveConfirmDialog.ConfirmOutcome.Accepted)
                {
                    return;
                }

                worldSnapshot.BulkDelete(ids);
                // The delete runs async on the game thread; the per-outcome result arrives as a
                // SysMsg (removed / occupied / not-in-snapshot / engine-miss counts). Don't claim
                // success here -- the old "Deleted N placements." masked a fully-silent no-op.
                SetStatus("Delete requested for " + ids.Count + " placements -- result arrives as a system message.", false);
                ScheduleRefresh();
            }
        }

        private void OnRetemplateSelectedClicked(object sender, EventArgs e)
        {
            var ids = GetSelectedIds();
            if (ids.Count == 0)
            {
                SetStatus("Select one or more placements to retemplate.", false);
                return;
            }

            using (var dlg = new FormSnapshotBulkRetemplateDialog(ids.Count))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(dlg.NewTemplate))
                {
                    return;
                }

                worldSnapshot.BulkRetemplate(ids, dlg.NewTemplate);
                SetStatus("Retemplated " + ids.Count + " placements to " + dlg.NewTemplate + ".", false);
                ScheduleRefresh();
            }
        }

        // The bulk op lands on a later game-frame; re-read the table shortly after so the grid
        // reflects the mutation. Kept simple (a one-shot timer) — no per-frame polling.
        private void ScheduleRefresh()
        {
            var timer = new Timer { Interval = 250 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                RefreshTable();
            };
            timer.Start();
        }

        // ── States ──────────────────────────────────────────────────────────────

        private void ApplyEmptyState()
        {
            hasSnapshot = hasSnapshot && allRows != null;
            bool enabled = hasSnapshot;

            btnMove.Enabled = enabled;
            btnDelete.Enabled = enabled;
            btnRetemplate.Enabled = enabled;
            btnRefresh.Enabled = enabled;
            txtFilter.Enabled = enabled;

            lblReloadBadge.Visible = enabled;
            lblRowCount.Text = "0 / 0 nodes";
            lblSelCount.Text = "0 selected";

            lblEmptyState.Text = EmptyHeading + Environment.NewLine + EmptyBody;
            lblEmptyState.Visible = !enabled;
            grid.Visible = enabled;
        }

        private void ApplyLoadedState()
        {
            btnMove.Enabled = true;
            btnDelete.Enabled = true;
            btnRetemplate.Enabled = true;
            btnRefresh.Enabled = true;
            txtFilter.Enabled = true;
            lblReloadBadge.Visible = true;
            lblEmptyState.Visible = false;
            grid.Visible = true;
        }

        private void SetStatus(string text, bool isError)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = isError ? Color.Red : Colors.Font();
        }

        // ── Singleton hide-not-dispose ────────────────────────────────────────

        private void FormSnapshotPlacements_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                ini.AddSetting(SettingsSection, "placementsWidth", Width.ToString(CultureInfo.InvariantCulture), UtINI.Value.Types.VtInt);
                ini.AddSetting(SettingsSection, "placementsHeight", Height.ToString(CultureInfo.InvariantCulture), UtINI.Value.Types.VtInt);
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
