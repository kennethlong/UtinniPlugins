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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TJT.UI.Controls;
using UtinniCore.Utinni;
using UtinniCoreDotNet.Formats.Tre;
using UtinniCoreDotNet.PluginFramework;
using UtinniCoreDotNet.UI.Controls;
using UtinniCoreDotNet.UI.Forms;
using UtinniCoreDotNet.UI.Theme;
using UtinniCoreDotNet.Utility;

namespace TJT.UI.Forms
{
    public partial class FormTreBrowser : UtinniForm, IEditorForm
    {
        private readonly IEditorPlugin editorPlugin;
        private readonly UtINI ini;

        // Broad-filter guard (review consensus #4): past this many matches we do NOT rebuild the
        // tree — a flat ListView results mode is shown instead.
        private const int FilterMatchCap = 5000;

        // Flat path index from the shared TreArchiveIndex (review consensus #5) — the filter scans
        // this ONCE per debounced tick, never a per-tick tree re-walk.
        private IReadOnlyList<string> _allPaths;
        private TreArchiveIndex _index;         // shared browse facade (descriptor lookup for the detail pane)
        private PathNode _root;                 // directory trie for lazy tree navigation
        private HashSet<string> _loaded;        // Game.Repository install-time snapshot overlay (null = no live client)
        private Font _boldFont;
        private TreDetailPane _detail;          // right-region detail pane (07-03)
        private UtinniContextMenuStrip _tvTreContextMenu;  // 08-05 Task 4 — Open in IFF Editor
        private ToolStripMenuItem _miOpenInIffEditor;       // 08-05 Task 4
        private ToolStripMenuItem _miOpenInDatatableEditor; // 09-05 D-10.2 — Open in Datatable Editor

        public FormTreBrowser(IEditorPlugin editorPlugin)
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

            Width = ini.GetInt("TreBrowser", "width");
            Height = ini.GetInt("TreBrowser", "height");
            // Restore the splitter best-effort. SplitterDistance throws if it falls outside
            // [Panel1MinSize, width - Panel2MinSize]; guard AND try/catch so a stale/invalid ini
            // value can never bubble out of the ctor and fail the plugin's MEF load.
            try
            {
                int splitter = ini.GetInt("TreBrowser", "splitterDistance");
                if (splitter >= splitContainer.Panel1MinSize &&
                    splitter <= splitContainer.Width - splitContainer.Panel2MinSize)
                {
                    splitContainer.SplitterDistance = splitter;
                }
            }
            catch
            {
                // keep the designer default splitter distance
            }

            // Theme via Colors.*() accessors only — no raw ARGB color literals.
            splitContainer.BackColor = Colors.Primary();
            pnlDetail.BackColor = Colors.Primary();
            tvTre.BackColor = Colors.PrimaryHighlight();
            tvTre.ForeColor = Colors.Font();
            lvFiltered.BackColor = Colors.PrimaryHighlight();
            lvFiltered.ForeColor = Colors.Font();
            txtFilter.BackColor = Colors.PrimaryHighlight();
            txtFilter.ForeColor = Colors.Font();
            cbTypeFacet.BackColor = Colors.Primary();
            cbTypeFacet.ForeColor = Colors.Font();
            lblStatus.ForeColor = Colors.Font();
            lblLegend.ForeColor = Colors.Font();
            cbTypeFacet.SelectedIndex = 0;

            // ── UI-review polish (07 UI audit) ──
            txtFilter.CueBanner = "Filter files…";          // #1 search affordance on the most-used control
            cbTypeFacet.Visible = false;                     // #6 non-functional V1 stub — hide the dead control
            ThemeListViewHeader(lvFiltered);                 // #4 theme the flat-results column-header band
            BuildStatusLegendPanel();                        // #3 inset, divided, readable status/legend home

            // Host the detail pane in the right region (Panel2 / pnlDetail from plan 02).
            _detail = new TreDetailPane { Dock = DockStyle.Fill };
            pnlDetail.Controls.Add(_detail);

            // 08-05 Task 4: "Open in IFF Editor" context-menu on tree leaves. Browser stays
            // read-only — we only ADD a hand-off entry; existing read-only behavior unchanged.
            BuildTvTreContextMenu();

