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
// ClientEffect (.iff / FORM CLEF) in-proc loose-override save target — the editor-side mirror of the
// shipped Phase 22 apply-save-effect CLI (Utinni.Cli/Commands/ApplySaveEffectCommand.cs). Unlike terrain's
// typed-field save, the CLEF Form has ALREADY mutated its model (the variable-length string/scalar edit +
// add/remove/reorder live in MutableClientEffect, D-01) and hands this target the FULLY SERIALIZED CLEF
// bytes (MutableClientEffect.Serialize() == IffWriter.Write). So this class owns ZERO format logic: it
// derives the relative logical path from the OpenSource, composes <root>/<loose-subdir>/<logical> through
// the fail-closed two-step LooseOverridePath.Resolve (D-10), and writes atomically with a Flush(true)
// barrier (the stale-bytes reload-race guard). The source TRE is NEVER modified (loose override only, D-09).
// The in-proc <-> CLI save-parity test (REVIEWS MEDIUM #9) proves this path and apply-save-effect converge
// byte-for-byte on the shared Plan 01 codec. Implementation original to Utinni under MIT.

using System;
using System.IO;
using System.Threading.Tasks;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.Saving;

namespace TJT.Saving
{
    /// <summary>
    /// Off-UI-thread loose-override save target for the ClientEffect editor — the editor-side mirror of the
    /// shipped <c>apply-save-effect</c> CLI (<c>ApplySaveEffectCommand</c>). The Form has already composed
    /// the byte-exact CLEF through the Plan 01 codec (<see cref="UtinniCoreDotNet.Formats.ClientEffect.MutableClientEffect.Serialize"/>),
    /// so this target takes the SERIALIZED bytes + the document provenance and writes them as a loose
    /// override under <c><paramref name="resolvedRoot"/>/<paramref name="looseOverrideSubDir"/>/&lt;logical&gt;</c>.
    ///
    /// <para><b>Path containment (T-22-path, REVIEWS MEDIUM #9):</b> the destination is composed via the
    /// SAME two-step <see cref="LooseOverridePath.Resolve(string, string)"/> as
    /// <see cref="IffSaveTargets"/>/<see cref="TerrainSaveTargets"/> — each leg fail-closes on <c>..</c>/
    /// rooted/prefix-escape, so a path-escaping logical path returns <see cref="SaveResult.Failure"/> and
    /// NEVER writes. The atomic write ends in <c>Flush(true)</c> so a follow-up in-client reload cannot race
    /// still-buffered bytes (the MEDIUM-9 stale-bytes barrier).</para>
    ///
    /// <para><b>No format logic here</b> — the codec (in <c>UtinniCoreDotNet.dll</c>) owns the bytes; this
    /// class only resolves the destination + writes. The source TRE is never modified (loose override only,
    /// D-09).</para>
    /// </summary>
    public static class ClientEffectSaveTargets
    {
        /// <summary>The documented default loose-override sub-directory (the searchPath the client toggles).</summary>
        public const string DefaultLooseSubDir = "loose";

        // ─────────────────────────────────────────────────────────────────────
        // Result type (mirrors IffSaveTargets.SaveResult Ok/Path/Message so the Form maps it to
        // "Saved -> <path>" / "Save failed: <reason>" status copy).
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Outcome of a loose-override save attempt.</summary>
        public sealed class SaveResult
        {
            /// <summary>True iff the bytes were written.</summary>
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
        // Save the (already-serialized) CLEF bytes as a loose override under
        // <resolvedRoot>/<looseOverrideSubDir>/<logical-path-from-source> (D-10). The two-step compose
        // mirrors TerrainSaveTargets/IffSaveTargets so the effect override lands on the documented loose
        // searchPath like every other editor.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes the byte-exact serialized CLEF <paramref name="serializedClef"/> as a loose override
        /// derived from <paramref name="source"/>'s logical path. Returns <see cref="SaveResult.Failure"/>
        /// (with no write) when the root is unconfigured, the source has no derivable logical path, the
        /// path escapes containment, or the write fails — never throws out to the UI handler (Pitfall 5).
        /// </summary>
        /// <param name="serializedClef">The byte-exact CLEF produced by <c>MutableClientEffect.Serialize()</c>.</param>
        /// <param name="source">Document provenance (TRE / loose); supplies the logical override path.</param>
        /// <param name="resolvedRoot">Absolute client root for the loose-override destination.</param>
        /// <param name="looseOverrideSubDir">
        /// The loose-override sub-directory composed under <paramref name="resolvedRoot"/> (defaults to
        /// <see cref="DefaultLooseSubDir"/> <c>"loose"</c>). Empty/null preserves the legacy
        /// <c>&lt;root&gt;/&lt;logical&gt;</c> destination.
        /// </param>
        public static async Task<SaveResult> SaveLooseOverride(
            byte[] serializedClef,
            OpenSource source,
            string resolvedRoot,
            string looseOverrideSubDir = DefaultLooseSubDir)
        {
            if (serializedClef == null) throw new ArgumentNullException("serializedClef");
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
                    : Path.GetFileName(loose.Path);
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

            // Compose the override base = resolvedRoot/looseOverrideSubDir (the two-step shape
            // IffSaveTargets/TerrainSaveTargets use). Each leg goes through the fail-closed LooseOverridePath
            // defenses one section at a time. An empty subdir preserves the legacy <root>/<logical> destination.
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

            try
            {
                await Task.Run(() => WriteAtomic(fullPath, serializedClef)).ConfigureAwait(false);
                return SaveResult.Success(fullPath);
            }
            catch (Exception ex)
            {
                return SaveResult.Failure("Save failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Writes the serialized CLEF to a user-chosen absolute <paramref name="path"/> (Save As…). Works
        /// for ANY provenance — the user picks the destination, so no logical path is needed.
        /// </summary>
        public static async Task<SaveResult> SaveToPath(byte[] serializedClef, string path)
        {
            if (serializedClef == null) throw new ArgumentNullException("serializedClef");
            if (string.IsNullOrEmpty(path))
            {
                return SaveResult.Failure("No destination path was provided.");
            }
            try
            {
                await Task.Run(() => WriteAtomic(path, serializedClef)).ConfigureAwait(false);
                return SaveResult.Success(path);
            }
            catch (Exception ex)
            {
                return SaveResult.Failure("Save failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Overwrites the original loose file in place when <paramref name="source"/> is
        /// <see cref="OpenSource.LooseFile"/>. Returns failure on any other source (a TRE is never edited
        /// in place — D-09).
        /// </summary>
        public static async Task<SaveResult> SaveInPlace(byte[] serializedClef, OpenSource source)
        {
            if (serializedClef == null) throw new ArgumentNullException("serializedClef");
            if (source == null) throw new ArgumentNullException("source");
            var loose = source as OpenSource.LooseFile;
            if (loose == null)
            {
                return SaveResult.Failure(
                    "In-place save needs a loose-file source — use Save as loose override or Save As.");
            }
            return await SaveToPath(serializedClef, loose.Path).ConfigureAwait(false);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core write — Flush(true) BEFORE returning (the stale-bytes reload-race barrier, mirrors
        // IffSaveTargets/TerrainSaveTargets WriteAtomic). The bytes are already serialized by the caller.
        // ─────────────────────────────────────────────────────────────────────

        private static void WriteAtomic(string path, byte[] bytes)
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
