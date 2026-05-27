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
using System.IO;
using System.Text;
using System.Windows.Forms;
using UtinniCoreDotNet.Formats.Decoders;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.Formats.Tre;
using UtinniCoreDotNet.UI.Controls;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Controls
{
    /// <summary>Immutable metadata for the selected TRE entry, populated from its TreEntryDescriptor.</summary>
    public struct TreMetadata
    {
        public string Path;
        public long SizeBytes;
        public string SourceArchive;
        public uint Crc;
        public string CompressionKind;
        public string RootFormTag;
        public TreVersion Version;
    }

    /// <summary>
    /// TRE Browser detail pane (07-03): metadata header + type/version banner + a universal IFF
    /// chunk tree (TAG · size · @offset, from the shared Formats/Iff reader) + a collapsed raw-hex
    /// peek. Degrades to four DISTINCT non-readable states — encrypted (enumerate-only), unsupported-
    /// but-readable-raw, parse-failure, and empty — so one bad file never crashes the browser. The
    /// chunk-tree region is a reusable read-only surface Phase 8's IFF editor makes editable (D-13).
    /// </summary>
    public class TreDetailPane : UserControl
    {
        private const int HexCap = 4096;
        // Row cap for the structured ListViews so a huge datatable cannot add hundreds of thousands
        // of rows synchronously and freeze the UI thread (review LOW / T-07-17).
        private const int StructuredRowCap = 5000;

        // Metadata strip
        private readonly UtinniLabel lblPath = ValueLabel();
        private readonly UtinniLabel lblSize = ValueLabel();
        private readonly UtinniLabel lblArchive = ValueLabel();
        private readonly UtinniLabel lblCrc = ValueLabel();
        private readonly UtinniLabel lblCompression = ValueLabel();
        private readonly Panel accent = new Panel();
        private readonly UtinniLabel lblBanner = new UtinniLabel();
        private readonly UtinniContextMenuStrip metaMenu = new UtinniContextMenuStrip();

        // Content area + state panels
        private readonly Panel pnlContent = new Panel();
        private readonly Panel pnlReadable = new Panel();
        private readonly TreeView tvChunks = new TreeView();
        // 07-04b: per-type structured view — a uniform themed ListView (datatable rows, STF entries,
        // object-template fields, mesh/shader/UI-page summaries), with a title + row-cap truncation label.
        private readonly Panel pnlStructured = new Panel();
        private readonly UtinniLabel lblStructuredTitle = new UtinniLabel();
        private readonly ListView lvStructured = new ListView();
        private readonly UtinniLabel lblStructuredTrunc = new UtinniLabel();
        private readonly Panel pnlHex = new Panel();
        private readonly UtinniButton btnHexToggle = new UtinniButton();
        private readonly TextBox txtHex = new TextBox();
        private readonly UtinniLabel lblRawNote = new UtinniLabel();
        private readonly Panel pnlInfo = new Panel();
        private readonly UtinniLabel lblInfoHeading = new UtinniLabel();
        private readonly UtinniLabel lblInfoBody = new UtinniLabel();

        private string _copyPath;
        private uint _copyCrc;

        public TreDetailPane()
        {
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Colors.Primary();

            BuildContentArea();   // Dock.Fill — added FIRST so the metadata strip claims the top edge
            BuildMetadataStrip(); // Dock.Top

            ShowEmpty();
        }

        // ── public display API (FormTreBrowser AfterSelect dispatches to these) ──

        public void ShowEmpty()
        {
            ClearMeta();
            lblBanner.Text = "";
            ShowInfo("No file selected", "Select a file in the tree to inspect its structure and contents.", false);
        }

        public void ShowDecoding(TreMetadata meta)
        {
            PopulateMeta(meta);
            SetBanner(meta);
            ShowInfo("Decoding…", "Reading the payload from the archive…", false);
        }

        public void ShowReadable(TreMetadata meta, byte[] payload)
        {
            PopulateMeta(meta);
            SetBanner(meta);

            IffDocument doc;
            try
            {
                using (var ms = new MemoryStream(payload))
                {
                    doc = IffReader.Read(ms); // SAME parser the inspect-iff CLI uses (D-08)
                }
            }
            catch (IffParseException ex)
            {
                ShowParseFailure(meta, ex.Message);
                return;
            }
            catch (IOException ex)
            {
                ShowParseFailure(meta, ex.Message);
                return;
            }

            LoadIff(doc);
            lblRawNote.Visible = false;
            tvChunks.Visible = true;
            RenderStructured(doc, meta);   // 07-04b: per-type structured view in pnlStructured
            FillHex(payload);
            ShowReadablePanel();
        }

        /// <summary>
        /// 07-04b: Renders the non-IFF SWG string table (.stf) as a structured (id, text) view.
        /// The .stf is not an IFF container, so there is no chunk tree — the metadata header, the
        /// structured ListView, and the raw-hex peek render. Called by FormTreBrowser when the
        /// resolved payload is recognized as a string table.
        /// </summary>
        public void ShowStringTable(TreMetadata meta, byte[] payload)
        {
            PopulateMeta(meta);
            SetBanner(meta);
            tvChunks.Visible = false;        // .stf is not IFF — no universal chunk tree
            lblRawNote.Visible = false;
            try
            {
                RenderStringTable(StringTableDecoder.Decode(payload));
            }
            catch (DecoderException ex)
            {
                ShowParseFailure(meta, ex.Message);
                return;
            }
            FillHex(payload);
            ShowReadablePanel();
        }

        /// <summary>
        /// Renders the universal IFF chunk tree from a parsed document. Public + standalone so
        /// Phase 8's IFF editor can reuse this exact surface and add editing on top (D-13).
        /// </summary>
        public void LoadIff(IffDocument doc)
        {
            tvChunks.BeginUpdate();
            tvChunks.Nodes.Clear();
            if (doc != null && doc.Root != null)
            {
                tvChunks.Nodes.Add(BuildChunkNode(doc.Root));
                tvChunks.Nodes[0].Expand();
            }
            tvChunks.EndUpdate();
        }

        public void ShowEncrypted(TreMetadata meta)
        {
            PopulateMeta(meta);
            SetBanner(meta);
            // Version-accurate (review LOW). NOTE: as of the 07-02 discovery, V5000 is the readable
            // SWGEmu Pre-CU format, so it is NOT enumerate-only and never reaches here — only V6000
            // (encrypted Restoration payloads) does. The branch is kept for any future enumerate-only
            // lineage; the copy is truthful per version.
            if (meta.Version == TreVersion.V6000)
            {
                ShowInfo("Encrypted payload (v6000) — enumerate-only",
                    "This archive's contents are obfuscated and cannot be decoded in-app. Metadata is available above. Extract with TreeFileExtractor.exe to inspect raw bytes.",
                    false);
            }
            else
            {
                ShowInfo("Enumerate-only payload",
                    "This archive's payloads are not directly decodable in-app. Metadata is available above.",
                    false);
            }
        }

        public void ShowUnsupportedRaw(TreMetadata meta, byte[] payload)
        {
            PopulateMeta(meta);
            SetBanner(meta);
            // A NON-enumerate-only payload whose bytes are NOT an IFF FORM — show the real bytes,
            // NOT the encrypted/extract copy (review item 12).
            tvChunks.Visible = false;
            lblRawNote.Visible = true;

            // 07-04b: SWG UI pages (.gui) are TEXT, not IFF — recognize them by the path/extension
            // hint and label them as a UI page (criterion #3 coverage) while showing the raw text.
            if (IffStructureSummary.IsUiPagePath(meta.Path))
            {
                lblRawNote.Text = "UI page (text format) — showing raw text.";
                RenderUiPageSummary(meta, payload);
            }
            else
            {
                lblRawNote.Text = "No IFF structure recognized — showing raw bytes.";
                HideStructured();
            }
            FillHex(payload);
            ShowReadablePanel();
        }

        public void ShowParseFailure(TreMetadata meta, string reason)
        {
            PopulateMeta(meta);
            SetBanner(meta);
            ShowInfo("Could not decode this file",
                reason + ". The file may be truncated or use an unsupported layout. Other files are unaffected.",
                true);
        }

        // ── 07-04b structured-view rendering (all via the shared Formats/Decoders — Pitfall 7) ──

        /// <summary>
        /// Dispatches the parsed IFF document to the matching per-type structured view in
        /// pnlStructured. The same decoders the decode-iff CLI verb exercises (no UI-only decode).
        /// Unrecognized types hide the structured view; the chunk tree + hex peek still render.
        /// </summary>
        private void RenderStructured(IffDocument doc, TreMetadata meta)
        {
            try
            {
                var root = doc != null ? doc.Root as IffContainerChunk : null;
                string sub = root != null ? root.SubTypeId : null;

                if (sub == "DTII")
                {
                    RenderDataTable(DataTableDecoder.Decode(doc));
                    return;
                }
                if (sub == "MESH" || sub == "SKMG" || sub == "SKTM" || sub == "KFAT" || sub == "CKAT")
                {
                    var appearance = AppearanceSummary.Summarize(doc);
                    if (appearance != null) { RenderAppearance(appearance); return; }
                }
                if (sub == "SSHT" || sub == "CSHD")
                {
                    RenderStructure(IffStructureSummary.Summarize(doc, meta.Path), "Shader");
                    return;
                }
                if (ObjectTemplateDecoder.LooksLikeObjectTemplate(doc != null ? doc.Root : null))
                {
                    RenderObjectTemplate(ObjectTemplateDecoder.Decode(doc));
                    return;
                }
                HideStructured(); // unrecognized IFF — chunk tree + hex still render (UI-SPEC)
            }
            catch (DecoderException)
            {
                HideStructured(); // a decoder problem hides section 4; one bad file never crashes the pane
            }
        }

        private void RenderDataTable(DataTableView dt)
        {
            BeginStructured("Datatable — " + dt.Columns.Count + " cols × " + dt.Rows.Count + " rows");
            foreach (var c in dt.Columns)
            {
                lvStructured.Columns.Add(c.Name + " (" + SpecChar(c.Kind) + ")");
            }
            int shown = Math.Min(StructuredRowCap, dt.Rows.Count);
            for (int r = 0; r < shown; r++)
            {
                object[] cells = dt.Rows[r];
                var item = new ListViewItem(CellText(cells.Length > 0 ? cells[0] : null));
                for (int i = 1; i < cells.Length; i++) item.SubItems.Add(CellText(cells[i]));
                lvStructured.Items.Add(item);
            }
            EndStructured(dt.Rows.Count, shown);
        }

        private void RenderStringTable(StfTable stf)
        {
            BeginStructured("String table — " + stf.Entries.Count + " entries");
            lvStructured.Columns.Add("String ID");
            lvStructured.Columns.Add("Name");
            lvStructured.Columns.Add("Text");
            int shown = Math.Min(StructuredRowCap, stf.Entries.Count);
            for (int i = 0; i < shown; i++)
            {
                StfEntry e = stf.Entries[i];
                var item = new ListViewItem(e.Id.ToString());
                item.SubItems.Add(e.Name ?? "");
                item.SubItems.Add(e.Text ?? "");
                lvStructured.Items.Add(item);
            }
            EndStructured(stf.Entries.Count, shown);
        }

        private void RenderObjectTemplate(ObjectTemplateView ot)
        {
            string baseSuffix = string.IsNullOrEmpty(ot.BaseTemplate) ? "" : " : " + ot.BaseTemplate;
            BeginStructured("Object template — " + ot.RootType + baseSuffix);
            lvStructured.Columns.Add("Field");
            lvStructured.Columns.Add("Value");
            lvStructured.Columns.Add("Inherited from");
            int shown = Math.Min(StructuredRowCap, ot.Fields.Count);
            for (int i = 0; i < shown; i++)
            {
                ObjectTemplateField f = ot.Fields[i];
                var item = new ListViewItem(f.Name);
                item.SubItems.Add(f.Value ?? "");
                item.SubItems.Add(f.InheritedFrom ?? "");
                lvStructured.Items.Add(item);
            }
            EndStructured(ot.Fields.Count, shown);
        }

        private void RenderAppearance(AppearanceInfo a)
        {
            BeginStructured("Appearance — " + a.Kind);
            lvStructured.Columns.Add("Property");
            lvStructured.Columns.Add("Value");
            AddKv("Kind", a.Kind);
            if (a.VertexCount > 0) AddKv("Vertices", a.VertexCount.ToString());
            if (a.ShaderCount > 0) AddKv("Shaders", a.ShaderCount.ToString());
            if (a.JointCount > 0) AddKv("Joints", a.JointCount.ToString());
            if (a.FrameCount > 0) AddKv("Frames", a.FrameCount.ToString());
            int jointsShown = 0;
            foreach (string name in a.JointNames)
            {
                if (jointsShown >= StructuredRowCap) break;
                AddKv("Joint " + jointsShown, name);
                jointsShown++;
            }
            EndStructured(a.JointNames.Count, jointsShown);
        }

        private void RenderStructure(StructureInfo s, string label)
        {
            BeginStructured(label + " — " + s.RootTag);
            lvStructured.Columns.Add("Property");
            lvStructured.Columns.Add("Value");
            AddKv("Root tag", s.RootTag);
            AddKv("Recognized as", s.RecognizedAs);
            AddKv("Child count", s.ChildCount.ToString());
            AddKv("Child tags", string.Join(", ", s.ChildTags));
            EndStructured(0, 0);
        }

        private void RenderUiPageSummary(TreMetadata meta, byte[] payload)
        {
            BeginStructured("UI page (text)");
            lvStructured.Columns.Add("Property");
            lvStructured.Columns.Add("Value");
            AddKv("Type", "UI page (.gui, text — not IFF)");
            AddKv("Path", meta.Path ?? "");
            AddKv("Size", (payload != null ? payload.Length : 0) + " bytes");
            EndStructured(0, 0);
        }

        private void AddKv(string key, string value)
        {
            var item = new ListViewItem(key);
            item.SubItems.Add(value ?? "");
            lvStructured.Items.Add(item);
        }

        private void BeginStructured(string title)
        {
            lvStructured.BeginUpdate();
            lvStructured.Items.Clear();
            lvStructured.Columns.Clear();
            lblStructuredTitle.Text = title;
            lblStructuredTrunc.Visible = false;
        }

        private void EndStructured(int total, int shown)
        {
            foreach (ColumnHeader col in lvStructured.Columns) col.Width = -2; // autosize header+content
            lvStructured.EndUpdate();
            if (total > shown)
            {
                lblStructuredTrunc.Text = "… " + total + " rows — showing first " + shown;
                lblStructuredTrunc.Visible = true;
            }

            // The structured view is the PRIMARY region (Dock.Fill) so the table shows from its first
            // row with its column header fully visible. When the payload is IFF, the universal chunk
            // tree is a fixed top strip above it (not a Fill that squeezes the table); for non-IFF
            // (.stf / .gui) there is no tree. Hex peek stays a collapsible bottom strip.
            if (tvChunks.Visible)
            {
                tvChunks.Dock = DockStyle.Top;
                tvChunks.Height = 150;
            }
            pnlStructured.Dock = DockStyle.Fill;
            pnlStructured.Visible = true;
            pnlStructured.SendToBack();   // Fill control sits behind the docked edge controls
            if (tvChunks.Visible) tvChunks.BringToFront();
            pnlHex.BringToFront();
            if (lblRawNote.Visible) lblRawNote.BringToFront();
        }

        private void HideStructured()
        {
            lvStructured.BeginUpdate();
            lvStructured.Items.Clear();
            lvStructured.Columns.Clear();
            lvStructured.EndUpdate();
            pnlStructured.Visible = false;
            pnlStructured.Dock = DockStyle.Bottom;
            // No structured view: the chunk tree reclaims the primary Fill region.
            tvChunks.Dock = DockStyle.Fill;
            tvChunks.SendToBack();
        }

        private static string CellText(object cell)
        {
            return cell == null ? "" : Convert.ToString(cell, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string SpecChar(DataCellKind kind)
        {
            switch (kind)
            {
                case DataCellKind.Int: return "i";
                case DataCellKind.Float: return "f";
                default: return "s";
            }
        }

        // ── rendering helpers ──

        private void ShowReadablePanel()
        {
            pnlInfo.Visible = false;
            pnlReadable.Visible = true;
            pnlReadable.BringToFront();
        }

        private void ShowInfo(string heading, string body, bool isError)
        {
            lblInfoHeading.Text = heading;
            lblInfoHeading.ForeColor = isError ? Color.Red : Colors.Font();
            lblInfoBody.Text = body;
            lblInfoBody.ForeColor = Colors.FontDisabled();
            pnlReadable.Visible = false;
            pnlInfo.Visible = true;
            pnlInfo.BringToFront();
        }

        private TreeNode BuildChunkNode(IffChunk chunk)
        {
            var container = chunk as IffContainerChunk;
            string label = container != null
                ? chunk.TypeId + " " + container.SubTypeId + "  ·  " + chunk.LengthBytes + " bytes  ·  @" + chunk.OffsetBytes
                : chunk.TypeId + "  ·  " + chunk.LengthBytes + " bytes  ·  @" + chunk.OffsetBytes;

            var node = new TreeNode(label);
            if (container != null)
            {
                foreach (IffChunk child in container.Children)
                {
                    node.Nodes.Add(BuildChunkNode(child));
                }
            }
            return node;
        }

        private void FillHex(byte[] payload)
        {
            txtHex.Text = HexDump(payload, HexCap);
        }

        private static string HexDump(byte[] data, int cap)
        {
            if (data == null || data.Length == 0) return "(empty)";
            int n = Math.Min(cap, data.Length);
            var sb = new StringBuilder(n * 4);
            for (int row = 0; row < n; row += 16)
            {
                sb.Append(row.ToString("X8")).Append("  ");
                var ascii = new StringBuilder(16);
                for (int col = 0; col < 16; col++)
                {
                    int i = row + col;
                    if (i < n)
                    {
                        sb.Append(data[i].ToString("x2")).Append(' ');
                        byte b = data[i];
                        ascii.Append(b >= 0x20 && b <= 0x7E ? (char)b : '.');
                    }
                    else
                    {
                        sb.Append("   ");
                    }
                }
                sb.Append(' ').Append(ascii).Append("\r\n");
            }
            if (data.Length > cap)
            {
                sb.Append("… (").Append(data.Length - cap).Append(" more bytes; first ").Append(cap).Append(" shown)\r\n");
            }
            return sb.ToString();
        }

        private void SetBanner(TreMetadata meta)
        {
            string tag = string.IsNullOrEmpty(meta.RootFormTag) ? "?" : meta.RootFormTag;
            lblBanner.Text = DescribeType(tag) + " (" + tag + " / " + meta.Version.ToString().TrimStart('V') + ")";
        }

        private static string DescribeType(string rootFormTag)
        {
            switch (rootFormTag)
            {
                case "DTII": return "Datatable";
                case "STAT":
                case "STRT": return "String Table";
                case "CREO":
                case "SHOT":
                case "TANO": return "Object Template";
                case "MESH":
                case "MGN ": return "Mesh / Appearance";
                case "SSHT": return "Shader";
                default: return "IFF Asset";
            }
        }

        private void PopulateMeta(TreMetadata meta)
        {
            _copyPath = meta.Path;
            _copyCrc = meta.Crc;
            lblPath.Text = meta.Path ?? "";
            lblSize.Text = meta.SizeBytes + " bytes (" + HumanSize(meta.SizeBytes) + ")";
            lblArchive.Text = meta.SourceArchive ?? "";
            lblCrc.Text = "0x" + meta.Crc.ToString("X8");
            lblCompression.Text = string.IsNullOrEmpty(meta.CompressionKind) ? "none" : meta.CompressionKind;
        }

        private void ClearMeta()
        {
            _copyPath = null;
            _copyCrc = 0;
            lblPath.Text = lblSize.Text = lblArchive.Text = lblCrc.Text = lblCompression.Text = "";
        }

        private static string HumanSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.0") + " KB";
            double mb = kb / 1024.0;
            return mb.ToString("0.0") + " MB";
        }

        // ── UI construction ──

        private void BuildMetadataStrip()
        {
            var pnlMeta = new Panel { Dock = DockStyle.Top, Height = 150, BackColor = Colors.Primary() };

            AddRow(pnlMeta, "Path", lblPath, 4);
            AddRow(pnlMeta, "Size", lblSize, 20);
            AddRow(pnlMeta, "Archive", lblArchive, 36);
            AddRow(pnlMeta, "CRC", lblCrc, 52);
            AddRow(pnlMeta, "Compression", lblCompression, 68);

            // 2px Colors.Secondary() accent rule above the banner (the reserved accent — mirrors UtinniForm).
            accent.Height = 2;
            accent.BackColor = Colors.Secondary();
            accent.SetBounds(3, 92, 320, 2);
            accent.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            pnlMeta.Controls.Add(accent);

            lblBanner.AutoSize = false;
            lblBanner.SetBounds(3, 98, 380, 22);
            lblBanner.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblBanner.ForeColor = Colors.Font();
            lblBanner.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold); // banner is the only Bold use here
            pnlMeta.Controls.Add(lblBanner);

            // Copy path / Copy CRC (read-only clipboard copy of displayed text — not an asset export).
            var copyPath = new ToolStripMenuItem("Copy path");
            copyPath.Click += (s, e) => { if (!string.IsNullOrEmpty(_copyPath)) Clipboard.SetText(_copyPath); };
            var copyCrc = new ToolStripMenuItem("Copy CRC");
            copyCrc.Click += (s, e) => Clipboard.SetText("0x" + _copyCrc.ToString("X8"));
            metaMenu.Items.Add(copyPath);
            metaMenu.Items.Add(copyCrc);
            pnlMeta.ContextMenuStrip = metaMenu;

            Controls.Add(pnlMeta);
        }

        private static void AddRow(Panel parent, string key, UtinniLabel value, int y)
        {
            var k = new UtinniLabel { AutoSize = false, Text = key, ForeColor = Colors.FontDisabled() };
            k.SetBounds(3, y, 130, 16);
            value.SetBounds(140, y, 240, 16);
            value.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            value.ForeColor = Colors.Font();
            parent.Controls.Add(value);
            parent.Controls.Add(k);
        }

        private static UtinniLabel ValueLabel()
        {
            return new UtinniLabel { AutoSize = false, Text = "" };
        }

        private void BuildContentArea()
        {
            pnlContent.Dock = DockStyle.Fill;
            pnlContent.BackColor = Colors.Primary();

            // ── readable panel: chunk tree (fill) + structured placeholder (bottom) + hex peek (bottom) ──
            pnlReadable.Dock = DockStyle.Fill;
            pnlReadable.BackColor = Colors.Primary();

            tvChunks.Dock = DockStyle.Fill;
            tvChunks.BackColor = Colors.PrimaryHighlight();
            tvChunks.ForeColor = Colors.Font();
            tvChunks.BorderStyle = BorderStyle.None;
            tvChunks.HideSelection = false;
            tvChunks.ShowLines = true;

            // hex peek (collapsed by default via the toggle button — 22px header only until expanded)
            pnlHex.Dock = DockStyle.Bottom;
            pnlHex.Height = 22;
            pnlHex.BackColor = Colors.Primary();

            btnHexToggle.Dock = DockStyle.Top;
            btnHexToggle.Height = 20;
            btnHexToggle.Text = "Raw hex ▾";
            btnHexToggle.ForeColor = Colors.Font();
            btnHexToggle.Click += (s, e) =>
            {
                txtHex.Visible = !txtHex.Visible;
                pnlHex.Height = txtHex.Visible ? 180 : 22;
                btnHexToggle.Text = txtHex.Visible ? "Raw hex ▴" : "Raw hex ▾";
            };

            txtHex.Dock = DockStyle.Fill;
            txtHex.Multiline = true;
            txtHex.ReadOnly = true;
            txtHex.ScrollBars = ScrollBars.Both;
            txtHex.WordWrap = false;
            txtHex.BackColor = Colors.PrimaryHighlight();
            txtHex.ForeColor = Colors.Font();
            txtHex.BorderStyle = BorderStyle.None;
            txtHex.Font = new Font("Consolas", 9f); // the monospace exception (UI-SPEC Typography)
            txtHex.Visible = false;                 // collapsed by default

            pnlHex.Controls.Add(txtHex);
            pnlHex.Controls.Add(btnHexToggle);

            pnlStructured.Dock = DockStyle.Bottom;
            pnlStructured.Height = 240;
            pnlStructured.BackColor = Colors.Primary();
            pnlStructured.Visible = false; // shown by RenderStructured when a view is recognized

            // Fill control FIRST, then the Top/Bottom edge labels (layout-order convention).
            lvStructured.Dock = DockStyle.Fill;
            lvStructured.View = View.Details;
            lvStructured.FullRowSelect = true;
            lvStructured.GridLines = false;
            lvStructured.HideSelection = false;
            lvStructured.BackColor = Colors.PrimaryHighlight();
            lvStructured.ForeColor = Colors.Font();
            lvStructured.BorderStyle = BorderStyle.None;

            lblStructuredTrunc.Dock = DockStyle.Bottom;
            lblStructuredTrunc.AutoSize = false;
            lblStructuredTrunc.Height = 16;
            lblStructuredTrunc.ForeColor = Colors.FontDisabled();
            lblStructuredTrunc.Visible = false;

            lblStructuredTitle.Dock = DockStyle.Top;
            lblStructuredTitle.AutoSize = false;
            lblStructuredTitle.Height = 18;
            lblStructuredTitle.ForeColor = Colors.Font();

            pnlStructured.Controls.Add(lvStructured);
            pnlStructured.Controls.Add(lblStructuredTrunc);
            pnlStructured.Controls.Add(lblStructuredTitle);

            lblRawNote.Dock = DockStyle.Top;
            lblRawNote.AutoSize = false;
            lblRawNote.Height = 18;
            lblRawNote.ForeColor = Colors.FontDisabled();
            lblRawNote.Visible = false;

            pnlReadable.Controls.Add(tvChunks);
            pnlReadable.Controls.Add(lblRawNote);
            pnlReadable.Controls.Add(pnlStructured);
            pnlReadable.Controls.Add(pnlHex);

            // ── info panel: encrypted / unsupported-note / parse-fail / empty ──
            pnlInfo.Dock = DockStyle.Fill;
            pnlInfo.BackColor = Colors.Primary();
            pnlInfo.Padding = new Padding(8);

            lblInfoBody.Dock = DockStyle.Fill;
            lblInfoBody.AutoSize = false;
            lblInfoBody.ForeColor = Colors.FontDisabled();

            lblInfoHeading.Dock = DockStyle.Top;
            lblInfoHeading.AutoSize = false;
            lblInfoHeading.Height = 24;
            lblInfoHeading.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
            lblInfoHeading.ForeColor = Colors.Font();

            pnlInfo.Controls.Add(lblInfoBody);
            pnlInfo.Controls.Add(lblInfoHeading);

            pnlContent.Controls.Add(pnlReadable);
            pnlContent.Controls.Add(pnlInfo);

            Controls.Add(pnlContent);
        }
    }
}
