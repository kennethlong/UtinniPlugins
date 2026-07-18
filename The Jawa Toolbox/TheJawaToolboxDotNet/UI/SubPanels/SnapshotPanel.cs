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
using System.Windows.Forms;
using TJT.SWG;
using TJT.UI.Forms;
using UtinniCore.Swg.Math;
using UtinniCore.Utinni;
using UtinniCoreDotNet.Hotkeys;
using UtinniCoreDotNet.PluginFramework;
using UtinniCoreDotNet.UI.Controls;

namespace TJT.UI.SubPanels
{
    public interface ISnapshotPanel : ISceneAvailability
    {
        void SetCmbSnapshots(List<string> snapshots);
        void UpdateNodeEditingMode(bool enable);
        void UpdateSelectedNodeControls(WorldSnapshotReaderWriter.Node node, string cellName = "", string typeText = "");
        // Wave 2 (v18): the id-keyed selected-node state for the advertised client's live
        // snapshot (no native Node exists there).
        void UpdateSelectedNodeControlsLive(long id, long parentId, string templateName, float radius, Vector position);
        void UpdateSelectedNodeControlsPosition(Vector position);
        void UpdateGizmoModeControls(bool value);
        void UpdateGizmoOperationControls(bool value);
        void UpdateGizmoSnapControl(bool value);
    }

    public partial class SnapshotPanel : SubPanel, ISnapshotPanel
    {
        private readonly WorldSnapshotImpl worldSnapshot;

        private readonly UtINI ini;

        // 15-01: the plugin host + the companion placements-table window (singleton per editor session;
        // hide-not-dispose so a re-launch re-Shows the same instance). The placements window is a VIEW
        // over the SAME loaded snapshot this panel drives.
        private readonly IEditorPlugin editorPlugin;
        private FormSnapshotPlacements placementsForm;

        // 15-01: the snapshot the panel has actually loaded into the scene (null = none). Drives the
        // placements window's loaded-vs-empty state honestly (a name selected in the combo is NOT the
        // same as a loaded snapshot).
        private string loadedSnapshotName;

        public SnapshotPanel(IEditorPlugin editorPlugin, HotkeyManager hotkeyManager, UtINI ini) : base("Snapshot")
        {
            InitializeComponent();

            this.editorPlugin = editorPlugin;
            worldSnapshot = new WorldSnapshotImpl(this, editorPlugin, hotkeyManager);

            this.ini = ini;

            CreateSettings();
            ini.Load();

            txtNewNodeFilename.Text = ini.GetString("Snapshot", "defaultNodeObjectFilename");
            chkEnableNodeEditing.Checked = ini.GetBool("Snapshot", "autoEnableSnapshotEditing");
            chkAllowTargetEverything.Checked = ini.GetBool("Snapshot", "autoAllowTargetEverything");
        }

        private void CreateSettings()
        {
            ini.AddSetting("Snapshot", "defaultSnapshotName", "naboo", UtINI.Value.Types.VtString);
            ini.AddSetting("Snapshot", "defaultNodeObjectFilename", "object/tangible/furniture/cheap/shared_armoire_s01.iff", UtINI.Value.Types.VtString);
            ini.AddSetting("Snapshot", "autoEnableSnapshotEditing", "false", UtINI.Value.Types.VtBool);
            ini.AddSetting("Snapshot", "autoAllowTargetEverything", "false", UtINI.Value.Types.VtBool);
        }

        private void btnLoad_Click(object sender, EventArgs e)
        {
            string name = cmbSnapshots.Items[cmbSnapshots.SelectedIndex].ToString();
            worldSnapshot.Load(name);
            loadedSnapshotName = name;

            // Re-baseline the companion placements table to the newly-loaded snapshot (if open). The
            // load lands on a later game-frame; SetSnapshot triggers a (game-thread) re-read.
            if (placementsForm != null && placementsForm.Visible)
            {
                placementsForm.SetSnapshot(name);
            }
        }

        private void btnUnload_Click(object sender, EventArgs e)
        {
            worldSnapshot.Unload();
            loadedSnapshotName = null;

            if (placementsForm != null && placementsForm.Visible)
            {
                placementsForm.SetSnapshot(null); // clears the table; closing the window does NOT unload
            }
        }

        // 15-01: open the companion resizable placements-table window. Singleton per editor session —
        // already open → Activate(); otherwise create + Show(). The window hosts the flat placements
        // table + multi-select bulk move/delete/retemplate over the loaded snapshot's native node list.
        private void btnPlacements_Click(object sender, EventArgs e)
        {
            if (placementsForm == null || placementsForm.IsDisposed)
            {
                placementsForm = new FormSnapshotPlacements(worldSnapshot, editorPlugin);
            }

            // Pass the ACTUALLY-loaded snapshot (not merely the combo selection) so the window honestly
            // shows the empty-state until a snapshot is loaded from this panel. On the advertised
            // client the ENGINE loads the current scene's snapshot itself (this panel's Load flow is
            // SWGEmu-only), so when a scene is active the table reads that live snapshot instead
            // (Goal B Wave 1 — WorldSnapshotLive id-keyed rows).
            placementsForm.SetSnapshot(ResolveEffectiveSnapshotName());

            if (placementsForm.Visible)
            {
                placementsForm.Activate();
            }
            else
            {
                placementsForm.Show();
            }
        }

