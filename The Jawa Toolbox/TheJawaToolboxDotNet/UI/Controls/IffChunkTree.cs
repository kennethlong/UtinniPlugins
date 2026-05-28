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
using System.Windows.Forms;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.UI.Controls;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Controls
{
    /// <summary>
    /// Shared themed UserControl that renders the IFF chunk tree (D-09). Extracted from
    /// <see cref="TreDetailPane"/>'s inline <c>tvChunks</c> + <c>BuildChunkNode</c> + <c>LoadIff</c>
    /// (Phase 7) so both the read-only TRE Browser detail pane and the editable IFF Editor (08-04)
    /// share one chunk-tree surface. Phases 9-11 (datatable / string-table / object-template
    /// editors) reuse this same control.
    ///
    /// <para><b>Two binding modes</b> — read-only consumers call <see cref="LoadDocument"/> with an
    /// immutable <see cref="IffDocument"/>; editable consumers call <see cref="LoadMutable"/> with a
    /// <see cref="MutableIffDocument"/> and each <see cref="TreeNode.Tag"/> references the bound
    /// <see cref="MutableIffNode"/>. The control itself does NOT take a hard dependency on
    /// editor-only types; read-only consumers never touch the mutable surface.</para>
    ///
    /// <para><b>Phase 7 node label parity</b> — labels keep the exact format
    /// <c>TAG [SubType]  ·  N bytes  ·  @offset</c> for the read-only path so the TRE Browser is
    /// functionally unchanged (CON-T-05). The editable path renders a stable-id-style label
    /// <c>TAG [SubType]  ·  N bytes</c> (no @offset — the mutable model has no fixed file offset
    /// once it has been edited).</para>
    ///
    /// <para><b>Four-state degradation</b> — a malformed/oversized tree must render a state, never
    /// crash the host. Reader caps in <c>IffReader</c> already bound node count/size upstream
    /// (08-01 / Phase 7). This control's own surface is a single themed <c>TreeView</c>; it cannot
    /// throw on an empty tree (Nodes simply stays empty). Failure to parse is the host's
    /// responsibility — the host calls <c>ShowParseFailure</c>/<c>ShowEmpty</c> on its detail-pane
    /// and never calls <see cref="LoadDocument"/>/<see cref="LoadMutable"/>.</para>
    ///
    /// <para><b>Structural-op context-menu attachment</b> — set <see cref="StructuralOpMenu"/> with
    /// a themed <see cref="UtinniContextMenuStrip"/> populated by the host. The control wires it
    /// onto the internal <c>TreeView</c>'s <c>ContextMenuStrip</c>. The menu's items + handlers are
    /// owned by the editor host (08-04). Read-only consumers leave it null.</para>
    /// </summary>
    public class IffChunkTree : UserControl
    {
        private readonly TreeView tvChunks = new TreeView();

        /// <summary>
        /// Raised after the selection changes in the chunk tree. <see cref="TreeNode.Tag"/> on the
        /// selected node references the bound model node (an <see cref="IffChunk"/> for read-only
        /// consumers, a <see cref="MutableIffNode"/> for editable consumers).
        /// </summary>
        public event TreeViewEventHandler AfterSelect;

        /// <summary>
        /// Optional context menu used for structural ops in the editable IFF Editor (08-04). The
        /// menu's items + handlers are wired by the host. Setting this property installs it as the
        /// underlying <see cref="TreeView"/>'s <see cref="Control.ContextMenuStrip"/>; clearing it
        /// (set to null) removes the attachment. Read-only consumers (the TRE Browser) leave it
        /// null.
        /// </summary>
        public ContextMenuStrip StructuralOpMenu
        {
            get { return tvChunks.ContextMenuStrip; }
            set { tvChunks.ContextMenuStrip = value; }
        }

        /// <summary>The currently selected tree node, or null if none. Pass-through to the
        /// underlying <see cref="TreeView"/>.</summary>
        public TreeNode SelectedNode
        {
            get { return tvChunks.SelectedNode; }
            set { tvChunks.SelectedNode = value; }
        }

        /// <summary>
        /// Root-level <see cref="TreeNode"/> collection of the inner <see cref="TreeView"/>.
        /// Editable consumers (08-04 FormIffEditor) iterate this to decorate dirty / added nodes
        /// per the UI-SPEC ●/＋ glyph + Colors.Secondary() accent rule. Pass-through, not a copy.
        /// </summary>
        public TreeNodeCollection RootNodes
        {
            get { return tvChunks.Nodes; }
        }

        public IffChunkTree()
        {
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Colors.Primary();

            // Themed TreeView — matches Phase 7 tvChunks theming exactly (TreDetailPane.cs 670-675).
            tvChunks.Dock = DockStyle.Fill;
            tvChunks.BackColor = Colors.PrimaryHighlight();
            tvChunks.ForeColor = Colors.Font();
            tvChunks.BorderStyle = BorderStyle.None;
            tvChunks.HideSelection = false;
            tvChunks.ShowLines = true;
            tvChunks.AfterSelect += OnInnerAfterSelect;

            Controls.Add(tvChunks);
        }

        // ── Read-only binding (Phase 7 parity) ──────────────────────────────

        /// <summary>
        /// Renders the chunk tree from an immutable <see cref="IffDocument"/>. Read-only consumers
        /// (the TRE Browser) use this entry point. Each <see cref="TreeNode.Tag"/> references the
        /// bound <see cref="IffChunk"/> from the read result. Node labels use the exact Phase 7
        /// format <c>TAG [SubType]  ·  N bytes  ·  @offset</c>.
        /// </summary>
        public void LoadDocument(IffDocument doc)
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

        // ── Editable binding (08-04 — MutableIffDocument) ────────────────────

        /// <summary>
        /// Renders the chunk tree from a mutable <see cref="MutableIffDocument"/>. Editable
        /// consumers (the IFF Editor in 08-04) use this entry point. Each <see cref="TreeNode.Tag"/>
        /// references the bound <see cref="MutableIffNode"/> so the host can map selection back to
        /// the model for structural ops (add / remove / rename-retag / reorder / duplicate /
        /// edit-FORM-subtype) and leaf-payload edits.
        ///
        /// <para>Node labels use the format <c>TAG [SubType]  ·  N bytes</c> — no @offset, since
        /// the mutable model has no fixed file offset once edited. The host may suffix dirty/added
        /// markers (per the UI-SPEC <c>●</c>/<c>＋</c> glyph convention) after this call by reading
        /// <see cref="MutableIffNode.IsDirty"/>.</para>
        /// </summary>
        public void LoadMutable(MutableIffDocument doc)
        {
            tvChunks.BeginUpdate();
            tvChunks.Nodes.Clear();
            if (doc != null && doc.Root != null)
            {
                tvChunks.Nodes.Add(BuildMutableNode(doc.Root));
                tvChunks.Nodes[0].Expand();
            }
            tvChunks.EndUpdate();
        }

        /// <summary>
        /// Refreshes the tree from the same <see cref="MutableIffDocument"/> after a structural or
        /// payload edit. Equivalent to calling <see cref="LoadMutable"/> again. Provided for clarity
        /// at the host call site (08-04 dirties the tree after every edit op).
        /// </summary>
        public void RefreshMutable(MutableIffDocument doc)
        {
            LoadMutable(doc);
        }

        // ── Node builders ───────────────────────────────────────────────────

        /// <summary>
        /// Builds a <see cref="TreeNode"/> from an immutable <see cref="IffChunk"/>. The label is
        /// EXACTLY Phase 7's format — preserved verbatim from <c>TreDetailPane.BuildChunkNode</c>
        /// for parity (CON-T-05 / read-only TRE Browser unchanged).
        /// </summary>
        private TreeNode BuildChunkNode(IffChunk chunk)
        {
            var container = chunk as IffContainerChunk;
            string label = container != null
                ? chunk.TypeId + " " + container.SubTypeId + "  ·  " + chunk.LengthBytes + " bytes  ·  @" + chunk.OffsetBytes
                : chunk.TypeId + "  ·  " + chunk.LengthBytes + " bytes  ·  @" + chunk.OffsetBytes;

            var node = new TreeNode(label) { Tag = chunk };
            if (container != null)
            {
                foreach (IffChunk child in container.Children)
                {
                    node.Nodes.Add(BuildChunkNode(child));
                }
            }
            return node;
        }

        /// <summary>
        /// Builds a <see cref="TreeNode"/> from a <see cref="MutableIffNode"/>. The label uses the
        /// same TAG · size shape Phase 7 used, minus the @offset (the mutable model has no fixed
        /// file offset after editing). The host can override / decorate labels per the UI-SPEC
        /// dirty/added markers (<c>●</c>/<c>＋</c>) after the build.
        /// </summary>
        private TreeNode BuildMutableNode(MutableIffNode mut)
        {
            string label;
            if (mut.Kind == MutableIffNodeKind.Container)
            {
                label = mut.TypeId + " " + (mut.SubTypeId ?? "") + "  ·  " + MutableNodeByteCount(mut) + " bytes";
            }
            else
            {
                label = mut.TypeId + "  ·  " + mut.PayloadLength + " bytes";
            }

            var node = new TreeNode(label) { Tag = mut };
            if (mut.Kind == MutableIffNodeKind.Container)
            {
                foreach (MutableIffNode child in mut.Children)
                {
                    node.Nodes.Add(BuildMutableNode(child));
                }
            }
            return node;
        }

        /// <summary>
        /// Rough byte count for a mutable container node — sum of children's recursive byte counts
        /// plus the 4-byte SubTypeId. This is a display-only convenience for the editable label; it
        /// is NOT the authoritative serialized size (which the writer computes on save with proper
        /// container framing). For a clean container that still holds its captured slice, the
        /// captured length is exact; for a dirty container the sum-of-children value is an
        /// approximation pending re-roll.
        /// </summary>
        private static long MutableNodeByteCount(MutableIffNode mut)
        {
            if (mut.Kind == MutableIffNodeKind.Leaf)
            {
                return mut.PayloadLength;
            }
            long total = 4L; // SubTypeId
            foreach (MutableIffNode child in mut.Children)
            {
                // Each child contributes its own tag(4) + length(4) + payload — approximate via
                // the recursive byte count for containers, payload for leaves.
                total += 8L + MutableNodeByteCount(child);
            }
            return total;
        }

        // ── Selection event forwarding ───────────────────────────────────────

        private void OnInnerAfterSelect(object sender, TreeViewEventArgs e)
        {
            TreeViewEventHandler h = AfterSelect;
            if (h != null) h(this, e);
        }
    }
}
