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
using UtinniCore.Utinni;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.Saving;

namespace TJT.Saving
{
    /// <summary>
    /// Off-UI-thread file save targets for the IFF Editor — D-05.1 (loose override) and
    /// D-05.2 (Save / Save-As) (Phase 8 Plan 05 Task 2). Both paths serialize via
    /// <see cref="IffWriter.Write(MutableIffDocument)"/> and write a single file with a
    /// <c>Flush(true)</c> barrier before the awaiter completes — preventing the
    /// stale-bytes reload race (08-REVIEWS MEDIUM-9).
    ///
    /// <para><b>D-05.3 + D-05.4 (live patch + .tre repack) are NOT here</b> — they live in
    /// 08-06 and 08-07 with their own provenance gating.</para>
    ///
    /// <para><b>Root containment (08-REVIEWS MEDIUM-8):</b> loose-override paths are
    /// constructed via the framework-side <see cref="LooseOverridePath.Resolve(string, string)"/>
    /// helper which enforces rooted/.. rejection + normalized StartsWith. Save/Save-As paths
    /// come from a <see cref="System.Windows.Forms.SaveFileDialog"/> (user-chosen) and bypass
    /// the helper.</para>
    /// </summary>
    public static class IffSaveTargets
    {
        // ─────────────────────────────────────────────────────────────────────
        // Result type
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Outcome of a save attempt.</summary>
        public sealed class SaveResult
        {
            public bool Ok { get; private set; }
            public string Path { get; private set; }
            public string Message { get; private set; }

            public static SaveResult Success(string path)
            {
                return new SaveResult { Ok = true, Path = path, Message = null };
            }

