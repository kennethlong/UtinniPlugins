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
// Terrain (.trn / FORM TGEN) in-proc field-aware save target — the editor-side mirror of the shipped
// Phase 20 apply-save-trn CLI (Utinni.Cli/Commands/ApplySaveTrnCommand.cs). It consumes the Phase 20 codec
// (TrnFieldEncoder + the MutableIff DOM) with ZERO new format logic: it packs no byte offsets of its own
// (TgenFieldLayouts is the single offset source) and re-emits via IffWriter.Write through the held
// MutableIffDocument. The on-disk LAYR -> IHDR active-flag leaf + the typed FORM<tag> -> FORM<version> ->
// DATA shape were understood from swg-client-v2 .../sharedTerrain/.../generator/TerrainGenerator.cpp
// (SOE/Bootprint, All Rights Reserved) and the EA-IFF-85 public standard — only the on-disk layout was
// studied; no code, comments, identifier names, or test fixtures copied from any reference source.
// Implementation original to Utinni under MIT.

using System;
using System.IO;
using System.Threading.Tasks;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.Formats.Terrain;
using UtinniCoreDotNet.Saving;

namespace TJT.Saving
{
    /// <summary>
    /// In-proc field-aware terrain save target — the editor-side mirror of the shipped
    /// <c>apply-save-trn</c> CLI (<c>ApplySaveTrnCommand</c>), chosen over a CLI shell-out (21-01 planner
    /// decision: in-proc is the established TJT save idiom — see <see cref="IffSaveTargets"/> — and avoids a
    /// process re-launch + re-parse). The 21-01 byte-parity test proves this path's output is byte-identical
    /// to the CLI for BOTH a typed fixed-length field edit AND the <c>--field active</c> IHDR int32 edit.
    ///
    /// <para><b>Edit-save sequence (mirrors <see cref="UtinniCoreDotNet.Formats.Terrain.TrnFieldEncoder"/>'s
    /// exact-span contract):</b> locate the target <c>DATA</c> leaf by stable id → gate on
    /// <see cref="TerrainNode.IsEditable"/> (reject raw-preserved / dead-skipped BEFORE encoding) →
    /// <c>TrnFieldEncoder.EncodeField</c> (same-length payload; <c>ArgumentException</c> surfaced as a
    /// failure, never bubbled) → <see cref="MutableIffNode.SetPayload"/> (dirties ONE leaf) →
    /// <see cref="TerrainDocument.Serialize"/> (= <c>IffWriter.Write</c>) → fail-closed
    /// <see cref="LooseOverridePath.Resolve"/> <c>--root</c> containment → atomic write. NO byte-offset
    /// packing lives here (<see cref="TgenFieldLayouts"/> is the single offset source).</para>
    ///
    /// <para><b>Active-flag leaf addressing (RESEARCH OQ2 resolution, net-new public bridge):</b> the consumed
    /// model exposes only <see cref="TerrainLayer.StableIdPath"/> (the LAYR FORM path), NOT the
    /// <c>IHDR</c> <c>DATA</c> leaf id that <c>--field active</c> mutates, and the CLI's
    /// <c>FindMutableLeafByStableId</c> walk is PRIVATE to <c>ApplySaveTrnCommand</c>. This class adds the
    /// net-new PUBLIC <see cref="ResolveIhdrLeafStableId"/> bridge from a selected layer's LAYR FORM stable id
    /// to its IHDR DATA leaf stable id, asserting the parent FORM is <c>IHDR</c> (mirrors the CLI's
    /// parent-is-IHDR rule). Plan 02 consumes it for the active-flag toggle.</para>
    /// </summary>
    public static class TerrainSaveTargets
    {
        private const string LayerItemHeaderForm = "IHDR";
        private const string LayerForm = "LAYR";
        private const string DataLeafTypeId = "DATA";

        // ─────────────────────────────────────────────────────────────────────
        // Result type (mirrors IffSaveTargets.SaveResult Ok/Path/Message so the SubPanel maps it to
        // "Saved -> <path>" / "Save failed: <reason>" status copy).
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Outcome of an in-proc terrain field-edit save attempt.</summary>
        public sealed class SaveResult
        {
            /// <summary>True iff the edit applied and the bytes were written.</summary>
            public bool Ok { get; private set; }

            /// <summary>The written file path on success; null on failure.</summary>
            public string Path { get; private set; }

            /// <summary>The failure reason on failure; null on success.</summary>
            public string Message { get; private set; }

            /// <summary>Constructs a success result.</summary>
            public static SaveResult Success(string path)
            {
                return new SaveResult { Ok = true, Path = path, Message = null };
            }