            // Kick off the disk enumeration from the ctor (matching FormObjectBrowser.LoadRepo) —
            // the heavy work runs off-thread via Task.Run and the result is applied back on the UI
            // thread through the captured SynchronizationContext (await continuation), so it does
            // NOT depend on the Shown event, which does not reliably fire for forms shown inside
            // the injected SWG message loop.
            StartLoad();
        }

        // 08-05 Task 4 — TRE Browser hand-off to the IFF Editor. Right-clicking a resolved IFF
        // leaf surfaces "Open in IFF Editor"; selecting it resolves the payload + descriptor
        // off-UI-thread and hands the bytes + provenance to FormIffEditor.OpenFromTreEntry.
        private void BuildTvTreContextMenu()
        {
            _tvTreContextMenu = new UtinniContextMenuStrip();
            _miOpenInIffEditor = new ToolStripMenuItem("Open in IFF Editor");
            _miOpenInIffEditor.Click += OnOpenInIffEditor;
            _tvTreContextMenu.Items.Add(_miOpenInIffEditor);
            // 09-05 D-10.2: Open in Datatable Editor — HIDDEN unless the selected entry is a .tab
            // (extension-only visibility ships V1; the DTII-in-non-.tab corner case is reachable via
            // Open in IFF Editor → Switch to typed datatable view, flagged in the 09-07 smoke).
            _miOpenInDatatableEditor = new ToolStripMenuItem("Open in Datatable Editor");
            _miOpenInDatatableEditor.Click += OnOpenInDatatableEditor;
            _tvTreContextMenu.Items.Add(_miOpenInDatatableEditor);
            _tvTreContextMenu.Opening += OnTvTreContextMenuOpening;
            tvTre.ContextMenuStrip = _tvTreContextMenu;
            // Right-click should select the underlying node so the menu's logic targets it.
            tvTre.NodeMouseClick += OnTvTreNodeMouseClick;
        }