        private void btnReload_Click(object sender, EventArgs e)
        {
            worldSnapshot.Reload();
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            worldSnapshot.Save();
        }

        private void btnSaveAs_Click(object sender, EventArgs e)
        {
            FormSnapshotSaveAsDialog form = new FormSnapshotSaveAsDialog(GroundScene.Get().Name)
            {
                Location = new System.Drawing.Point(MousePosition.X - 200, MousePosition.Y - 40)
            };

            DialogResult dialogResult = form.ShowDialog(this);
            if (dialogResult == DialogResult.OK)
            {
                worldSnapshot.SaveAs(form.SaveAsName);
            }
        }

        private void btnAddNode_Click(object sender, EventArgs e)
        {
            worldSnapshot.AddNode(txtNewNodeFilename.Text);
        }

        // Duplicate the currently look-at-targeted snapshot node. A button (not just the Ctrl+D
        // hotkey) because keyboard hotkeys don't reach the overlay on the advertised client -- the
        // embedded game window consumes them (Ctrl+D is also the game's character-rotate). Mirrors
        // the Add Node button path; DuplicateNode itself is target-gated + game-thread marshaled.
        private void btnDuplicateNode_Click(object sender, EventArgs e)
        {
            worldSnapshot.DuplicateNode();
        }

        private void btnRemoveNode_Click(object sender, EventArgs e)
        {
            worldSnapshot.RemoveNode();
        }

        private void chkEnableNodeEditing_CheckedChanged(object sender, EventArgs e)
        {
            worldSnapshot.UpdateNodeEditingMode(chkEnableNodeEditing.Checked);
        }

        private void nudNodePos_ValueChanged(object sender, EventArgs e)
        {
            worldSnapshot.SetSelectedNodePosition((float)nudNodePosX.Value, (float)nudNodePosY.Value, (float)nudNodePosZ.Value);
        }

        private void nudNodeRadius_ValueChanged(object sender, EventArgs e)
        {
            worldSnapshot.SetRadius((float)nudNodeRadius.Value);
        }

        public void UpdateNodeEditingMode(bool enable)
        {
            chkEnableNodeEditing.CheckedChanged -= chkEnableNodeEditing_CheckedChanged;
            chkEnableNodeEditing.Checked = enable;
            chkEnableNodeEditing.CheckedChanged += chkEnableNodeEditing_CheckedChanged;
        }

        public void SetCmbSnapshots(List<string> snapshots)
        {
            cmbSnapshots.Items.AddRange(snapshots.ToArray());
            int index = cmbSnapshots.Items.IndexOf(ini.GetString("Snapshot", "defaultSnapshotName"));
            if (index >= 0)
            {
                cmbSnapshots.SelectedIndex = index;
            }
            else if (cmbSnapshots.Items.Count > 0)
            {
                // Phase 24 v4: guard against an empty snapshot list (advertised client pre-treefile
                // -> empty Repository); SelectedIndex=0 on an empty combo throws.
                cmbSnapshots.SelectedIndex = 0;
            }
        }

        private bool previousIsSceneActive;
        public void UpdateSceneAvailability(bool isSceneActive)
        {
            if (previousIsSceneActive == isSceneActive)
            {
                return;
            }

            cmbSnapshots.Enabled = isSceneActive;
            btnLoad.Enabled = isSceneActive;
            btnSave.Enabled = isSceneActive;
            btnSaveAs.Enabled = isSceneActive;
            btnReload.Enabled = isSceneActive;
            btnUnload.Enabled = isSceneActive;

            btnAddNode.Enabled = isSceneActive;
            btnDuplicateNode.Enabled = isSceneActive;

            previousIsSceneActive = isSceneActive;

            // Advertised client (Goal B Wave 1): the engine (un)loads the live snapshot with the
            // scene, so an open placements window re-baselines at every scene boundary — the table
            // re-reads on scene entry (generation bump visible in its status) and empties on exit.
            if (WorldSnapshotLive.IsAvailable && loadedSnapshotName == null &&
                placementsForm != null && !placementsForm.IsDisposed && placementsForm.Visible)
            {
                placementsForm.SetSnapshot(ResolveEffectiveSnapshotName());
            }
        }