            /// <summary>Constructs a failure result.</summary>
            public static SaveResult Failure(string message)
            {
                return new SaveResult { Ok = false, Path = null, Message = message };
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Net-new PUBLIC bridge: LAYR FORM stable id -> IHDR DATA leaf stable id (RESEARCH OQ2).
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the <c>IHDR</c> layer-item-header <c>DATA</c> leaf's stable id for the LAYR FORM addressed
        /// by <paramref name="layerFormStableId"/> — the leaf the <c>--field active</c> write mutates (int32 at
        /// offset 0). Walks <paramref name="doc"/> re-deriving stable ids via
        /// <see cref="MutableIffDocument.DeriveStableId"/> (the same parentPrefix/ordinal shape the CLI's
        /// private <c>FindMutableLeafRecursive</c> uses), locates the LAYR FORM container whose stable id ==
        /// <paramref name="layerFormStableId"/>, and returns the stable id of its <c>IHDR</c> child FORM's
        /// <c>DATA</c> leaf — asserting that leaf's parent FORM <see cref="MutableIffNode.SubTypeId"/> is
        /// <c>IHDR</c> (mirrors <c>ApplySaveTrnCommand.ResolveFieldContext</c>'s parent-is-IHDR rule). This is
        /// NET-NEW public API; the CLI's private <c>FindMutableLeafByStableId</c> is NOT reused.
        /// </summary>
        /// <param name="doc">The decoded terrain document's mutable DOM (<see cref="TerrainDocument.Mutable"/>).</param>
        /// <param name="layerFormStableId">The selected layer's <see cref="TerrainLayer.StableIdPath"/> (LAYR FORM path).</param>
        /// <returns>The stable id of the layer's IHDR DATA leaf.</returns>
        /// <exception cref="ArgumentNullException">A required argument was null.</exception>
        /// <exception cref="ArgumentException">No LAYR FORM matched, or it has no IHDR DATA child leaf.</exception>
        public static string ResolveIhdrLeafStableId(MutableIffDocument doc, string layerFormStableId)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            if (string.IsNullOrEmpty(layerFormStableId)) throw new ArgumentNullException("layerFormStableId");

            MutableIffNode layerForm = FindNodeByStableId(doc, layerFormStableId);
            if (layerForm == null || layerForm.Kind != MutableIffNodeKind.Container)
            {
                throw new ArgumentException(
                    "No LAYR FORM container found for stable id '" + layerFormStableId + "'.", "layerFormStableId");
            }

            // Mirror TgenDecoder.DecodeLayer: a real LAYR FORM nests its IHDR (+ child nodes) under a single
            // version-form body, which contributes a stable-id segment. Descend into it so the IHDR DATA leaf
            // id we return resolves through FindNodeByStableId on the TRUE DOM — the 21-04 live-smoke
            // "no IHDR DATA child leaf" defect on real high-era terrain (naboo.trn). The synthesizer's
            // collapsed fixture (layer FORM sub-type "0003", not "LAYR") skips this descent unchanged.
            MutableIffNode walkRoot = layerForm;
            string walkPrefix = layerFormStableId + "/";
            if (string.Equals(layerForm.SubTypeId, LayerForm, StringComparison.Ordinal))
            {
                MutableIffNode versionBody = FirstContainerChild(layerForm);
                if (versionBody != null)
                {
                    walkRoot = versionBody;
                    walkPrefix = layerFormStableId + "/"
                        + MutableIffDocument.DeriveStableId(versionBody, "", IndexOf(layerForm, versionBody)) + "/";
                }
            }

            // Walk walkRoot's children for the IHDR container, then its DATA leaf, re-deriving the leaf's
            // stable id with the SAME prefix shape DeriveStableId produces.
            for (int i = 0; i < walkRoot.Children.Count; i++)
            {
                MutableIffNode child = walkRoot.Children[i];
                string childId = MutableIffDocument.DeriveStableId(child, walkPrefix, i);
                if (child.Kind == MutableIffNodeKind.Container
                    && string.Equals(child.SubTypeId, LayerItemHeaderForm, StringComparison.Ordinal))
                {
                    string ihdrPrefix = childId + "/";
                    for (int j = 0; j < child.Children.Count; j++)
                    {
                        MutableIffNode grandChild = child.Children[j];
                        if (grandChild.Kind == MutableIffNodeKind.Leaf
                            && string.Equals(grandChild.TypeId, DataLeafTypeId, StringComparison.Ordinal))
                        {
                            // Parent FORM is IHDR (asserted by the enclosing branch) — return the leaf stable id.
                            return MutableIffDocument.DeriveStableId(grandChild, ihdrPrefix, j);
                        }
                    }
                }
            }

            throw new ArgumentException(
                "LAYR FORM '" + layerFormStableId + "' has no IHDR DATA child leaf (cannot address active flag).",
                "layerFormStableId");
        }

