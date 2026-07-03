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
using System.Drawing;
using TJT.SWG;
using UtinniCore.Utinni;
using UtinniCoreDotNet.UI.Controls;

namespace TJT.UI.SubPanels
{
    public partial class MiscPanel : SubPanel, ISceneAvailability
    {
        private readonly CuiImpl cui;
        private readonly MiscImpl misc;

        // Bucket A-2 world-pick inspector: read the HUD's currently-picked world object via the
        // advertised CuiManager.SelectedObject getter and show a multi-field readout (template name,
        // appearance file, network id, type, cell, position, yaw, active flag, portal/client-data files).
        // Every field routes through an advertised row (see RenderObjectReadout) so this is
        // advertised-client-safe -- unlike the onTarget callback path, a pure getter wakes no editor
        // subscriber chain (the safe way to consume world-pick). "Inspect Player" runs the same readout
        // on the player's own creature object (Game.PlayerCreatureObject, advertised) -- no click needed.
        // "Inspect Camera" runs an advertised-safe readout on the active game camera (Game.Camera ->
        // game::getCamera, advertised) -- position/yaw/pitch off the advertised transform chain, plus the
        // SWGEmu-only lens fields (see RenderCameraReadout).
        // The sysmsg broadcast row injects a typed system message into the client's chat feed via the
        // v14-advertised systemMessageManager::sendMessage row (SysMsg.Broadcast -- main-loop marshaled,
        // skew-guarded native-side, so it degrades to a log line on a pre-v14 advertised exe).
        private UtinniButton btnReadSelectedObject;
        private UtinniButton btnInspectPlayer;
        private UtinniButton btnInspectCamera;
        private UtinniTextbox txtSysMsg;
        private UtinniButton btnSendSysMsg;
        private UtinniTextbox txtSelectedObject;

        public MiscPanel(UtINI ini) : base("Misc")
        {
            InitializeComponent();
            BuildSelectedObjectReadout();

            cui = new CuiImpl();
            misc = new MiscImpl(this);

            CreateSettings(ini);
            ini.Load();

            txtCreateObject.Text = ini.GetString("Misc", "defaultCreateObjectFilename");
            txtCreateAppearance.Text = ini.GetString("Misc", "defaultCreateAppearanceFilename");
        }

