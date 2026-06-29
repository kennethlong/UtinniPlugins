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
        // appearance file, network id, position, yaw). Every field routes through an advertised row
        // (see btnReadSelectedObject_Click) so this is advertised-client-safe -- unlike the onTarget
        // callback path, a pure getter wakes no editor subscriber chain (the safe way to consume world-pick).
        private UtinniButton btnReadSelectedObject;
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

            txtSelectedObject = new UtinniTextbox
            {
                Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right,
                BackColor = Color.FromArgb(64, 64, 64),
                BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                ForeColor = Color.WhiteSmoke,
                Font = new Font(System.Drawing.FontFamily.GenericMonospace, 8.25f),
                Location = new Point(3, 139),
                Size = new Size(411, 116),
                Multiline = true,
                ReadOnly = true,
                Text = "Selected object: (none)"
            };

            Size = new Size(417, 261);
            Controls.Add(btnReadSelectedObject);
            Controls.Add(txtSelectedObject);
        }

        // World-pick consumer / richer inspector (advertised-safe GETTERS only -- no onTarget dispatch, no
        // editor-subscriber blast radius). All reads route through advertised rows the resolver re-points on
        // the advertised client: CuiManager.SelectedObject -> cuiHud::g_instance/getTarget; Object.Transform
        // -> getTransform_o2w; ObjectTemplateName -> object::getObjectTemplateName; NetworkIdValue ->
        // object::getNetworkId; SharedAppearanceFilename -> getObjectTemplate -> objectTemplate::
        // getAppearanceFilename. Each native accessor null-degrades (empty/0) off the advertised client, and
        // Yaw is pure matrix math off the same transform read -- no extra RVA. (Avoid Object.NetworkId /
        // GetTemplateFilename / GetAppearanceFilename: those map to UNadvertised SWGEmu paths.)
        private void btnReadSelectedObject_Click(object sender, EventArgs e)
        {
            try
            {
                var obj = CuiManager.SelectedObject;
                if (obj == null)
                {
                    txtSelectedObject.Text = "Selected object: (none -- nothing picked, or no live HUD)";
                    return;
                }

                var lines = new System.Text.StringBuilder();

                var template = obj.ObjectTemplateName;
                lines.AppendLine("Template:   " + (string.IsNullOrEmpty(template) ? "(unavailable)" : template));

                var appearance = obj.SharedAppearanceFilename;
                lines.AppendLine("Appearance: " + (string.IsNullOrEmpty(appearance) ? "(unavailable)" : appearance));

                lines.AppendLine("Type:       " + FormatObjectType(obj.ObjectType));

                long networkId = obj.NetworkIdValue;
                lines.AppendLine("Network ID: " + (networkId != 0 ? "0x" + networkId.ToString("X") : "(unavailable)"));

                // ParentCellName: null = outdoors / no containing cell; "" = in a cell but the name isn't
                // safely readable on the advertised NGE client (raw-field offset, §5); else the cell name.
                var cell = obj.ParentCellName;
                lines.AppendLine("Cell:       " + (cell == null ? "(outdoors / none)" : (cell.Length == 0 ? "(in cell -- name n/a on advertised)" : cell)));

                var transform = obj.Transform;
                if (transform != null)
                {
                    var pos = transform.Position;
                    lines.AppendLine(string.Format("Position:   ({0:F1}, {1:F1}, {2:F1})", pos.X, pos.Y, pos.Z));
                    lines.Append(string.Format("Yaw:        {0:F1} deg", transform.YawP2l));
                }
                else
                {
                    lines.Append("Position:   (no transform available)");
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
            // btnReadSelectedObject is intentionally NOT scene-gated (null-safe getter; the advertised
            // client doesn't deliver this signal here anyway).

            previousIsSceneActive = isSceneActive;
        }
    }
}
