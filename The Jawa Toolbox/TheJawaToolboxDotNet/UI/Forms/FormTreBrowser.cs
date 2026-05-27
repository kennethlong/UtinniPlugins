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
using UtinniCore.Utinni;
using UtinniCoreDotNet.Formats.Tre;
using UtinniCoreDotNet.PluginFramework;
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
        private PathNode _root;                 // directory trie for lazy tree navigation
        private HashSet<string> _loaded;        // Game.Repository install-time snapshot overlay (null = no live client)
        private Font _boldFont;
        private bool _loadStarted;

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

            // Start the disk enumeration once the handle exists (so the per-branch Control.Invoke
            // marshaling from the background Task is valid).
            this.Shown += FormTreBrowser_Shown;
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

        // ── Background enumeration ──────────────────────────────────────────

        private void FormTreBrowser_Shown(object sender, EventArgs e)
        {
            if (_loadStarted) return;
            _loadStarted = true;
            lblStatus.Text = "Loading archive index…";
            Task.Run((Action)LoadWorker);
        }

        private void LoadWorker()
        {
            try
            {
                string dir = ResolveClientTreDir();
                if (dir == null)
                {
                    // First-run error state — never a silent empty tree (review item 7).
                    MarshalLegend("Could not locate the client .tre directory — set [TreBrowser] clientDir");
                    MarshalStatus("");
                    return;
                }

                // Consume ONLY the shared TreArchiveIndex facade (D-08, criterion #4) — the UI never
                // calls the lower-level master-index / per-archive readers directly. The facade
                // prefers a COT2000/SearchTOC master index, else per-archive enumeration. Built
                // once, paths only (no payloads).
                TreArchiveIndex index = TreArchiveIndex.Build(dir);
                _allPaths = index.AllPaths;
                _root = BuildTrie(_allPaths);

                _loaded = TryBuildLoadedOverlay();

                // Lazy tree: only the top-level branches are added now; children are filled on
                // BeforeExpand. Per-branch BATCHED marshaling (review consensus #4): one
                // Control.Invoke per top-level branch, NEVER one Invoke per node (213k nodes).
                List<string> topKeys = _root.Children.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (string key in topKeys)
                {
                    if (!IsHandleCreated) return;
                    PathNode branch = _root.Children[key];
                    this.Invoke((Action)(() => tvTre.Nodes.Add(MakeNode(key, branch))));
                }

                MarshalLegend(_loaded != null
                    ? "Dimmed = on disk, not currently loaded"
                    : "Overlay unavailable — no live client");
                MarshalStatus(_allPaths.Count + " paths");
            }
            catch (Exception ex)
            {
                MarshalLegend("Failed to load archive index: " + ex.Message);
                MarshalStatus("");
            }
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
            try
            {
                string wd = UtinniCore.Utility.utility.GetWorkingDirectory();
                if (!string.IsNullOrEmpty(wd) && DirHasTre(wd))
                {
                    return wd;
                }
            }
            catch
            {
                // utility binding unavailable outside a live client — fall through to the ini fallback.
            }

            string configured = ini.GetString("TreBrowser", "clientDir");
            if (!string.IsNullOrEmpty(configured) && DirHasTre(configured))
            {
                return configured;
            }

            return null;
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
            // Plan 03 fills pnlDetail from the selected entry's TreEntryDescriptor here. For now,
            // surface the selected virtual path in the status label.
            PathNode pn = e.Node != null ? e.Node.Tag as PathNode : null;
            if (pn != null && pn.IsLeaf)
            {
                lblStatus.Text = pn.FullPath;
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
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private void MarshalStatus(string s)
        {
            if (IsHandleCreated)
            {
                BeginInvoke((Action)(() => lblStatus.Text = s));
            }
        }

        private void MarshalLegend(string s)
        {
            if (IsHandleCreated)
            {
                BeginInvoke((Action)(() => lblLegend.Text = s));
            }
        }

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
