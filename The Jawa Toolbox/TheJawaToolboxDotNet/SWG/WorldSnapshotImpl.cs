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

using System.Collections.Generic;
using System.IO;
using TJT.UI.SubPanels;
using UtinniCore.ImguiGizmo;
using UtinniCore.Swg.Math;
using UtinniCore.Utinni;
using UtinniCore.Utinni.CuiHud;
using UtinniCoreDotNet.Callbacks;
using UtinniCoreDotNet.Commands;
using UtinniCoreDotNet.Hotkeys;
using UtinniCoreDotNet.PluginFramework;

namespace TJT.SWG
{
    public class WorldSnapshotImpl
    {
        private readonly ISnapshotPanel snapshotPanel;
        private readonly IEditorPlugin editorPlugin;
        private readonly HotkeyManager hotkeyManager;

        public bool EnableNodeEditing;

        private WorldSnapshotReaderWriter.Node copiedNode;

        public WorldSnapshotImpl(ISnapshotPanel snapshotPanel, IEditorPlugin editorPlugin, HotkeyManager hotkeyManager)
        {
            this.snapshotPanel = snapshotPanel;
            this.editorPlugin = editorPlugin;
            this.hotkeyManager = hotkeyManager;

            GameCallbacks.AddInstallCallback(OnInstallCallback);
            GameCallbacks.AddSetupSceneCall(OnSetupSceneCallback);
            GameCallbacks.AddCleanupSceneCall(OnCleanupCallback);
            ObjectCallbacks.AddOnTargetCallback(OnTarget);

            ImGuiCallbacks.AddOnEnabledCallback(OnGizmoEnabled);
            ImGuiCallbacks.AddOnDisabledCallback(OnGizmoDisabled);
            ImGuiCallbacks.AddOnPositionChangedCallback(OnPositionChanged);
            ImGuiCallbacks.AddOnRotationChangedCallback(OnRotationChanged);

            hotkeyManager.Add(new Hotkey("ToggleSnapshotNodeEditingMode", "Toggle Snapshot Node Editing Mode", "Oemtilde", ToggleNodeEditing, true));
            hotkeyManager.Add(new Hotkey("SaveSnapshot", "Save Snapshot", "Control + S", Save, true));
            hotkeyManager.Add(new Hotkey("CopySnapshotNode", "Copy Snapshot Node", "Control + C", CopyNode, true, true, true));
            hotkeyManager.Add(new Hotkey("PasteSnapshotNode", "Paste Snapshot Node", "Control + V", PasteNode, true, true, true));
            hotkeyManager.Add(new Hotkey("DuplicateSnapshotNode", "Duplicate Snapshot Node", "Control + D", DuplicateNode, true, true, true));
            hotkeyManager.Add(new Hotkey("DeleteSnapshotNode", "Delete Snapshot Node", "Delete", RemoveNode, true, true, true));

            hotkeyManager.Add(new Hotkey("SetGizmoTranslateOperationMode", "Set Gizmo Operation Mode to Translate", "Control + Q", SetOperationModeToTranslateHotkey, true, false, true));
            hotkeyManager.Add(new Hotkey("SetGizmoRotationOperationMode", "Set Gizmo Operation Mode to Rotation", "Control + E", SetOperationModeToRotationHotkey, true, false, true));
            hotkeyManager.Add(new Hotkey("ToggleGizmoSnap", "Toggle Gizmo Snap", "Control + B", ToggleGizmoSnapHotkey, true, false, true));
        }

        private void OnInstallCallback()
        {
            var dirInfo = Game.Repository.GetDirectoryInfo("snapshot");

            List<string> snapshots = new List<string>();

            for (int i = 0; i < dirInfo.Size; i++)
            {
                string snapshotFile = Game.Repository.GetFilenameAt(dirInfo.StartIndex + i);

                if (snapshotFile.EndsWith(".ws"))
                {
                    snapshots.Add(Path.GetFileNameWithoutExtension(snapshotFile));
                }
            }

            snapshotPanel.SetCmbSnapshots(snapshots);
        }

        private void OnSetupSceneCallback()
        {
            snapshotPanel.UpdateSceneAvailability(true);
        }

        private void OnCleanupCallback()
        {
            snapshotPanel.UpdateSceneAvailability(false);
        }