        private void OnTvTreNodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                tvTre.SelectedNode = e.Node;
            }
        }

        private void OnTvTreContextMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Only show the menu when a resolvable leaf is selected.
            PathNode pn = tvTre.SelectedNode != null ? tvTre.SelectedNode.Tag as PathNode : null;
            if (pn == null || !pn.IsLeaf)
            {
                e.Cancel = true;
                return;
            }
            TreEntryDescriptor d;
            if (_index == null || !_index.TryGetDescriptor(pn.FullPath, out d))
            {
                e.Cancel = true;
                return;
            }
            // Defensive: do NOT offer Open-in-IFF-Editor on enumerate-only (V5000/V6000)
            // descriptors — the payload can't be resolved (07-04a — encrypted), so the editor
            // can't open it.
            _miOpenInIffEditor.Enabled = !d.EnumerateOnly;
            _miOpenInIffEditor.ToolTipText = d.EnumerateOnly
                ? "Payload is enumerate-only — cannot open in IFF Editor."
                : "Opens this entry in the IFF Editor with TRE provenance.";

            // 09-05 D-10.2: HIDE (not just disable) the Datatable-Editor item unless the entry is a
            // .tab AND its payload is resolvable (enumerate-only payloads can't be opened). Extension-
            // only visibility per the V1 hand-off shape (iter-2 LOW).
            bool isTab = string.Equals(Path.GetExtension(pn.FullPath ?? ""), ".tab", StringComparison.OrdinalIgnoreCase);
            _miOpenInDatatableEditor.Visible = isTab && !d.EnumerateOnly;
            _miOpenInDatatableEditor.ToolTipText = "Opens this datatable in the typed Datatable Editor with TRE provenance.";
        }

        private void OnOpenInIffEditor(object sender, EventArgs e)
        {
            PathNode pn = tvTre.SelectedNode != null ? tvTre.SelectedNode.Tag as PathNode : null;
            if (pn == null || !pn.IsLeaf) return;
            TreEntryDescriptor d;
            if (_index == null || !_index.TryGetDescriptor(pn.FullPath, out d)) return;
            // Resolve the payload OFF the UI thread (same pattern as DispatchDetail).
            TreEntryDescriptor descriptor = d;
            string logicalPath = pn.FullPath;
            Task.Run(() =>
            {
                try
                {
                    byte[] payload;
                    bool ok = TrePayloadResolver.TryResolve(descriptor, out payload);
                    if (!IsHandleCreated) return;
                    BeginInvoke((Action)(() =>
                    {
                        if (!ok)
                        {
                            lblStatus.Text = "Cannot open " + logicalPath + " — payload is enumerate-only.";
                            return;
                        }
                        FormIffEditor editor = FindOrCreateIffEditor();
                        if (editor == null)
                        {
                            lblStatus.Text = "IFF Editor is unavailable in this session.";
                            return;
                        }
                        editor.OpenFromTreEntry(
                            payload,
                            descriptor.ResolvedArchivePath,
                            logicalPath,
                            descriptor.ArchiveLocalOffset);
                        editor.Show();
                        editor.Activate();
                    }));
                }
                catch (Exception ex)
                {
                    if (IsHandleCreated)
                    {
                        BeginInvoke((Action)(() =>
                        {
                            lblStatus.Text = "Open-in-IFF-Editor failed: " + ex.Message;
                            lblStatus.ForeColor = Color.Red;
                        }));
                    }
                }
            });
        }

        // Find the FormIffEditor instance the plugin registered in GetForms() and return it.
        // Returns null if the editor failed to load (Plugin.cs registration sits inside a
        // try/catch — if FormIffEditor's ctor threw, it isn't in the forms list).
        private FormIffEditor FindOrCreateIffEditor()
        {
            foreach (IEditorForm f in editorPlugin.GetForms())
            {
                FormIffEditor editor = f as FormIffEditor;
                if (editor != null) return editor;
            }
            return null;
        }

        // 09-05 D-10.2 — TRE Browser hand-off to the Datatable Editor. Mirrors OnOpenInIffEditor
        // verbatim (off-UI-thread payload resolve → BeginInvoke marshal → FindOrCreate →
        // OpenFromTreEntry). The visibility predicate (extension == ".tab") is enforced on the
        // context-menu Opening event so this handler only fires for .tab entries.
        private void OnOpenInDatatableEditor(object sender, EventArgs e)
        {
            PathNode pn = tvTre.SelectedNode != null ? tvTre.SelectedNode.Tag as PathNode : null;
            if (pn == null || !pn.IsLeaf) return;
            TreEntryDescriptor d;
            if (_index == null || !_index.TryGetDescriptor(pn.FullPath, out d)) return;
            TreEntryDescriptor descriptor = d;
            string logicalPath = pn.FullPath;
            Task.Run(() =>
            {
                try
                {
                    byte[] payload;
                    bool ok = TrePayloadResolver.TryResolve(descriptor, out payload);
                    if (!IsHandleCreated) return;
                    BeginInvoke((Action)(() =>
                    {
                        if (!ok)
                        {
                            lblStatus.Text = "Cannot open " + logicalPath + " — payload is enumerate-only.";
                            return;
                        }
                        FormDatatableEditor editor = FindOrCreateDatatableEditor();
                        if (editor == null)
                        {
                            lblStatus.Text = "Datatable Editor is unavailable in this session.";
                            return;
                        }
                        editor.OpenFromTreEntry(
                            payload,
                            descriptor.ResolvedArchivePath,
                            logicalPath,
                            descriptor.ArchiveLocalOffset);
                        editor.Show();
                        editor.Activate();
                    }));
                }
                catch (Exception ex)
                {
                    if (IsHandleCreated)
                    {
                        BeginInvoke((Action)(() =>
                        {
                            lblStatus.Text = "Open-in-Datatable-Editor failed: " + ex.Message;
                            lblStatus.ForeColor = Color.Red;
                        }));
                    }
                }
            });
        }

        // Find the FormDatatableEditor instance the plugin registered in GetForms() and return it.
        // Returns null if the editor failed to load (Plugin.cs registration sits inside a try/catch).
        // Mirrors FindOrCreateIffEditor (the small duplication is acceptable — both forms talk to the
        // plugin's GetForms list; V2 refactor candidate per 09-05-PLAN behavior note).
        private FormDatatableEditor FindOrCreateDatatableEditor()
        {
            foreach (IEditorForm f in editorPlugin.GetForms())
            {
                FormDatatableEditor editor = f as FormDatatableEditor;
                if (editor != null) return editor;
            }
            return null;
        }

        // #4 (UI audit): owner-draw a ListView's column-header band so it matches the dark theme
        // instead of the OS light chrome. Rows draw default (the control's themed back/fore colors).
        // Static + ListView-parameterized so the detail pane reuses the same treatment.
        internal static void ThemeListViewHeader(ListView lv)
        {
            lv.OwnerDraw = true;
            lv.DrawColumnHeader += (s, e) =>
            {
                using (var bg = new SolidBrush(Colors.PrimaryShadow()))
                {
                    e.Graphics.FillRectangle(bg, e.Bounds);
                }
                Rectangle textRect = e.Bounds;
                textRect.X += 4;
                TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font ?? lv.Font, textRect, Colors.Font(),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            lv.DrawItem += (s, e) => { e.DrawDefault = true; };
            lv.DrawSubItem += (s, e) => { e.DrawDefault = true; };
        }

        // #3 (UI audit): the status + legend labels were two bare Dock.Bottom labels with no inset,
        // flagged "cramped/hard to read" since 07-02. Re-home them in an inset bottom panel with a
        // 2px divider so the load-bearing overlay legend + path/match feedback reads cleanly.
        private void BuildStatusLegendPanel()
        {
            var panel1 = splitContainer.Panel1;
            panel1.Controls.Remove(lblStatus);
            panel1.Controls.Remove(lblLegend);

            var pnlStatus = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                Padding = new Padding(3, 3, 3, 3),
                BackColor = Colors.Primary()
            };
            var divider = new Panel { Dock = DockStyle.Top, Height = 2, BackColor = Colors.PrimaryShadow() };

            lblStatus.Dock = DockStyle.Bottom;
            lblLegend.Dock = DockStyle.Bottom;
            pnlStatus.Controls.Add(lblStatus);   // bottom-most
            pnlStatus.Controls.Add(lblLegend);   // above status
            pnlStatus.Controls.Add(divider);     // top divider

            panel1.Controls.Add(pnlStatus);
        }

        private void CreateSettings()
        {
            ini.AddSetting("TreBrowser", "width", "1100", UtINI.Value.Types.VtInt);
            ini.AddSetting("TreBrowser", "height", "700", UtINI.Value.Types.VtInt);
            ini.AddSetting("TreBrowser", "splitterDistance", "360", UtINI.Value.Types.VtInt);
            // Fallback client-dir source (review item 7 / CONTEXT line 100). Empty by default —
            // the primary source is the injected client's working directory.
            ini.AddSetting("TreBrowser", "clientDir", "", UtINI.Value.Types.VtString);
        }

        public string GetName()
        {
            return this.Text;
        }

        public Form Create(IEditorPlugin editorPlugin, List<Form> parentChildren)
        {
            foreach (Form form in parentChildren)
            {
                if (form.GetType() == typeof(FormTreBrowser))
                {
                    form.Activate();
                    return null;
                }
            }

            FormTreBrowser formTreBrowser = new FormTreBrowser(editorPlugin);
            formTreBrowser.Show();
            parentChildren.Add(formTreBrowser);
            return formTreBrowser;
        }

        // ── Enumeration (ctor-driven; heavy work off-thread, UI applied via await continuation) ──

        private const string BaseTitle = "TRE Browser";

        private sealed class LoadResult
        {
            public string Dir;
            public TreArchiveIndex Index;
            public IReadOnlyList<string> AllPaths;
            public PathNode Root;
            public HashSet<string> Loaded;
            public string Error;   // non-null = a user-facing failure/empty state
        }

        private async void StartLoad()
        {
            lblStatus.Text = "Loading archive index…";
            SetTitle("Loading…");

            LoadResult r;
            try
            {
                // Heavy disk enumeration OFF the UI thread; ResolveClientTreDir/TreArchiveIndex.Build/
                // TryBuildLoadedOverlay touch no WinForms controls (Log.Info is thread-safe).
                r = await Task.Run((Func<LoadResult>)DoHeavyLoad);
            }
            catch (Exception ex)
            {
                Log.Info("[TreBrowser] load failed: " + ex);
                lblLegend.Text = "Failed to load archive index: " + ex.Message;
                SetTitle("load failed (see Utinni log)");
                return;
            }

            // Back on the UI thread (captured SynchronizationContext) — safe to touch controls
            // directly, no Control.Invoke needed.
            if (r.Error != null)
            {
                Log.Info("[TreBrowser] " + r.Error);
                lblLegend.Text = r.Error;
                SetTitle(r.Dir == null ? "No .tre/.toc found" : "0 paths — " + r.Dir);
                return;
            }

            _allPaths = r.AllPaths;
            _index = r.Index;
            _root = r.Root;
            _loaded = r.Loaded;

            ShowFullTree(); // populates the top-level branches (lazy children via BeforeExpand)
            lblLegend.Text = _loaded != null
                ? "Dimmed = on disk, not currently loaded"
                : "Overlay unavailable — no live client";
            SetTitle(_allPaths.Count + " paths");
        }

        private LoadResult DoHeavyLoad()
        {
            var r = new LoadResult();
            string dir = ResolveClientTreDir();   // logs each candidate dir
            if (dir == null)
            {
                Log.Info("[TreBrowser] no client .tre directory resolved (module/working/ini all lacked *.toc/*.tre)");
                r.Error = "Could not locate the client .tre directory — set [TreBrowser] clientDir";
                return r;
            }
            r.Dir = dir;
            Log.Info("[TreBrowser] resolved client .tre directory: '" + dir + "'");

            // Consume ONLY the shared TreArchiveIndex facade (D-08, criterion #4) — the UI never
            // calls the lower-level master-index / per-archive readers directly. The facade prefers
            // a COT2000/SearchTOC master index, else per-archive enumeration. Paths only (no payloads).
            TreArchiveIndex index = TreArchiveIndex.Build(dir);
            r.Index = index;
            r.AllPaths = index.AllPaths;
            Log.Info("[TreBrowser] enumerated " + r.AllPaths.Count + " paths from '" + dir + "'");
            if (r.AllPaths.Count == 0)
            {
                r.Error = "No entries found under: " + dir;
                return r;
            }

            r.Root = BuildTrie(r.AllPaths);
            r.Loaded = TryBuildLoadedOverlay();
            return r;
        }

        private void SetTitle(string suffix)
        {
            this.Text = string.IsNullOrEmpty(suffix) ? BaseTitle : BaseTitle + " — " + suffix;
            // UtinniForm draws the title in OnPaint; force a repaint so runtime Text changes show.
            this.Invalidate();
        }

        /// <summary>
        /// Resolves the client .tre directory (review item 7 / CONTEXT line 100 — this implements
        /// "the TRE mount source is the injected client's config where SWG loads .tre"; NOT a new
        /// product decision). Priority: (1) PRIMARY the injected client working directory
        /// (utility.GetWorkingDirectory()); (2) FALLBACK the [TreBrowser] clientDir ini key;
        /// (3) null → the caller shows the first-run error state.
        /// </summary>
        private string ResolveClientTreDir()
        {
            // (1) PRIMARY: the SWG client install root = the directory of the process Utinni is
            // injected into (its main module). This is where SWG keeps its .tre/.toc — and is far
            // more reliable than the process *current* directory (GetWorkingDirectory is just
            // GetCurrentDirectory, which is frequently NOT the install root). Review item 7 /
            // CONTEXT line 100 explicitly allows the SWG process directory when the working dir is
            // unsuitable.
            string moduleDir = TryGetProcessDir();
            Log.Info("[TreBrowser] process module dir: '" + (moduleDir ?? "<null>") +
                "' hasTre=" + (moduleDir != null && DirHasTre(moduleDir)));
            if (moduleDir != null && DirHasTre(moduleDir))
            {
                return moduleDir;
            }

            // (2) the process current directory (utility.GetWorkingDirectory()).
            string wd = null;
            try { wd = UtinniCore.Utility.utility.GetWorkingDirectory(); }
            catch { /* binding unavailable outside a live client */ }
            Log.Info("[TreBrowser] working dir: '" + (wd ?? "<null>") +
                "' hasTre=" + (!string.IsNullOrEmpty(wd) && DirHasTre(wd)));
            if (!string.IsNullOrEmpty(wd) && DirHasTre(wd))
            {
                return wd;
            }

            // (3) FALLBACK: the documented [TreBrowser] clientDir ini key.
            string configured = ini.GetString("TreBrowser", "clientDir");
            Log.Info("[TreBrowser] ini clientDir: '" + (configured ?? "<null>") +
                "' hasTre=" + (!string.IsNullOrEmpty(configured) && DirHasTre(configured)));
            if (!string.IsNullOrEmpty(configured) && DirHasTre(configured))
            {
                return configured;
            }

            return null;
        }

        private static string TryGetProcessDir()
        {
            try
            {
                string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                return Path.GetDirectoryName(exe);
            }
            catch
            {
                return null;
            }
        }

        private static bool DirHasTre(string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    return false;
                }
                return Directory.EnumerateFiles(dir, "*.toc").Any()
                    || Directory.EnumerateFiles(dir, "*.tre").Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Builds the loaded-overlay set by reading Game.Repository DIRECTLY on this background Task
        /// — the SAME deliberate established pattern FormObjectBrowser.LoadRepo uses for the
        /// in-process harvested set (review item 9). Read-only consumption via FilenameCount +
        /// GetFilenameAt (CON-N-02); the harvest is a DESTRUCTIVE one-shot install-time SNAPSHOT —
        /// it is never re-harvested and the one-shot harvest accessor is never invoked again. Returns null
        /// when there is no live client; on any failure it degrades to null (full-color tree).
        /// </summary>
        private HashSet<string> TryBuildLoadedOverlay()
        {
            try
            {
                if (!Game.IsRunning)
                {
                    return null;
                }

                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int count = Game.Repository.FilenameCount;
                for (int i = 0; i < count; i++)
                {
                    set.Add(Game.Repository.GetFilenameAt(i));
                }
                return set.Count > 0 ? set : null;
            }
            catch
            {
                return null;
            }
        }

        // ── Lazy tree population ────────────────────────────────────────────

        private TreeNode MakeNode(string segment, PathNode node)
        {
            if (node.Children.Count == 0)
            {
                // Leaf: type-tag suffix + loaded/dimmed overlay color.
                var leaf = new TreeNode(segment + " " + TypeTag(node.FullPath)) { Tag = node };
                leaf.ForeColor = LeafColor(node.FullPath);
                return leaf;
            }

            // Directory: add a single dummy child (Tag == null) so the expand glyph shows; the real
            // children are filled lazily on BeforeExpand.
            var dir = new TreeNode(segment) { Tag = node };
            dir.Nodes.Add(new TreeNode("…"));
            return dir;
        }

        private void tvTre_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            PathNode pn = e.Node.Tag as PathNode;
            if (pn == null)
            {
                return;
            }

            // Replace the dummy child (Tag == null) with the real children on demand.
            if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Tag == null)
            {
                tvTre.BeginUpdate();
                e.Node.Nodes.Clear();
                foreach (string key in pn.Children.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                {
                    e.Node.Nodes.Add(MakeNode(key, pn.Children[key]));
                }
                tvTre.EndUpdate();
            }
        }

        private void tvTre_AfterSelect(object sender, TreeViewEventArgs e)
        {
            PathNode pn = e.Node != null ? e.Node.Tag as PathNode : null;
            if (pn == null || !pn.IsLeaf)
            {
                _detail.ShowEmpty(); // directory / no leaf selected
                return;
            }

            string path = pn.FullPath;
            lblStatus.Text = path;

            TreEntryDescriptor d;
            if (_index == null || !_index.TryGetDescriptor(path, out d))
            {
                _detail.ShowParseFailure(new TreMetadata { Path = path }, "No descriptor for this entry");
                return;
            }

            var meta = new TreMetadata
            {
                Path = path,
                SizeBytes = d.Length,
                SourceArchive = !string.IsNullOrEmpty(d.TreeFileName) ? d.TreeFileName : System.IO.Path.GetFileName(d.ResolvedArchivePath),
                Crc = d.Crc,
                CompressionKind = CompressorKindName(d.Compressor),
                RootFormTag = "",
                Version = d.Version
            };
            _detail.ShowDecoding(meta); // metadata shows immediately while the payload resolves

            // Resolve + decode OFF the UI thread. Single pinned contract: TryResolve IS the branch
            // (no pre-branch on EnumerateOnly + separate Resolve — review consensus #3).
            TreEntryDescriptor descriptor = d;
            Task.Run(() =>
            {
                try
                {
                    byte[] payload;
                    bool ok = TrePayloadResolver.TryResolve(descriptor, out payload);
                    if (IsHandleCreated) BeginInvoke((Action)(() => DispatchDetail(meta, ok, payload)));
                }
                catch (TreParseException ex)
                {
                    if (IsHandleCreated) BeginInvoke((Action)(() => _detail.ShowParseFailure(meta, ex.Message)));
                }
                catch (System.IO.IOException ex)
                {
                    if (IsHandleCreated) BeginInvoke((Action)(() => _detail.ShowParseFailure(meta, ex.Message)));
                }
            });
        }

        private void DispatchDetail(TreMetadata meta, bool resolved, byte[] payload)
        {
            if (!resolved)
            {
                // TryResolve returned false => enumerate-only (v6000). The ONLY route to encrypted
                // (gated on the enumerate-only signal, NOT on the FORM tag — review item 12).
                _detail.ShowEncrypted(meta);
                return;
            }

            // The .stf string table is NOT IFF (raw magic+version binary, 07-04a) — route it to the
            // dedicated structured (id/text) view before the IFF FORM check.
            if (UtinniCoreDotNet.Formats.Decoders.StringTableDecoder.LooksLikeStf(payload))
            {
                meta.RootFormTag = "STF";
                _detail.ShowStringTable(meta, payload);
            }
            else if (LooksLikeIff(payload))
            {
                if (payload.Length >= 12)
                {
                    meta.RootFormTag = System.Text.Encoding.ASCII.GetString(payload, 8, 4); // FORM subtype
                }
                _detail.ShowReadable(meta, payload);
            }
            else
            {
                // Readable but NOT an IFF FORM (or .stf) — show the real bytes; the detail pane labels
                // a .gui/ui path as a UI page (text), else the unsupported-raw note (review item 12).
                _detail.ShowUnsupportedRaw(meta, payload);
            }
        }

        private static bool LooksLikeIff(byte[] payload)
        {
            if (payload == null || payload.Length < 8) return false;
            string tag = System.Text.Encoding.ASCII.GetString(payload, 0, 4);
            return tag == "FORM" || tag == "LIST" || tag == "CAT ";
        }

        private static string CompressorKindName(int compressor)
        {
            switch (compressor)
            {
                case 0: return "none";
                case 1: return "deflate";
                case 2: return "zlib";
                default: return "unknown";
            }
        }

        private Color LeafColor(string fullPath)
        {
            if (_loaded == null)
            {
                return Colors.Font(); // no live client — render all full-color
            }
            return _loaded.Contains(fullPath) ? Colors.Font() : Colors.FontDisabled();
        }

        // ── Debounced, capped, flat-index filter ────────────────────────────

        private void txtFilter_TextChanged(object sender, EventArgs e)
        {
            dbFilter.Stop();
            dbFilter.Start();
        }

        private void dbFilter_Tick(object sender, EventArgs e)
        {
            dbFilter.Stop();
            if (_allPaths == null)
            {
                return;
            }

            string text = txtFilter.Text.Trim();
            if (text.Length == 0)
            {
                ShowFullTree();
                return;
            }

            // Scan the FLAT shared index ONCE (review consensus #5) — not a per-tick tree re-walk.
            var matches = new List<string>();
            int total = 0;
            for (int i = 0; i < _allPaths.Count; i++)
            {
                if (_allPaths[i].IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    total++;
                    if (matches.Count < FilterMatchCap)
                    {
                        matches.Add(_allPaths[i]);
                    }
                }
            }

            if (total > FilterMatchCap)
            {
                // Broad filter — do NOT rebuild the tree; show the flat results ListView instead.
                tvTre.Visible = false;
                lvFiltered.Visible = true;
                lvFiltered.BeginUpdate();
                lvFiltered.Items.Clear();
                for (int i = 0; i < matches.Count; i++)
                {
                    lvFiltered.Items.Add(new ListViewItem(matches[i]));
                }
                lvFiltered.EndUpdate();
                lblStatus.Text = total + " matches — refine filter";
                return;
            }

            // Within cap — prune the tree to the matching ancestor chains, expand, bold whole nodes.
            tvTre.Visible = true;
            lvFiltered.Visible = false;
            PathNode pruned = BuildTrie(matches);
            tvTre.BeginUpdate();
            tvTre.Nodes.Clear();
            // Per-branch build: each top-level branch is built fully in memory then added in one
            // Nodes mutation (not per-node).
            foreach (string key in pruned.Children.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                tvTre.Nodes.Add(BuildFilteredNode(key, pruned.Children[key]));
            }
            tvTre.ExpandAll();
            tvTre.EndUpdate();
            lblStatus.Text = total + (total == 1 ? " match" : " matches");
        }

        private TreeNode BuildFilteredNode(string segment, PathNode node)
        {
            if (node.Children.Count == 0)
            {
                // Whole matching leaf node is bolded (NodeFont) — standard WinForms TreeView cannot
                // bold a substring without owner-draw (review divergent-view: substring-bold infeasible).
                var leaf = new TreeNode(segment + " " + TypeTag(node.FullPath)) { Tag = node };
                leaf.NodeFont = BoldFont();
                leaf.ForeColor = LeafColor(node.FullPath);
                return leaf;
            }

            var dir = new TreeNode(segment) { Tag = node };
            foreach (string key in node.Children.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                dir.Nodes.Add(BuildFilteredNode(key, node.Children[key]));
            }
            return dir;
        }

        private void ShowFullTree()
        {
            tvTre.Visible = true;
            lvFiltered.Visible = false;
            lblStatus.Text = _allPaths != null ? _allPaths.Count + " paths" : "";
            if (_root == null)
            {
                return;
            }
            tvTre.BeginUpdate();
            tvTre.Nodes.Clear();
            foreach (string key in _root.Children.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                tvTre.Nodes.Add(MakeNode(key, _root.Children[key]));
            }
            tvTre.EndUpdate();
        }

        private Font BoldFont()
        {
            if (_boldFont == null)
            {
                _boldFont = new Font(tvTre.Font, FontStyle.Bold);
            }
            return _boldFont;
        }

        // ── Persistence (best-effort — UtINI exposes no explicit setter; re-register + Save) ──

        private void FormTreBrowser_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                ini.AddSetting("TreBrowser", "width", Width.ToString(), UtINI.Value.Types.VtInt);
                ini.AddSetting("TreBrowser", "height", Height.ToString(), UtINI.Value.Types.VtInt);
                ini.AddSetting("TreBrowser", "splitterDistance", splitContainer.SplitterDistance.ToString(), UtINI.Value.Types.VtInt);
                ini.Save();
            }
            catch
            {
                // Persistence is best-effort; never block close.
            }

            // Singleton form (Plugin.cs registers ONE instance at load): user-initiated close
            // hides instead of disposing so the TJT window menu can re-Show this same instance.
            // Same latent bug class as FormIffEditor (08-05 smoke defect) — fixed defensively
            // here so closing then re-opening the TRE Browser via the host's window menu does
            // not throw ObjectDisposedException at Form.CreateHandle. Editor-host shutdown
            // (CloseReason.ApplicationExitCall / TaskManagerClosing / WindowsShutDown) still
            // falls through and disposes normally.
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static string TypeTag(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "[-]";
            }
            int dot = path.LastIndexOf('.');
            string ext = dot >= 0 ? path.Substring(dot + 1).ToLowerInvariant() : "";
            switch (ext)
            {
                case "iff": return "[IFF]";
                case "tab": return "[TAB]";
                case "stf": return "[STF]";
                case "tpl":
                case "tmpl": return "[TMPL]";
                case "msh":
                case "mgn": return "[MESH]";
                case "skt": return "[SKEL]";
                case "ans": return "[ANIM]";
                case "sht": return "[SHDR]";
                default: return "[-]";
            }
        }

        /// <summary>One directory-trie node: children + (for a leaf) the full virtual path.</summary>
        private sealed class PathNode
        {
            public readonly Dictionary<string, PathNode> Children =
                new Dictionary<string, PathNode>(StringComparer.OrdinalIgnoreCase);
            public string FullPath;
            public bool IsLeaf;
        }

        private static PathNode BuildTrie(IReadOnlyList<string> paths)
        {
            var root = new PathNode();
            foreach (string p in paths)
            {
                if (string.IsNullOrEmpty(p))
                {
                    continue;
                }
                string[] segs = p.Split('/');
                PathNode cur = root;
                for (int i = 0; i < segs.Length; i++)
                {
                    string seg = segs[i];
                    if (seg.Length == 0)
                    {
                        continue;
                    }
                    PathNode next;
                    if (!cur.Children.TryGetValue(seg, out next))
                    {
                        next = new PathNode();
                        cur.Children[seg] = next;
                    }
                    cur = next;
                }
                cur.IsLeaf = true;
                cur.FullPath = p;
            }
            return root;
        }
    }
}
