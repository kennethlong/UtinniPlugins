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
        private readonly Panel pnlStructured = new Panel();   // 07-04b fills this
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
            lblRawNote.Text = "No IFF structure recognized — showing raw bytes.";
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

            // hex peek (collapsed by default via the toggle button)
            pnlHex.Dock = DockStyle.Bottom;
            pnlHex.Height = 180;
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
            pnlStructured.Height = 0;
            pnlStructured.BackColor = Colors.Primary();
            pnlStructured.Visible = false; // 07-04b fills the per-type structured views here

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