        public void Load(string filename)
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                WorldSnapshot.Load(filename);
            });
        }

        public void Unload()
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                WorldSnapshot.Unload();
            });
        }

        public void Reload()
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                WorldSnapshot.Reload();
            });
        }

        public void Save()
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                WorldSnapshotReaderWriter.Get().SaveFile("");
            });
        }

        public void SaveAs(string snapshotName)
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                WorldSnapshotReaderWriter.Get().SaveFile(snapshotName);
            });
        }

        public void AddNode(string objectFilename)
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                var node = WorldSnapshot.CreateAddNode(objectFilename, Game.Player.ObjectToParent);
                if (node != null)
                {
                    editorPlugin.AddUndoCommand(this, new AddUndoCommandEventArgs(new AddWorldSnapshotNodeCommand(node)));
                }
            });
        }

        public void RemoveNode()
        {
            if (EnableNodeEditing)
            {
                GroundSceneCallbacks.AddUpdateLoopCall(() =>
                {
                    var obj = Game.PlayerLookAtTargetObject;
                    if (obj != null)
                    {
                        var node = WorldSnapshotReaderWriter.Get().GetNodeById((int)obj.NetworkId, obj.ParentObject);
                        if (node != null)
                        {
                            editorPlugin.AddUndoCommand(this, new AddUndoCommandEventArgs(new RemoveWorldSnapshotNodeCommand(node)));
                            WorldSnapshot.RemoveNode(node);
                        }
                    }
                });
            }
        }

        // ── 15-01 WorldSnapshot bulk operations (PROD-W2-WS / D-01/D-02) ──────────
        //
        // Each bulk method derives an ordered composition plan via the pure framework
        // helper WorldSnapshotBulkComposer (BCL-only; no native/WinForms refs), then
        // enqueues EXACTLY ONE GroundSceneCallbacks.AddUpdateLoopCall that iterates the
        // plan and composes the shipped per-node WorldSnapshotCommands. The whole bulk
        // therefore lands atomically on one game-frame and is undoable (each shipped
        // command snapshots the node + pushes through AddUndoCommand). NEVER mutate the
        // snapshot on the WinForms thread — that corrupts SWG's allocator (Pattern 1,
        // project_rh_snapshot_no_heap_alloc). Zero new format code (D-02): the table +
        // these ops are a new view over the native WorldSnapshotReaderWriter node list.

        public void BulkMove(IEnumerable<int> selectedIds, float dx, float dy, float dz)
        {
            var plan = WorldSnapshotBulkComposer.ComposeMove(selectedIds, dx, dy, dz);

            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                foreach (var descriptor in plan)
                {
                    var node = WorldSnapshotReaderWriter.Get().GetNodeById(descriptor.NodeId);
                    if (node == null)
                    {
                        continue;
                    }

                    var newTransform = new Transform(node.Transform);
                    newTransform.SetPosition(
                        node.Transform.Position.X + descriptor.DeltaX,
                        node.Transform.Position.Y + descriptor.DeltaY,
                        node.Transform.Position.Z + descriptor.DeltaZ);

                    editorPlugin.AddUndoCommand(this,
                        new AddUndoCommandEventArgs(new WorldSnapshotNodePositionChangedCommand(node, node.Transform, newTransform)));

                    var obj = Network.GetObjectById(node.Id);
                    if (obj != null)
                    {
                        obj.Transform.Position = newTransform.Position;
                        obj.PositionAndRotationChanged(false, newTransform.Position);
                    }

                    node.Transform.Position = newTransform.Position;
                }

                WorldSnapshot.DetailLevelChanged();
            });
        }

        public void BulkDelete(IEnumerable<int> selectedIds)
        {
            var plan = WorldSnapshotBulkComposer.ComposeDelete(selectedIds);

            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                foreach (var descriptor in plan)
                {
                    var node = WorldSnapshotReaderWriter.Get().GetNodeById(descriptor.NodeId);
                    if (node == null)
                    {
                        continue;
                    }

                    editorPlugin.AddUndoCommand(this,
                        new AddUndoCommandEventArgs(new RemoveWorldSnapshotNodeCommand(node)));
                    WorldSnapshot.RemoveNode(node);
                }
            });
        }

        public void BulkRetemplate(IEnumerable<int> selectedIds, string newTemplate)
        {
            // Retemplate composes a Remove + Add PAIR per node (the helper plan is already
            // ordered Remove-then-Add per node). We capture each node's transform BEFORE
            // removing it so the freshly-added new-template node lands in the same spot.
            var plan = WorldSnapshotBulkComposer.ComposeRetemplate(selectedIds, newTemplate);

            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                Transform pendingTransform = null;

                foreach (var descriptor in plan)
                {
                    if (descriptor.Kind == WorldSnapshotBulkOpKind.Remove)
                    {
                        var node = WorldSnapshotReaderWriter.Get().GetNodeById(descriptor.NodeId);
                        if (node == null)
                        {
                            pendingTransform = null;
                            continue;
                        }

                        pendingTransform = new Transform(node.Transform);
                        editorPlugin.AddUndoCommand(this,
                            new AddUndoCommandEventArgs(new RemoveWorldSnapshotNodeCommand(node)));
                        WorldSnapshot.RemoveNode(node);
                    }
                    else // Add (the retemplate half)
                    {
                        var newNode = WorldSnapshot.CreateAddNode(descriptor.NewObjectTemplate, Game.Player.ObjectToParent);
                        if (newNode == null)
                        {
                            continue;
                        }

                        if (pendingTransform != null)
                        {
                            newNode.Transform.Position = pendingTransform.Position;
                            newNode.Transform.CopyRotation(pendingTransform);
                        }

                        editorPlugin.AddUndoCommand(this,
                            new AddUndoCommandEventArgs(new AddWorldSnapshotNodeCommand(newNode)));
                    }
                }

                WorldSnapshot.DetailLevelChanged();
            });
        }

        // 15-01: drive the shipped gizmo + per-node panel controls for a single placements-table row
        // selection. Mirrors OnTarget's node→controls path but keyed by the table's node id rather than
        // the in-world look-at target. Runs on the game thread (gizmo enable + control update). When the
        // node's in-world object is resolvable we enable the gizmo on it; the panel controls always
        // update so the modder sees the selected node's id / template / position.
        public void SelectNodeById(int nodeId)
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                var node = WorldSnapshotReaderWriter.Get().GetNodeById(nodeId);
                if (node == null)
                {
                    return;
                }

                var obj = Network.GetObjectById(node.Id);
                if (obj != null && EnableNodeEditing)
                {
                    EnableGizmo(obj);
                }

                string cellName = node.ParentId != 0 ? "cell " + node.ParentId : "";
                snapshotPanel.UpdateSelectedNodeControls(node, cellName, "");
            });
        }

        public void ToggleNodeEditing()
        {
            bool result = !EnableNodeEditing;
            snapshotPanel.UpdateNodeEditingMode(result);
            UpdateNodeEditingMode(result);
        }

        public void UpdateNodeEditingMode(bool value)
        {
            EnableNodeEditing = value;
            OnTarget();
        }

        public void OnTarget()
        {
            var target = Game.PlayerLookAtTargetObject;
            if (target == null)
            {
                DisableGizmo();
                snapshotPanel.UpdateSelectedNodeControls(null);
            }
            else
            {
                var node = WorldSnapshotReaderWriter.Get().GetNodeById((int)target.NetworkId, target.ParentObject);
                if (node != null)
                {
                    if (EnableNodeEditing)
                    {
                        EnableGizmo(target);
                    }
                    else
                    {
                        DisableGizmo();
                    }

                    string cellName = "";
                    if (target.ParentCell != null)
                    {
                        cellName = target.ParentCell.Name;
                    }

                    snapshotPanel.UpdateSelectedNodeControls(node, cellName, target.ClientObject.GameObjectTypeName + " (" + target.ClientObject.GameObjectType + ")");
                }
                else
                {
                    DisableGizmo();
                }
            }
        }

        public void EnableGizmo(UtinniCore.Utinni.Object target)
        {
            GroundSceneCallbacks.AddPreDrawLoopCall(() =>
            {
                imgui_impl.Enable(target);
            });
        }

        public void DisableGizmo()
        {
            GroundSceneCallbacks.AddPreDrawLoopCall(() =>
            {
                imgui_impl.Disable();
            });
        }

        public void OnPositionChanged()
        {
            GroundSceneCallbacks.AddPreDrawLoopCall(() =>
            {
                var obj = Game.PlayerLookAtTargetObject;
                if (obj != null)
                {
                    var node = WorldSnapshotReaderWriter.Get().GetNodeById((int)obj.NetworkId, obj.ParentObject);
                    if (node != null)
                    {
                        editorPlugin.AddUndoCommand(this, new AddUndoCommandEventArgs(new WorldSnapshotNodePositionChangedCommand(node, node.Transform, obj.Transform)));
                        node.Transform.Position = obj.Transform.Position;
                        snapshotPanel.UpdateSelectedNodeControlsPosition(node.Transform.Position);
                    }
                }
            });
        }

        public void OnRotationChanged() // ToDo Something is broken where it sometimes has 1-2 too many undo stages
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                var obj = Game.PlayerLookAtTargetObject;
                if (obj != null)
                {
                    var node = WorldSnapshotReaderWriter.Get().GetNodeById((int)obj.NetworkId, obj.ParentObject);
                    if (node != null)
                    {
                        editorPlugin.AddUndoCommand(this, new AddUndoCommandEventArgs(new WorldSnapshotNodeRotationChangedCommand(node, node.Transform, obj.Transform)));
                        node.Transform.CopyRotation(obj.Transform);
                    }
                }
            });
        }

        public void SetSelectedNodePosition(float x, float y, float z)
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                var obj = Game.PlayerLookAtTargetObject;
                if (obj != null)
                {
                    var node = WorldSnapshotReaderWriter.Get().GetNodeById((int)obj.NetworkId, obj.ParentObject);
                    if (node != null)
                    {

                        if (node.ParentId != 0)
                        {
                            Vector oldO2w = obj.ObjectToWorld.Position;
                            Vector oldO2p = obj.ObjectToParent.Position;

                            Vector deltaPos = new Vector(oldO2p.X - x, oldO2p.Y - y, oldO2p.Z - z);
                            obj.ObjectToWorld.Position = new Vector(oldO2w + deltaPos);
                            obj.ObjectToParent.SetPosition(x, y, z);
                        }
                        else
                        {
                            obj.Transform.SetPosition(x, y, z);
                        }
                        obj.PositionAndRotationChanged(false, node.Transform.Position);

                        editorPlugin.AddUndoCommand(this, new AddUndoCommandEventArgs(new WorldSnapshotNodePositionChangedCommand(node, node.Transform, obj.Transform)));
                        node.Transform.SetPosition(x, y, z);
                    }
                }
            });
        }

        public void SetRadius(float radius)
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                var obj = Game.PlayerLookAtTargetObject;
                if (obj != null)
                {
                    var node = WorldSnapshotReaderWriter.Get().GetNodeById((int)obj.NetworkId, obj.ParentObject);
                    if (node != null)
                    {
                        node.Radius = radius;
                        // ToDo add an Undo command for radius?
                    }
                }
            });
        }

        public void CopyNode()
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                var obj = Game.PlayerLookAtTargetObject;
                if (obj != null)
                {
                    var node = WorldSnapshotReaderWriter.Get().GetNodeById((int)obj.NetworkId, obj.ParentObject);
                    if (node != null)
                    {
                        copiedNode = node;
                    }
                }
            });
        }

        public void PasteNode()
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                if (copiedNode != null)
                {
                    var copiedTransform = new Transform(copiedNode.Transform)
                    {
                        Position = cui_hud.GetCursorWorldPosition()
                    };

                    var newNode = WorldSnapshot.CreateNodeCopy(copiedNode, copiedTransform);
                    if (newNode != null)
                    {
                        editorPlugin.AddUndoCommand(this, new AddUndoCommandEventArgs(new AddWorldSnapshotNodeCommand(newNode)));
                    }
                }
            });
        }

        public void DuplicateNode()
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                var obj = Game.PlayerLookAtTargetObject;
                if (obj != null)
                {
                    var node = WorldSnapshotReaderWriter.Get().GetNodeById((int)obj.NetworkId, obj.ParentObject);
                    if (node != null)
                    {
                        var newNode = WorldSnapshot.CreateNodeCopy(node, obj.Transform);
                        if (newNode != null)
                        {
                            editorPlugin.AddUndoCommand(this, new AddUndoCommandEventArgs(new AddWorldSnapshotNodeCommand(newNode)));
                        }
                    }
                }
            });
        }

        public void RotateYaw(float value)
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                var obj = Game.PlayerLookAtTargetObject;
                if (obj != null)
                {
                    obj.Transform.Yaw(value);
                    obj.PositionAndRotationChanged(false, obj.Transform.Position);

                    var node = WorldSnapshotReaderWriter.Get().GetNodeById((int)obj.NetworkId, obj.ParentObject);
                    if (node != null)
                    {
                        editorPlugin.AddUndoCommand(this, new AddUndoCommandEventArgs(new WorldSnapshotNodeRotationChangedCommand(node, node.Transform, obj.Transform)));
                        node.Transform.CopyRotation(obj.Transform);
                    }
                }
            });
        }

        public void RotatePitch(float value)
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                var obj = Game.PlayerLookAtTargetObject;
                if (obj != null)
                {
                    obj.Transform.Pitch(value);
                    obj.PositionAndRotationChanged(false, obj.Transform.Position);

                    var node = WorldSnapshotReaderWriter.Get().GetNodeById((int)obj.NetworkId, obj.ParentObject);
                    if (node != null)
                    {
                        editorPlugin.AddUndoCommand(this, new AddUndoCommandEventArgs(new WorldSnapshotNodeRotationChangedCommand(node, node.Transform, obj.Transform)));
                        node.Transform.CopyRotation(obj.Transform);
                    }
                }
            });
        }

        public void RotateRoll(float value)
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                var obj = Game.PlayerLookAtTargetObject;
                if (obj != null)
                {
                    obj.Transform.Roll(value);
                    obj.PositionAndRotationChanged(false, obj.Transform.Position);

                    var node = WorldSnapshotReaderWriter.Get().GetNodeById((int)obj.NetworkId, obj.ParentObject);
                    if (node != null)
                    {
                        editorPlugin.AddUndoCommand(this, new AddUndoCommandEventArgs(new WorldSnapshotNodeRotationChangedCommand(node, node.Transform, obj.Transform)));
                        node.Transform.CopyRotation(obj.Transform);
                    }
                }
            });
        }

        public void ResetRotation()
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                var obj = Game.PlayerLookAtTargetObject;
                if (obj != null)
                {
                    obj.Transform.SetRotationAxis(0, 0, 0);

                    var node = WorldSnapshotReaderWriter.Get().GetNodeById((int)obj.NetworkId, obj.ParentObject);
                    if (node != null)
                    {
                        editorPlugin.AddUndoCommand(this, new AddUndoCommandEventArgs(new WorldSnapshotNodeRotationChangedCommand(node, node.Transform, obj.Transform)));
                        node.Transform.CopyRotation(obj.Transform);
                    }
                }
            });
        }

        public void SetOperationModeToTranslate()
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                imgui_impl.SetOperationModeToTranslate();
            });
        }

        public void SetOperationModeToRotation()
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                imgui_impl.SetOperationModeToRotate();
            });
        }

        public void SetOperationModeToTranslateHotkey()
        {
            SetOperationModeToTranslate();
            snapshotPanel.UpdateGizmoOperationControls(true);
        }

        public void SetOperationModeToRotationHotkey()
        {
            SetOperationModeToRotation();
            snapshotPanel.UpdateGizmoOperationControls(false);
        }

        public void SetGizmoToLocal()
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                imgui_impl.SetGizmoModeToLocal();
            });
            //snapshotPanel.UpdateGizmoModeControls(true);
        }

        public void SetGizmoToWorld()
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                imgui_impl.SetGizmoModeToWorld();
            });
            //snapshotPanel.UpdateGizmoModeControls(false);
        }

        public void SetGizmoSnap(bool value)
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                imgui_impl.EnableSnap(value);
            });
        }

        public void ToggleGizmoSnapHotkey()
        {
            bool hasSnapOn = !imgui_impl.IsSnapOn();
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                imgui_impl.EnableSnap(hasSnapOn);
            });
            snapshotPanel.UpdateGizmoSnapControl(hasSnapOn);
        }

        public void SetSnapScale(float value)
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                imgui_impl.SetSnapSize(value);
            });
        }

        private void OnGizmoEnabled()
        {
            hotkeyManager.Hotkeys["SetGizmoTranslateOperationMode"].Enabled = true;
            hotkeyManager.Hotkeys["SetGizmoRotationOperationMode"].Enabled = true;
            hotkeyManager.Hotkeys["ToggleGizmoSnap"].Enabled = true;
        }

        private void OnGizmoDisabled()
        {
            hotkeyManager.Hotkeys["SetGizmoTranslateOperationMode"].Enabled = false;
            hotkeyManager.Hotkeys["SetGizmoRotationOperationMode"].Enabled = false;
            hotkeyManager.Hotkeys["ToggleGizmoSnap"].Enabled = false;
        }

        public void AllowTargetEverything(bool value)
        {
            GroundSceneCallbacks.AddUpdateLoopCall(() =>
            {
                cui_hud.PatchAllowTargetEverything(value);
            });
        }

    }
}