        /// <summary>
        /// Resolves the editable <c>DATA</c> leaf's stable id for the typed terrain node addressed by
        /// <paramref name="nodeFormStableId"/> — the <see cref="TerrainNode.StableIdPath"/> exposed by the
        /// decoded model is the node's <c>FORM&lt;tag&gt;</c> path, NOT the leaf id <c>apply-save-trn</c>
        /// mutates. A typed node is <c>FORM&lt;tag&gt; -&gt; FORM&lt;version&gt; -&gt; DATA</c> (see
        /// <c>TgenDecoder.DecodeNode</c>): this walks the node FORM's child version FORM(s) for the first
        /// <c>DATA</c> leaf and returns its stable id (re-derived via
        /// <see cref="MutableIffDocument.DeriveStableId"/> with the same parentPrefix/ordinal shape the
        /// decoder uses). NET-NEW public bridge — the consuming editor (Plan 02) must NOT hand-roll this
        /// walk, and the CLI's private leaf walk is NOT reused.
        /// </summary>
        /// <param name="doc">The decoded terrain document's mutable DOM (<see cref="TerrainDocument.Mutable"/>).</param>
        /// <param name="nodeFormStableId">The selected typed node's <see cref="TerrainNode.StableIdPath"/> (FORM&lt;tag&gt; path).</param>
        /// <returns>The stable id of the node's editable DATA leaf.</returns>
        /// <exception cref="ArgumentNullException">A required argument was null.</exception>
        /// <exception cref="ArgumentException">No node FORM matched, or it has no DATA leaf under a version FORM.</exception>
        public static string ResolveTypedDataLeafStableId(MutableIffDocument doc, string nodeFormStableId)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            if (string.IsNullOrEmpty(nodeFormStableId)) throw new ArgumentNullException("nodeFormStableId");

            MutableIffNode nodeForm = FindNodeByStableId(doc, nodeFormStableId);
            if (nodeForm == null || nodeForm.Kind != MutableIffNodeKind.Container)
            {
                throw new ArgumentException(
                    "No typed node FORM container found for stable id '" + nodeFormStableId + "'.", "nodeFormStableId");
            }

            // FORM<tag> -> FORM<version> -> DATA. Walk the version FORM children for the first DATA leaf.
            string nodePrefix = nodeFormStableId + "/";
            for (int i = 0; i < nodeForm.Children.Count; i++)
            {
                MutableIffNode versionForm = nodeForm.Children[i];
                string versionId = MutableIffDocument.DeriveStableId(versionForm, nodePrefix, i);
                if (versionForm.Kind != MutableIffNodeKind.Container) continue;
                string versionPrefix = versionId + "/";
                for (int j = 0; j < versionForm.Children.Count; j++)
                {
                    MutableIffNode g = versionForm.Children[j];
                    if (g.Kind == MutableIffNodeKind.Leaf
                        && string.Equals(g.TypeId, DataLeafTypeId, StringComparison.Ordinal))
                    {
                        return MutableIffDocument.DeriveStableId(g, versionPrefix, j);
                    }
                }
            }

            throw new ArgumentException(
                "Typed node FORM '" + nodeFormStableId + "' has no DATA leaf under a version FORM.",
                "nodeFormStableId");
        }

