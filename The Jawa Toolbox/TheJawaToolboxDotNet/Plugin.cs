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
using System.IO;
using System.Reflection;
using TJT.UI.Forms;
using TJT.UI.SubPanels;
using UtinniCore.Utinni;
using UtinniCoreDotNet.Hotkeys;
using UtinniCoreDotNet.PluginFramework;
using UtinniCoreDotNet.UI.Controls;
using UtinniCoreDotNet.UI.Forms;
using UtinniCoreDotNet.Utility;

namespace TJT
{
    public class TheJawaToolboxPlugin : IEditorPlugin
    {
        private readonly HotkeyManager hotkeyManager = new HotkeyManager(false);
        private readonly List<IEditorForm> forms = new List<IEditorForm>();
        private readonly List<SubPanelContainer> panels = new List<SubPanelContainer>();
        private readonly UtINI ini;

        public TheJawaToolboxPlugin()
        {
            Information = new PluginInformation("The Jawa Toolbox"
                , "An SWG Client development toolbox"
                , "Timbab");

            Log.Info("Created: The Jawa Toolbox");

            ini = new UtINI(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\settings.ini");

            forms.Add(new FormObjectBrowser(this));

            // Isolate the TRE Browser's construction: a failure here must NOT take down the whole
            // toolbox (a throwing form ctor would fail the plugin's MEF load and remove TJT from
            // the menu entirely). Log and continue so the rest of the toolbox still loads.
            try
            {
                forms.Add(new FormTreBrowser(this));
            }
            catch (Exception ex)
            {
                Log.Info("Failed to create FormTreBrowser; TRE Browser will be unavailable: " + ex);
            }

            // 08-05 Task 4: register the editable IFF Editor in the same try/catch isolation
            // block. GetSubPanels() stays null — the MEF SPI is NOT widened (CON-M-01/02,
            // STAB-04). The TRE Browser's "Open in IFF Editor" hand-off finds this form by
            // type in the existing forms list.
            try
            {
                forms.Add(new FormIffEditor(this));
            }
            catch (Exception ex)
            {
                Log.Info("Failed to create FormIffEditor; IFF Editor will be unavailable: " + ex);
            }

            panels.Add(new SubPanelContainer("Controls", new SubPanel[]
            {
                new ScenePanel(this, hotkeyManager, ini),
                new SnapshotPanel(this, hotkeyManager, ini),
                new PlayerPanel(hotkeyManager),
                new FreeCamPanel(hotkeyManager), 
                new GraphicsPanel(ini),
                new MiscPanel(ini)
            }));

            hotkeyManager.CreateSettings();
            hotkeyManager.Load();
        }

        public PluginInformation Information { get; }

        public UtINI GetConfig()
        {
            return ini;
        }

        public EventHandler<AddUndoCommandEventArgs> AddUndoCommand { get; set; }

        public HotkeyManager GetHotkeyManager()
        {
            return hotkeyManager;
        }

        public List<IEditorForm> GetForms()
        {
            return forms;
        }

        public List<SubPanelContainer> GetStandalonePanels()
        {
            return panels;
        }

        public List<SubPanel> GetSubPanels() { return null; }
    }
}
