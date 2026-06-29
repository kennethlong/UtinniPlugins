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

        // Bucket A-2 world-pick demo: read the HUD's currently-picked world object via the
        // advertised CuiManager.SelectedObject getter and show its world position. Both accessors
        // are advertised (g_instance -> getTarget; Object.Transform -> getTransform_o2w) so this
        // is advertised-client-safe -- unlike the onTarget callback path, a pure getter wakes
        // no editor subscriber chain (the safe way to consume world-pick).
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
                Location = new Point(3, 139),
                Size = new Size(411, 20),
                ReadOnly = true,
                Text = "Selected object: (none)"
            };

            Size = new Size(417, 165);
            Controls.Add(btnReadSelectedObject);
            Controls.Add(txtSelectedObject);
        }

        // World-pick consumer (advertised-safe getter only -- no onTarget dispatch, no editor-subscriber
        // blast radius). CuiManager.SelectedObject -> cuiHud::g_instance -> cuiHud::getTarget (advertised);
        // Object.Transform -> getTransform_o2w (advertised). Returns null off advertised / with no live HUD.
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

                var transform = obj.Transform;
                if (transform == null)
                {
                    txtSelectedObject.Text = "Selected object: picked (no transform available)";
                    return;
                }

                var pos = transform.Position;
                txtSelectedObject.Text = string.Format("Selected object @ world ({0:F1}, {1:F1}, {2:F1})", pos.X, pos.Y, pos.Z);
            }
            catch (Exception ex)
            {
                txtSelectedObject.Text = "Read failed: " + ex.Message;
            }
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