        // The snapshot name the placements window should show: the panel-loaded snapshot when one
        // exists (SWGEmu flow), else — advertised client — the engine-loaded live snapshot marker.
        // Null = nothing loaded (honest empty-state). NOT gated on previousIsSceneActive: on the
        // advertised client the scene-active callback fires only through the TJT loadScene path
        // (setupScene is un-detourable there), so a NORMAL login never arms it — the live-read
        // itself is the honest probe (0 rows + gen 0 when no scene is up, real rows once in-world).
        private string ResolveEffectiveSnapshotName()
        {
            if (loadedSnapshotName != null)
            {
                return loadedSnapshotName;
            }

            if (WorldSnapshotLive.IsAvailable)
            {
                return "(live scene)";
            }

            return null;
        }

        public void EnableSelectedNodeControls(bool value)
        {
            nudNodePosX.Enabled = value;
            nudNodePosY.Enabled = value;
            nudNodePosZ.Enabled = value;

            btnRotationPitchawAdd45.Enabled = value;
            btnRotationPitchawAdd1.Enabled = value;
            btnRotationPitchawSub1.Enabled = value;
            btnRotationPitchawSub45.Enabled = value;

            btnRotationPitchAdd45.Enabled = value;
            btnRotationPitchAdd1.Enabled = value;
            btnRotationPitchSub1.Enabled = value;
            btnRotationPitchSub45.Enabled = value;

            btnRotationRollAdd45.Enabled = value;
            btnRotationRollAdd1.Enabled = value;
            btnRotationRollSub1.Enabled = value;
            btnRotationRollSub45.Enabled = value;

            btnRotationYawRandom.Enabled = value;
            btnRotationPitchRandom.Enabled = value;
            btnRotationRollRandom.Enabled = value;

            btnRotationReset.Enabled = value;

            nudNodeRadius.Enabled = value;
            btnRemoveSelectedNode.Enabled = value;

            //chkSnap.Checked = IsSnap
            chkSnap.Enabled = value;
            nudSnapScale.Enabled = value;

            chkbtnOperation.Enabled = value;
            chkbtnMode.Enabled = value;
        }

        public void UpdateSelectedNodeControls(WorldSnapshotReaderWriter.Node node, string cellName, string typeText)
        {
            if (node == null)
            {
                EnableSelectedNodeControls(false);

                UpdateSelectedNodeControlsPosition(new Vector(0, 0, 0));

                txtNodeId.Text = "";
                txtNodeParentId.Text = "";
                txtNodeFilename.Text = "";
                return;
            }

            EnableSelectedNodeControls(chkEnableNodeEditing.Checked);

            nudNodeRadius.Value = (decimal) node.Radius;

            txtNodeId.Text = node.Id.ToString();
            txtNodeParentId.Text = node.ParentId.ToString();
            txtNodeCellName.Text = cellName;
            txtNodeFilename.Text = node.ObjectTemplateName;

            txtNodeType.Text = typeText;

            UpdateSelectedNodeControlsPosition(node.Transform.Position);
        }

        // Wave 2 (v18): live-snapshot flavor of UpdateSelectedNodeControls — same control set,
        // fed from id-keyed scalars instead of a native Node. Cell name is the parent-id hint
        // (raw cell-name reads are not advertised-safe); type text stays empty for the same
        // reason. The radius handler detaches around the programmatic set so a target change
        // doesn't enqueue a redundant live radius write.
        public void UpdateSelectedNodeControlsLive(long id, long parentId, string templateName, float radius, Vector position)
        {
            EnableSelectedNodeControls(chkEnableNodeEditing.Checked);

            nudNodeRadius.ValueChanged -= nudNodeRadius_ValueChanged;
            nudNodeRadius.Value = (decimal)radius;
            nudNodeRadius.ValueChanged += nudNodeRadius_ValueChanged;

            txtNodeId.Text = id.ToString();
            txtNodeParentId.Text = parentId.ToString();
            txtNodeCellName.Text = parentId != 0 ? "cell " + parentId : "";
            txtNodeFilename.Text = templateName;
            txtNodeType.Text = "";

            UpdateSelectedNodeControlsPosition(position);
        }

        public void UpdateSelectedNodeControlsPosition(Vector position)
        {
            nudNodePosX.ValueChanged -= nudNodePos_ValueChanged;
            nudNodePosY.ValueChanged -= nudNodePos_ValueChanged;
            nudNodePosZ.ValueChanged -= nudNodePos_ValueChanged;

            nudNodePosX.Value = (decimal)position.X;
            nudNodePosY.Value = (decimal)position.Y;
            nudNodePosZ.Value = (decimal)position.Z;

            nudNodePosX.ValueChanged += nudNodePos_ValueChanged;
            nudNodePosY.ValueChanged += nudNodePos_ValueChanged;
            nudNodePosZ.ValueChanged += nudNodePos_ValueChanged;
        }