        // Built in code (not the Designer) to keep the world-pick demo self-contained. Mirrors the
        // existing UtinniButton/UtinniTextbox styling; placed below the existing bottom row.
        private void BuildSelectedObjectReadout()
        {
            btnReadSelectedObject = new UtinniButton
            {
                BackColor = Color.FromArgb(0, 122, 204),
                DrawOutline = false,
                FlatStyle = System.Windows.Forms.FlatStyle.Popup,
                ForeColor = Color.WhiteSmoke,
                Location = new Point(177, 113),
                Size = new Size(130, 20),
                Text = "Read Selected Obj",
                UseDisableColor = true,
                UseVisualStyleBackColor = false
                // NOT scene-gated: CuiManager.SelectedObject is null-safe (returns null with no live HUD),
                // and the advertised client doesn't deliver the ISceneAvailability signal to this panel
                // (the other scene-gated buttons stay grayed there) -- so gate-free keeps it clickable.
            };
            btnReadSelectedObject.Click += btnReadSelectedObject_Click;

            btnInspectPlayer = new UtinniButton
            {
                Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right,
                BackColor = Color.FromArgb(0, 122, 204),
                DrawOutline = false,
                FlatStyle = System.Windows.Forms.FlatStyle.Popup,
                ForeColor = Color.WhiteSmoke,
                Location = new Point(310, 113),
                Size = new Size(104, 20),
                Text = "Inspect Player",
                UseDisableColor = true,
                UseVisualStyleBackColor = false
                // Same rationale as btnReadSelectedObject: Game.PlayerCreatureObject is a null-safe
                // advertised getter, and the advertised client never delivers ISceneAvailability here.
            };
            btnInspectPlayer.Click += btnInspectPlayer_Click;

            // Second inspector row (the top row is full): a wide "Inspect Camera" button spanning under
            // the two row-1 buttons. Same not-scene-gated rationale -- Game.Camera is a null-safe advertised
            // getter and the advertised client never delivers ISceneAvailability here.
            btnInspectCamera = new UtinniButton
            {
                Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right,
                BackColor = Color.FromArgb(0, 122, 204),
                DrawOutline = false,
                FlatStyle = System.Windows.Forms.FlatStyle.Popup,
                ForeColor = Color.WhiteSmoke,
                Location = new Point(177, 139),
                Size = new Size(237, 20),
                Text = "Inspect Camera",
                UseDisableColor = true,
                UseVisualStyleBackColor = false
            };
            btnInspectCamera.Click += btnInspectCamera_Click;

            // Sysmsg broadcast row: type a message, inject it into the client's chat/system feed via
            // the v14-advertised systemMessageManager::sendMessage (SysMsg.Broadcast). Not scene-gated
            // (SysMsg no-ops without a running client; skew-guarded native-side on advertised).
            txtSysMsg = new UtinniTextbox
            {
                Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right,
                BackColor = Color.FromArgb(64, 64, 64),
                BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                ForeColor = Color.WhiteSmoke,
                Location = new Point(3, 165),
                Size = new Size(326, 20),
                Text = ""
            };

            btnSendSysMsg = new UtinniButton
            {
                Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right,
                BackColor = Color.FromArgb(0, 122, 204),
                DrawOutline = false,
                FlatStyle = System.Windows.Forms.FlatStyle.Popup,
                ForeColor = Color.WhiteSmoke,
                Location = new Point(335, 164),
                Size = new Size(79, 20),
                Text = "Send Sysmsg",
                UseDisableColor = true,
                UseVisualStyleBackColor = false
            };
            btnSendSysMsg.Click += btnSendSysMsg_Click;

            txtSelectedObject = new UtinniTextbox
            {
                Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right,
                BackColor = Color.FromArgb(64, 64, 64),
                BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                ForeColor = Color.WhiteSmoke,
                Font = new Font(System.Drawing.FontFamily.GenericMonospace, 8.25f),
                Location = new Point(3, 191),
                Size = new Size(411, 150),
                Multiline = true,
                ReadOnly = true,
                Text = "Selected object: (none)"
            };

            Size = new Size(417, 347);
            Controls.Add(btnReadSelectedObject);
            Controls.Add(btnInspectPlayer);
            Controls.Add(btnInspectCamera);
            Controls.Add(txtSysMsg);
            Controls.Add(btnSendSysMsg);
            Controls.Add(txtSelectedObject);
        }

        // World-pick: inspect the HUD's currently-picked world object (CuiManager.SelectedObject ->
        // cuiHud::g_instance/getTarget, advertised).
        private void btnReadSelectedObject_Click(object sender, EventArgs e)
        {
            RenderReadout("Selected object", CuiManager.SelectedObject,
                "(none -- nothing picked, or no live HUD)");
        }

        // Inspect the player's own avatar (Game.PlayerCreatureObject, advertised) -- no in-world click
        // needed. Same advertised-safe getter chain as world-pick, run on the creature object.
        private void btnInspectPlayer_Click(object sender, EventArgs e)
        {
            RenderReadout("Player", Game.PlayerCreatureObject,
                "(no player creature object -- not in a scene?)");
        }

        // Inspect the active game camera (Game.Camera -> game::getCamera, advertised). Position/Yaw/Pitch
        // come from the advertised transform chain (getTransform_o2w + getYaw_p2l/getPitch_p2l) -> safe on
        // every client. The lens fields (FOV / clip planes / viewport / projection) are RAW Camera
        // struct-field reads whose offsets are fragile on the advertised NGE layout (§5), so they're read
        // ONLY on SWGEmu and degrade to "(n/a on advertised)" on the advertised client -- same discipline
        // as Object.ParentCellName / getParentCellName.
        private void btnInspectCamera_Click(object sender, EventArgs e)
        {
            RenderCameraReadout(Game.Camera, "(no active camera -- not in a scene?)");
        }

        // Inject the typed text as a full system message (chatBoxOnly=false -- the engine's complete
        // sysmsg treatment). SysMsg.Broadcast is fire-and-forget: main-loop marshaled, no-ops without
        // a running client, and the native wrapper drops (logs) on a pre-v14 advertised exe.
        private void btnSendSysMsg_Click(object sender, EventArgs e)
        {
            var text = txtSysMsg.Text;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            TJT.SWG.SysMsg.Broadcast(text);
        }