            public static SaveResult Failure(string message)
            {
                return new SaveResult { Ok = false, Path = null, Message = message };
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // (1) Save as loose override under the resolved client root + sub-directory
        //     (D-05.1). Round-2 MEDIUM 10: the resolved sub-directory is Tier-4-only;
        //     callers fall back to Save As… on failure and persist the user-chosen dir.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes the document as a loose-override file under
        /// <c>resolvedRoot/looseOverrideSubDir/&lt;logical-path-from-source&gt;</c>.
        ///
        /// <para>On <see cref="OpenSource.Unknown"/> source: returns failure with the
        /// round-2 MEDIUM 5 message ("Cannot resolve archive record — use Save As to write
        /// to a chosen file.") — there is no derivable logical path.</para>
        /// </summary>
        public static async Task<SaveResult> SaveLooseOverride(
            MutableIffDocument doc,
            OpenSource source,
            string resolvedRoot,
            string looseOverrideSubDir)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            if (source == null) throw new ArgumentNullException("source");
            if (string.IsNullOrEmpty(resolvedRoot))
            {
                return SaveResult.Failure("Loose-override base directory is not configured.");
            }

            // Derive the relative logical path from the source. Round-2 MEDIUM 5: Unknown has
            // no usable logical path — fail with the documented Save-As message.
            string relAssetPath;
            var tre = source as OpenSource.TreArchive;
            var loose = source as OpenSource.LooseFile;
            if (tre != null)
            {
                relAssetPath = tre.LogicalPath;
            }
            else if (loose != null)
            {
                // 15-20 (15-SMOKE Checklist D-ii): a raw Open… dialog yields only an absolute
                // filesystem path. Deriving relAssetPath as Path.GetFileName(loose.Path) flattened
                // the override (loose\ui_auc.stf) so the SWG client — which resolves by the logical
                // subpath (string\en\ui_auc.stf) — silently never picked it up. Recover the logical
                // subpath by making the opened absolute path relative to the resolved client root
                // (loose-prefix stripped for round-trip parity). Outside the root, TryFromAbsolute
                // returns false and we keep the original filename fallback (degrade, never throw).
                relAssetPath = LogicalAssetPath.TryFromAbsolute(loose.Path, resolvedRoot, out string logical)
                    ? logical
                    : Path.GetFileName(loose.Path);
            }
            else
            {
                // ClientMemory or Unknown — no logical path is derivable in a way the client
                // will reload from (Unknown is the degraded-fallback sentinel).
                return SaveResult.Failure(
                    "Cannot resolve archive record — use Save As to write to a chosen file.");
            }

            if (string.IsNullOrEmpty(relAssetPath))
            {
                return SaveResult.Failure("Asset has no logical path — use Save As instead.");
            }

            // Compose the override base = resolvedRoot/looseOverrideSubDir; both legs go
            // through LooseOverridePath defenses (canonicalize, '..' rejection, StartsWith
            // gate) one section at a time.
            string overrideBase;
            try
            {
                overrideBase = string.IsNullOrEmpty(looseOverrideSubDir)
                    ? resolvedRoot
                    : LooseOverridePath.Resolve(resolvedRoot, looseOverrideSubDir);
            }
            catch (ArgumentException ex)
            {
                return SaveResult.Failure("Invalid loose-override sub-directory: " + ex.Message);
            }

            string fullPath;
            try
            {
                fullPath = LooseOverridePath.Resolve(overrideBase, relAssetPath);
            }
            catch (ArgumentException ex)
            {
                return SaveResult.Failure("Invalid loose-override asset path: " + ex.Message);
            }

            // Serialize on a background Task. IffWriter.Write throws on over-cap chunks
            // (08-01 64 MB cap) — surface as a save-time validation, not a crash.
            try
            {
                await Task.Run(() => WriteAtomic(doc, fullPath)).ConfigureAwait(false);
                return SaveResult.Success(fullPath);
            }
            catch (IffParseException ex)
            {
                return SaveResult.Failure("Save failed (over-cap chunk?): " + ex.Message);
            }
            catch (Exception ex)
            {
                return SaveResult.Failure("Save failed: " + ex.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // (2) Save to a user-chosen path (D-05.2 Save As…). Accepts ANY OpenSource
        //     including Unknown (round-2 MEDIUM 5: Save-As is the only enabled save
        //     mode on Unknown — the user picks the destination so no logical path is
        //     needed).
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes the document to <paramref name="path"/> (an absolute, user-chosen file
        /// path). Works for ANY OpenSource including <see cref="OpenSource.Unknown"/>.
        /// </summary>
        public static async Task<SaveResult> SaveToPath(MutableIffDocument doc, string path)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            if (string.IsNullOrEmpty(path))
            {
                return SaveResult.Failure("No destination path was provided.");
            }
            try
            {
                await Task.Run(() => WriteAtomic(doc, path)).ConfigureAwait(false);
                return SaveResult.Success(path);
            }
            catch (IffParseException ex)
            {
                return SaveResult.Failure("Save failed (over-cap chunk?): " + ex.Message);
            }
            catch (Exception ex)
            {
                return SaveResult.Failure("Save failed: " + ex.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // (3) Save in place — overwrites the original loose file. ONLY enabled for
        //     OpenSource.LooseFile (W-3 contract: Unknown/TreArchive/ClientMemory all
        //     fail this match).
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Overwrites the original loose file when <paramref name="source"/> is
        /// <see cref="OpenSource.LooseFile"/>. Returns failure on any other source
        /// (per the W-3 contract — Unknown / TreArchive / ClientMemory cannot
        /// resolve a unique on-disk path to overwrite).
        /// </summary>
        public static async Task<SaveResult> SaveInPlace(MutableIffDocument doc, OpenSource source)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            if (source == null) throw new ArgumentNullException("source");
            var loose = source as OpenSource.LooseFile;
            if (loose == null)
            {
                return SaveResult.Failure(
                    "Cannot resolve archive record — use Save As to write to a chosen file.");
            }
            return await SaveToPath(doc, loose.Path).ConfigureAwait(false);
        }

        // ─────────────────────────────────────────────────────────────────────
        // (4) ini persistence helper — round-2 MEDIUM 10 Save-As fallback path.
        //     When the planner's-best-guess loose-override subdir was wrong and the
        //     user fell back to Save As…, persist the CHOSEN DIRECTORY into
        //     [IffEditor] looseOverrideDir so the NEXT loose-override save defaults
        //     to the correct location.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Records the parent directory of <paramref name="chosenFilePath"/> in
        /// <c>[IffEditor] looseOverrideDir</c> for the next session. Best-effort —
        /// never throws.
        /// </summary>
        public static void RecordSaveAsDirectory(UtINI ini, string chosenFilePath)
        {
            try
            {
                if (ini == null || string.IsNullOrEmpty(chosenFilePath)) return;
                string dir = Path.GetDirectoryName(chosenFilePath);
                if (string.IsNullOrEmpty(dir)) return;
                ini.AddSetting("IffEditor", "looseOverrideDir", dir, UtINI.Value.Types.VtString);
                ini.Save();
            }
            catch
            {
                // Persistence is best-effort; the next-session default is a convenience, not
                // a correctness requirement.
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core write — serialize via IffWriter then Flush(true) BEFORE returning
        // (08-REVIEWS MEDIUM-9 stale-bytes reload race barrier).
        // ─────────────────────────────────────────────────────────────────────

        private static void WriteAtomic(MutableIffDocument doc, string path)
        {
            byte[] bytes = IffWriter.Write(doc);
            // Create parent directory under the resolved root.
            string parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }
            // FileMode.Create truncates; Flush(true) flushes through the OS buffer to disk
            // BEFORE the awaiter signals completion, so a follow-up Reload-in-client cannot
            // race against still-buffered bytes (MEDIUM-9).
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
        }
    }
}
