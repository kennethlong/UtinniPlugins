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
using System.IO;
using System.Threading.Tasks;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.Formats.StringTable;
using UtinniCoreDotNet.Saving;

namespace TJT.Saving
{
    /// <summary>
    /// Save-target shim for the Phase 10 string-table editor (D-04). The flat-format analog of the Phase
    /// 9 <see cref="DatatableSaveTargets"/>, but composing at the BYTES layer: <c>.stf</c> is a flat
    /// little-endian binary, NOT an IFF container, so it cannot flow through
    /// <see cref="IffSaveTargets"/>' <c>MutableIffDocument</c> signature. Each save mode builds raw bytes
    /// via 10-01's <see cref="StringTableWriter.Serialize"/> and writes them with the SAME atomic-write +
    /// <c>Flush(true)</c> stale-bytes barrier (Phase 8 MEDIUM-9) inlined here; the loose-override path uses
    /// the Phase 8 <see cref="LooseOverridePath.Resolve"/> root-containment defense, and the repack path
    /// forwards the bytes to the byte-payload-based <see cref="TreRepackSaveTarget.Apply"/> (V6000 refusal
    /// + timestamped backup inherited). Reuses <see cref="IffSaveTargets.SaveResult"/> so the editor's
    /// save-flow is identical to the datatable shape. NO <c>DataGridView</c> reference.
    /// </summary>
    public static class StringTableSaveTargets
    {
        /// <summary>Overwrites the original loose <c>.stf</c> (gated on <see cref="OpenSource.LooseFile"/>).</summary>
        public static async Task<IffSaveTargets.SaveResult> SaveInPlace(MutableStringTableDocument doc, OpenSource source)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            if (source == null) throw new ArgumentNullException("source");
            var loose = source as OpenSource.LooseFile;
            if (loose == null)
            {
                return IffSaveTargets.SaveResult.Failure(
                    "Cannot resolve archive record — use Save As to write to a chosen file.");
            }
            return await SaveBytesToPath(doc, loose.Path).ConfigureAwait(false);
        }

        /// <summary>Writes the <c>.stf</c> bytes to a user-chosen path (any source, incl. Unknown).</summary>
        public static async Task<IffSaveTargets.SaveResult> SaveToPath(MutableStringTableDocument doc, string targetPath)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            if (string.IsNullOrEmpty(targetPath))
            {
                return IffSaveTargets.SaveResult.Failure("No destination path was provided.");
            }
            return await SaveBytesToPath(doc, targetPath).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes the <c>.stf</c> as a loose-override file under <c>resolvedRoot/subDir/&lt;logical-path&gt;</c>
        /// (gated on LooseFile or TreArchive; the <see cref="LooseOverridePath.Resolve"/> root-containment
        /// defense is reused verbatim).
        /// </summary>
        public static async Task<IffSaveTargets.SaveResult> SaveLooseOverride(
            MutableStringTableDocument doc, OpenSource source, string resolvedRoot, string subDir)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            if (source == null) throw new ArgumentNullException("source");
            if (string.IsNullOrEmpty(resolvedRoot))
            {
                return IffSaveTargets.SaveResult.Failure("Loose-override base directory is not configured.");
            }

            string relAssetPath;
            var tre = source as OpenSource.TreArchive;
            var loose = source as OpenSource.LooseFile;
            if (tre != null) relAssetPath = tre.LogicalPath;
            else if (loose != null) relAssetPath = Path.GetFileName(loose.Path);
            else return IffSaveTargets.SaveResult.Failure(
                "Cannot resolve archive record — use Save As to write to a chosen file.");

            if (string.IsNullOrEmpty(relAssetPath))
            {
                return IffSaveTargets.SaveResult.Failure("Asset has no logical path — use Save As instead.");
            }

            string fullPath;
            try
            {
                string overrideBase = string.IsNullOrEmpty(subDir)
                    ? resolvedRoot
                    : LooseOverridePath.Resolve(resolvedRoot, subDir);
                fullPath = LooseOverridePath.Resolve(overrideBase, relAssetPath);
            }
            catch (ArgumentException ex)
            {
                return IffSaveTargets.SaveResult.Failure("Invalid loose-override path: " + ex.Message);
            }

            return await SaveBytesToPath(doc, fullPath).ConfigureAwait(false);
        }

        /// <summary>Repacks the edited <c>.stf</c> back into its source <c>.tre</c> (V6000 refusal inherited).</summary>
        public static Task<TreRepackSaveTarget.TreRepackResult> RepackIntoSourceTre(
            MutableStringTableDocument doc, OpenSource.TreArchive ta, bool createBackup)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            if (ta == null) throw new ArgumentNullException("ta");
            byte[] bytes = StringTableWriter.Serialize(doc);
            return TreRepackSaveTarget.Apply(ta, bytes, createBackup);
        }

        // Serialize on a background Task, then atomic write with a Flush(true) barrier BEFORE the awaiter
        // completes (Phase 8 MEDIUM-9 stale-bytes reload race — inlined for the bytes layer since the .stf
        // is not an IffWriter document).
        private static async Task<IffSaveTargets.SaveResult> SaveBytesToPath(MutableStringTableDocument doc, string path)
        {
            try
            {
                await Task.Run(() => WriteAtomic(StringTableWriter.Serialize(doc), path)).ConfigureAwait(false);
                return IffSaveTargets.SaveResult.Success(path);
            }
            catch (Exception ex)
            {
                return IffSaveTargets.SaveResult.Failure("Save failed: " + ex.Message);
            }
        }

        private static void WriteAtomic(byte[] bytes, string path)
        {
            string parent = Path.GetDirectoryName(path);
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