        private void RenderCameraReadout(Camera cam, string noneMessage)
        {
            try
            {
                if (cam == null)
                {
                    txtSelectedObject.Text = "Camera: " + noneMessage;
                    return;
                }

                var lines = new System.Text.StringBuilder();
                lines.AppendLine("[Camera]");

                // Advertised-safe: world position + orientation off the object-to-world transform (the
                // same getTransform_o2w chain the object inspector uses). Yaw/Pitch are pure matrix math.
                var transform = cam.Transform;
                if (transform != null)
                {
                    var pos = transform.Position;
                    lines.AppendLine(string.Format("Position:    ({0:F1}, {1:F1}, {2:F1})", pos.X, pos.Y, pos.Z));
                    lines.AppendLine(string.Format("Yaw:         {0:F1} deg", transform.YawP2l));
                    lines.AppendLine(string.Format("Pitch:       {0:F1} deg", transform.PitchP2l));
                }
                else
                {
                    lines.AppendLine("Position:    (no transform available)");
                }

                // Lens fields are raw Camera struct reads -> offset-fragile on the advertised NGE client
                // (§5). Read them only on SWGEmu; degrade cleanly on the advertised client.
                if (UtinniCoreDotNet.Utility.Native.IsAdvertisedClient())
                {
                    lines.Append("Lens:        (n/a on advertised -- raw struct fields)");
                }
                else
                {
                    // SWG stores FOV in radians (setHorizontalFieldOfView(PI_OVER_3) == 60 deg) -> show degrees.
                    lines.AppendLine(string.Format("FOV:         {0:F1} deg h / {1:F1} deg v",
                        RadToDeg(cam.HorizontalFieldOfView), RadToDeg(cam.VerticalFieldOfView)));
                    lines.AppendLine(string.Format("Clip:        near {0:F2} / far {1:F1}", cam.NearPlane, cam.FarPlane));
                    lines.AppendLine(string.Format("Viewport:    {0} x {1}", cam.ViewportWidth, cam.ViewportHeight));
                    lines.Append("Projection:  " +
                        (cam.ProjectionMode == Camera.ProjectionModes.PmPerspective ? "perspective" : "parallel"));
                }

                txtSelectedObject.Text = lines.ToString();
            }
            catch (Exception ex)
            {
                txtSelectedObject.Text = "Read failed: " + ex.Message;
            }
        }

        private static float RadToDeg(float radians)
        {
            return radians * (180f / (float)Math.PI);
        }