        // ─────────────────────────────────────────────────────────────────────
        // In-proc edit -> save under the loose-override matrix (D-08) with fail-closed --root containment.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Applies ONE fixed-length field edit (scalar / enum / familyId / active) to the leaf addressed by
        /// <paramref name="stableLeafId"/> in <paramref name="terrain"/>'s mutable DOM, then writes the
        /// re-emitted bytes as a loose override under <paramref name="resolvedRoot"/> at the logical path
        /// derived from <paramref name="source"/>. Mirrors <c>apply-save-trn</c> in-proc: it gates on
        /// <see cref="TerrainNode.IsEditable"/> for a typed edit (raw / dead / truncated nodes are rejected
        /// BEFORE encoding), routes the encode through the single-source <see cref="TrnFieldEncoder"/> (the
        /// <c>--field active</c> path uses the IHDR descriptor exactly as the CLI does), and resolves the
        /// destination through the fail-closed <see cref="LooseOverridePath.Resolve"/> containment so a
        /// path-escaping logical path returns <see cref="SaveResult.Failure"/> and never writes.
        /// </summary>
        /// <param name="terrain">The decoded terrain document (its <see cref="TerrainDocument.Mutable"/> is edited).</param>
        /// <param name="source">Document provenance (TRE / loose); supplies the logical override path.</param>
        /// <param name="resolvedRoot">Absolute client root for the loose-override destination.</param>
        /// <param name="stableLeafId">The stable id of the DATA leaf to edit (typed leaf, or the IHDR leaf for <paramref name="fieldName"/> == "active").</param>
        /// <param name="tag">The enclosing typed FORM tag (or "IHDR" for the active edit).</param>
        /// <param name="version">The enclosing version FORM (or "active" for the active edit).</param>
        /// <param name="fieldName">The descriptor field name to edit ("active" for the IHDR layer flag).</param>
        /// <param name="value">The new value (invariant-culture per the field's parser).</param>
        public static async Task<SaveResult> SaveLooseOverride(
            TerrainDocument terrain,
            OpenSource source,
            string resolvedRoot,
            string stableLeafId,
            string tag,
            string version,
            string fieldName,
            string value)
        {
            if (terrain == null) throw new ArgumentNullException("terrain");
            if (source == null) throw new ArgumentNullException("source");
            if (string.IsNullOrEmpty(resolvedRoot))
            {
                return SaveResult.Failure("Loose-override base directory is not configured.");
            }

            // Derive the relative logical path from the source (TRE -> LogicalPath; loose -> made relative to
            // the resolved root). Unknown / ClientMemory have no client-resolvable logical path.
            string relAssetPath;
            var tre = source as OpenSource.TreArchive;
            var loose = source as OpenSource.LooseFile;
            if (tre != null)
            {
                relAssetPath = tre.LogicalPath;
            }
            else if (loose != null)
            {
                relAssetPath = LogicalAssetPath.TryFromAbsolute(loose.Path, resolvedRoot, out string logical)
                    ? logical
                    : System.IO.Path.GetFileName(loose.Path);
            }
            else
            {
                return SaveResult.Failure(
                    "Cannot resolve archive record — use Save As to write to a chosen file.");
            }

            if (string.IsNullOrEmpty(relAssetPath))
            {
                return SaveResult.Failure("Asset has no logical path — use Save As instead.");
            }

            // Apply the edit to the held mutable DOM (dirties ONE leaf). Encoder/gate failures are returned as
            // SaveResult.Failure — never bubbled (Pitfall 5).
            SaveResult editResult = ApplyFieldEdit(terrain, stableLeafId, tag, version, fieldName, value);
            if (editResult != null)
            {
                // ApplyFieldEdit returns non-null ONLY on failure.
                return editResult;
            }

            // Resolve the destination through the fail-closed --root containment (same gate as apply-save-trn).
            string fullPath;
            try
            {
                fullPath = LooseOverridePath.Resolve(resolvedRoot, relAssetPath);
            }
            catch (ArgumentException ex)
            {
                return SaveResult.Failure("Save failed: " + ex.Message);
            }

            byte[] bytes = terrain.Serialize();
            try
            {
                await Task.Run(() => WriteAtomic(fullPath, bytes)).ConfigureAwait(false);
                return SaveResult.Success(fullPath);
            }
            catch (Exception ex)
            {
                return SaveResult.Failure("Save failed: " + ex.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Edit core — locate the leaf, gate editability, encode ONE field, SetPayload. Returns null on
        // success (the DOM is mutated in place) or a Failure SaveResult on any rejection (no DOM mutation).
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Applies ONE field edit to <paramref name="terrain"/>'s mutable DOM in place. Returns <c>null</c> on
        /// success; a <see cref="SaveResult.Failure"/> on any rejection (leaf-not-found, non-editable node,
        /// encoder rejection) WITHOUT mutating the DOM. Public so the manual Preview path (Plan 02) can apply
        /// the same edit in-memory before dispatching a reload.
        /// </summary>
        public static SaveResult ApplyFieldEdit(
            TerrainDocument terrain,
            string stableLeafId,
            string tag,
            string version,
            string fieldName,
            string value)
        {
            if (terrain == null) throw new ArgumentNullException("terrain");
            if (string.IsNullOrEmpty(stableLeafId)) return SaveResult.Failure("No leaf id was provided.");

            MutableIffNode leaf = FindNodeByStableId(terrain.Mutable, stableLeafId);
            if (leaf == null || leaf.Kind != MutableIffNodeKind.Leaf)
            {
                return SaveResult.Failure("Edit target leaf not found: " + stableLeafId);
            }

            bool isActiveEdit = string.Equals(fieldName, TgenFieldLayouts.ActiveFieldName, StringComparison.Ordinal)
                && string.Equals(tag, LayerItemHeaderForm, StringComparison.Ordinal);

            // Gate a typed edit on the decoded node's editability (raw / dead / truncated are rejected before
            // encoding — mirrors ApplySaveTrnCommand). The active flag lives on an IHDR header (always editable
            // when present) and is exempt from the typed-node gate, exactly as the CLI is.
            if (!isActiveEdit && !TargetTypedNodeIsEditable(terrain, stableLeafId))
            {
                return SaveResult.Failure(
                    "Target is a non-editable (raw-fallback / truncated / obsolete) node; refusing to rewrite a half-understood payload.");
            }

            byte[] original = leaf.GetPayloadCopy();
            byte[] edited;
            try
            {
                edited = TrnFieldEncoder.EncodeField(original, tag, version, fieldName, value);
            }
            catch (ArgumentException ex)
            {
                // Surface the encoder message (var-length / NaN / wrong-type / span overflow) as a failure —
                // never let it bubble (Pitfall 5).
                return SaveResult.Failure(ex.Message);
            }

            if (edited.Length != original.Length)
            {
                // Fixed-length scope guard (D-05) — a length change would ripple parent FORM lengths.
                return SaveResult.Failure("fixed-length edit produced a different-length payload; nothing written.");
            }

            leaf.SetPayload(edited);
            return null; // success — DOM mutated in place
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stable-id leaf/node walk — NET-NEW in this class (the CLI's FindMutableLeafByStableId is private).
        // ─────────────────────────────────────────────────────────────────────

        private static MutableIffNode FindNodeByStableId(MutableIffDocument doc, string stableId)
        {
            if (doc == null || doc.Root == null) return null;
            return FindNodeRecursive(doc.Root, "", 0, stableId);
        }

        // The first container (FORM) child of a node, or null. Mirrors TgenDecoder.FirstContainerChild so the
        // LAYR -> version-body descent is identical on both the decode and save sides.
        private static MutableIffNode FirstContainerChild(MutableIffNode node)
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                if (node.Children[i].Kind == MutableIffNodeKind.Container) return node.Children[i];
            }
            return null;
        }

        // The physical child index of `child` within `parent` (DeriveStableId ordinal). 0 if not found.
        private static int IndexOf(MutableIffNode parent, MutableIffNode child)
        {
            for (int i = 0; i < parent.Children.Count; i++)
            {
                if (ReferenceEquals(parent.Children[i], child)) return i;
            }
            return 0;
        }

        private static MutableIffNode FindNodeRecursive(MutableIffNode node, string parentPrefix, int ordinal, string targetId)
        {
            string thisId = MutableIffDocument.DeriveStableId(node, parentPrefix, ordinal);
            if (thisId == targetId) return node;
            if (node.Kind == MutableIffNodeKind.Container)
            {
                string childPrefix = thisId + "/";
                for (int i = 0; i < node.Children.Count; i++)
                {
                    MutableIffNode found = FindNodeRecursive(node.Children[i], childPrefix, i, targetId);
                    if (found != null) return found;
                }
            }
            return null;
        }

        // True when the decoded typed node enclosing the addressed leaf is editable (raw / dead are not).
        // Anchors the prefix match on a path-segment boundary so node "1" is not a prefix of node "10".
        private static bool TargetTypedNodeIsEditable(TerrainDocument terrain, string leafId)
        {
            foreach (var layer in terrain.Layers)
            {
                bool? editable = WalkLayerForNode(layer, leafId);
                if (editable != null) return editable == true;
            }
            return false;
        }

        private static bool? WalkLayerForNode(TerrainLayer layer, string leafId)
        {
            foreach (var node in layer.Nodes)
            {
                if (node.StableIdPath != null && IsSelfOrDescendantId(leafId, node.StableIdPath))
                    return node.IsEditable;
            }
            foreach (var sub in layer.SubLayers)
            {
                bool? r = WalkLayerForNode(sub, leafId);
                if (r != null) return r;
            }
            return null;
        }

        private static bool IsSelfOrDescendantId(string leafId, string nodeStableId)
        {
            return leafId == nodeStableId
                || leafId.StartsWith(nodeStableId + "/", StringComparison.Ordinal);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core write — serialize-then-Flush(true) barrier (mirrors IffSaveTargets.WriteAtomic stale-bytes
        // reload-race guard). The bytes are already serialized by the caller (terrain.Serialize()).
        // ─────────────────────────────────────────────────────────────────────

        private static void WriteAtomic(string path, byte[] bytes)
        {
            string parent = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
        }
    }
}