        private void btnRotationPitchawAdd45_Click(object sender, EventArgs e)
        {
            worldSnapshot.RotateYaw(45);
        }

        private void btnRotationPitchawSub45_Click(object sender, EventArgs e)
        {
            worldSnapshot.RotateYaw(-45);
        }

        private void btnRotationPitchawAdd1_Click(object sender, EventArgs e)
        {
            worldSnapshot.RotateYaw(1);
        }

        private void btnRotationPitchawSub1_Click(object sender, EventArgs e)
        {
            worldSnapshot.RotateYaw(-1);
        }

        private void btnRotationPitchAdd45_Click(object sender, EventArgs e)
        {
            worldSnapshot.RotatePitch(45);
        }

        private void btnRotationPitchSub45_Click(object sender, EventArgs e)
        {
            worldSnapshot.RotatePitch(-45);
        }

        private void btnRotationPitchAdd1_Click(object sender, EventArgs e)
        {
            worldSnapshot.RotatePitch(1);
        }

        private void btnRotationPitchSub1_Click(object sender, EventArgs e)
        {
            worldSnapshot.RotatePitch(-1);
        }

        private void btnRotationRollAdd45_Click(object sender, EventArgs e)
        {
            worldSnapshot.RotateRoll(45);
        }

        private void btnRotationRollSub45_Click(object sender, EventArgs e)
        {
            worldSnapshot.RotateRoll(-45);
        }

        private void btnRotationRollAdd1_Click(object sender, EventArgs e)
        {
            worldSnapshot.RotateRoll(1);
        }

        private void btnRotationRollSub1_Click(object sender, EventArgs e)
        {
            worldSnapshot.RotateRoll(-1);
        }

        private void btnRotationYawRandom_Click(object sender, EventArgs e)
        {
            Random rnd = new Random();
            worldSnapshot.RotateYaw(rnd.Next(-180, 180));
        }

        private void btnRotationPitchRandom_Click(object sender, EventArgs e)
        {
            Random rnd = new Random();
            worldSnapshot.RotatePitch(rnd.Next(-180, 180));
        }

        private void btnRotationRollRandom_Click(object sender, EventArgs e)
        {
            Random rnd = new Random();
            worldSnapshot.RotateRoll(rnd.Next(-180, 180));
        }

        private void btnRotationReset_Click(object sender, EventArgs e)
        {
            worldSnapshot.ResetRotation();
        }

        private void chkSnap_CheckedChanged(object sender, EventArgs e)
        {
            worldSnapshot.SetGizmoSnap(chkSnap.Checked);
        }

        private void nudSnapScale_ValueChanged(object sender, EventArgs e)
        {
            worldSnapshot.SetSnapScale((float) nudSnapScale.Value);
        }

        private void chkbtnMode_CheckedChanged(object sender, EventArgs e)
        {
            if (chkbtnMode.Checked)
            {
                chkbtnMode.Text = "Local";
                worldSnapshot.SetGizmoToLocal();
            }
            else
            {
                chkbtnMode.Text = "World";
                worldSnapshot.SetGizmoToWorld();
            }
        }

        private void chkbtnOperation_CheckedChanged(object sender, EventArgs e)
        {
            if (chkbtnOperation.Checked)
            {
                chkbtnOperation.Text = "Translate";
                worldSnapshot.SetOperationModeToTranslate();
            }
            else
            {

                chkbtnOperation.Text = "Rotation";
                worldSnapshot.SetOperationModeToRotation();
            }
        }

        public void UpdateGizmoModeControls(bool value)
        {
            chkbtnMode.CheckedChanged -= chkbtnMode_CheckedChanged;
            chkbtnMode.Checked = value;
            chkbtnMode.CheckedChanged += chkbtnMode_CheckedChanged;

            if (value)
            {
                chkbtnMode.Text = "Local";
            }
            else
            {

                chkbtnMode.Text = "World";
            }
        }

        public void UpdateGizmoOperationControls(bool value)
        {

            chkbtnOperation.CheckedChanged -= chkbtnOperation_CheckedChanged;
            chkbtnOperation.Checked = value;
            chkbtnOperation.CheckedChanged += chkbtnOperation_CheckedChanged;

            if (value)
            {
                chkbtnOperation.Text = "Translate";
            }
            else
            {

                chkbtnOperation.Text = "Rotation";
            }
        } 

        public void UpdateGizmoSnapControl(bool value)
        {
            chkSnap.CheckedChanged -= chkSnap_CheckedChanged;
            chkSnap.Checked = value;
            chkSnap.CheckedChanged += chkSnap_CheckedChanged;
        }

        private void chkAllowTargetEverything_CheckedChanged(object sender, EventArgs e)
        {
            worldSnapshot.AllowTargetEverything(chkAllowTargetEverything.Checked);
        }
    }

}