        // Richer inspector (advertised-safe GETTERS only -- no onTarget dispatch, no editor-subscriber
        // blast radius). All reads route through advertised rows the resolver re-points on the advertised
        // client: Object.Transform -> getTransform_o2w; ObjectTemplateName -> object::getObjectTemplateName;
        // NetworkIdValue -> object::getNetworkId; SharedAppearanceFilename/SharedPortalLayoutFilename/
        // SharedClientDataFilename -> getObjectTemplate -> objectTemplate::getAppearanceFilename/
        // getPortalLayoutFilename/getClientDataFile; ObjectType -> object::getObjectType; IsActive ->
        // object::isActive. Each native accessor null-degrades (empty/0) off the advertised client, and Yaw
        // is pure matrix math off the same transform read -- no extra RVA. (Avoid Object.NetworkId /
        // GetTemplateFilename / GetAppearanceFilename: those map to UNadvertised SWGEmu paths.)
        private void RenderReadout(string header, UtinniCore.Utinni.Object obj, string noneMessage)
        {
            try
            {
                if (obj == null)
                {
                    txtSelectedObject.Text = header + ": " + noneMessage;
                    return;
                }

                var lines = new System.Text.StringBuilder();
                lines.AppendLine("[" + header + "]");

                var template = obj.ObjectTemplateName;
                lines.AppendLine("Template:    " + (string.IsNullOrEmpty(template) ? "(unavailable)" : template));

                var appearance = obj.SharedAppearanceFilename;
                lines.AppendLine("Appearance:  " + (string.IsNullOrEmpty(appearance) ? "(unavailable)" : appearance));

                var portal = obj.SharedPortalLayoutFilename;
                lines.AppendLine("Portal:      " + (string.IsNullOrEmpty(portal) ? "(none)" : portal));

                var clientData = obj.SharedClientDataFilename;
                lines.AppendLine("Client Data: " + (string.IsNullOrEmpty(clientData) ? "(none)" : clientData));

                lines.AppendLine("Type:        " + FormatObjectType(obj.ObjectType));

                lines.AppendLine("Active:      " + (obj.IsActive ? "yes" : "no"));

                long networkId = obj.NetworkIdValue;
                lines.AppendLine("Network ID:  " + (networkId != 0 ? "0x" + networkId.ToString("X") : "(unavailable)"));

                // ParentCellName: null = outdoors / no containing cell; "" = in a cell but the name isn't
                // safely readable on the advertised NGE client (raw-field offset, §5); else the cell name.
                var cell = obj.ParentCellName;
                lines.AppendLine("Cell:        " + (cell == null ? "(outdoors / none)" : (cell.Length == 0 ? "(in cell -- name n/a on advertised)" : cell)));

                var transform = obj.Transform;
                if (transform != null)
                {
                    var pos = transform.Position;
                    lines.AppendLine(string.Format("Position:    ({0:F1}, {1:F1}, {2:F1})", pos.X, pos.Y, pos.Z));
                    lines.Append(string.Format("Yaw:         {0:F1} deg", transform.YawP2l));
                }
                else
                {
                    lines.Append("Position:    (no transform available)");
                }

                txtSelectedObject.Text = lines.ToString();
            }
            catch (Exception ex)
            {
                txtSelectedObject.Text = "Read failed: " + ex.Message;
            }
        }

        // The advertised object::getObjectType returns the object's class Tag (a uint32 FOURCC, e.g.
        // 'CREO'/'TANO'/'BUIO'). Render the big-endian ASCII tag when all four bytes are printable,
        // always with the hex value alongside (a non-FOURCC numeric type still reads cleanly as hex).
        private static string FormatObjectType(uint type)
        {
            if (type == 0)
            {
                return "(unavailable)";
            }

            var tag = new char[4];
            bool printable = true;
            for (int i = 0; i < 4; i++)
            {
                int b = (int)((type >> (24 - i * 8)) & 0xFF);
                tag[i] = (char)b;
                if (b < 0x20 || b > 0x7E)
                {
                    printable = false;
                }
            }

            string hex = "0x" + type.ToString("X8");
            return printable ? ("'" + new string(tag) + "' (" + hex + ")") : hex;
        }

        private void CreateSettings(UtINI ini)
        {
            ini.AddSetting("Misc", "defaultCreateObjectFilename", "object/tangible/furniture/cheap/shared_armoire_s01.iff", UtINI.Value.Types.VtString);
            ini.AddSetting("Misc", "defaultCreateAppearanceFilename", "appearance/frn_all_chep_cabinet_s01.apt", UtINI.Value.Types.VtString);
        }

        private void btnCreateObject_Click(object sender, EventArgs e)
        {
            misc.CreateObject(txtCreateObject.Text);
        }

        private void btnCreateAppearance_Click(object sender, EventArgs e)
        {
            misc.CreateAppearance(txtCreateAppearance.Text);
        }

        private void btnReloadUi_Click(object sender, EventArgs e)
        {
            cui.ReloadUi();
        }

        private void btnRestartMusic_Click(object sender, EventArgs e)
        {
            cui.RestartMusic();
        }

        private bool previousIsSceneActive;
        public void UpdateSceneAvailability(bool isSceneActive)
        {
            if (previousIsSceneActive == isSceneActive)
            {
                return;
            }

            btnCreateObject.Enabled = isSceneActive;
            btnCreateAppearance.Enabled = isSceneActive;
            // btnReadSelectedObject / btnInspectPlayer / btnInspectCamera / btnSendSysMsg are intentionally
            // NOT scene-gated (null-safe getters / no-op-safe send; the advertised client doesn't deliver
            // this signal here anyway).

            previousIsSceneActive = isSceneActive;
        }
    }
}
