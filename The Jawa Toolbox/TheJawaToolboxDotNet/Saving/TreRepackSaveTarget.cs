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
using System.Threading.Tasks;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.Formats.Tre;
using UtinniCoreDotNet.Saving;

namespace TJT.Saving
{
    /// <summary>
    /// Full-archive <c>.tre</c> repack save target — D-05.4 implementation (Phase 8 Plan 07
    /// Task 3). Wraps <see cref="TreWriter.Repack"/> in a temp-write + <see cref="File.Replace"/>
    /// atomic-replace bracket, with an optional TIMESTAMPED backup of the original
    /// (08-REVIEWS MEDIUM-10 — never overwrite a prior backup) and an honest locked-archive
    /// fallback (08-REVIEWS MEDIUM-10 — refuse with the loose-override recommendation when the
    /// live client holds the archive open).
    ///
    /// <para><b>Highest-risk save mode in Phase 8:</b> a corrupt repack would break the
    /// client's resolution for every entry the archive carries (not just the one being
    /// edited). The implementation therefore:</para>
    /// <list type="bullet">
    ///   <item>writes to a temp file on the SAME volume first</item>
    ///   <item>verifies the bytes parse back via <see cref="TreFile.Open(string)"/> BEFORE
    ///         replacing the live archive (round-trip sanity gate)</item>
    ///   <item>backs up the original with a TIMESTAMPED name when the caller opts in (default
    ///         on at the UI layer per UI-SPEC §Destructive Assumption #5)</item>
    ///   <item>uses <see cref="File.Replace"/> for the atomic swap so a crash mid-write leaves
    ///         either the old archive (if Replace hasn't run yet) or the new one (if Replace
    ///         completed) — never a half-written hybrid</item>
    ///   <item>on locked-archive failure, deletes the temp file and returns
    ///         <see cref="TreRepackResult.RefusedClientHoldsArchive_LooseOverrideRecommended"/>
    ///         WITHOUT touching the original — no partial-write</item>
    /// </list>
    ///
    /// <para><b>Provenance gating (08-REVIEWS HIGH-2):</b> the caller (FormIffEditor) must
    /// have already pattern-matched <c>Source is OpenSource.TreArchive ta</c> before invoking
    /// <see cref="Apply"/>; this save target trusts its <paramref name="target"/> input and
    /// does not re-check the provenance.</para>
    ///
    /// <para><b>Lifecycle:</b> the public API is async <see cref="Apply"/>; it offloads the
    /// CPU-bound rebuild + I/O to a background <see cref="Task"/> so the UI thread stays
    /// responsive. The temp-file + atomic-replace dance is performed entirely off-UI-thread.</para>
    /// </summary>
    public static class TreRepackSaveTarget
    {
        /// <summary>Outcome of an <see cref="Apply"/> attempt — the UI switches on this.</summary>
        public enum TreRepackResult
        {
            /// <summary>Repack succeeded; original was replaced atomically with NO backup created.</summary>
            Replaced,

            /// <summary>Repack succeeded; original was replaced atomically AFTER a timestamped backup
            /// was created (08-REVIEWS MEDIUM-10 — never overwrites a prior backup name).</summary>
            BackedUpThenReplaced,

            /// <summary>The client appears to hold the archive open (sharing violation on temp-write
            /// OR on <see cref="File.Replace"/>). Temp file deleted; ORIGINAL UNCHANGED. The UI
            /// should pre-select the loose-override save mode per 08-REVIEWS MEDIUM-10.</summary>
            RefusedClientHoldsArchive_LooseOverrideRecommended,

            /// <summary>Repack failed for any other reason (parse-back round-trip refused, I/O
            /// failure, backup creation failure, etc.). Temp file deleted; ORIGINAL UNCHANGED.</summary>
            Failed,
        }

        /// <summary>
        /// Repacks <paramref name="target"/>'s <c>.tre</c> archive by full rebuild, replacing
        /// the live archive atomically and (optionally) timestamp-backing-up first.
        /// </summary>
        /// <param name="target">Source provenance — the <see cref="OpenSource.TreArchive"/>
        /// constructed by 08-05's TRE Browser hand-off. Carries the archive path, the record
        /// index, and the logical path of the edited entry.</param>
        /// <param name="rewrittenIffBytes">Freshly-serialized IFF bytes from
        /// <c>IffWriter.Write(mutableDoc)</c> — caller responsibility.</param>
        /// <param name="createBackup">When true, a timestamped backup of the original archive
        /// is created before replacement (UI-SPEC §Destructive Assumption #5: defaulted on).
        /// When false, no backup is created. The naming pattern is
        /// <c>&lt;name&gt;.tre.&lt;yyyyMMdd-HHmmss&gt;[-N].bak</c>; on the extremely unlikely
        /// sub-second name collision, a <c>-N</c> disambiguator is appended (never overwrites
        /// a prior backup).</param>
        public static async Task<TreRepackResult> Apply(
            OpenSource.TreArchive target,
            byte[] rewrittenIffBytes,
            bool createBackup)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (rewrittenIffBytes == null) throw new ArgumentNullException("rewrittenIffBytes");

            return await Task.Run(() => ApplyCore(target, rewrittenIffBytes, createBackup)).ConfigureAwait(false);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Background-thread core: open + repack + (backup) + temp-write + replace
        // ─────────────────────────────────────────────────────────────────────

        private static TreRepackResult ApplyCore(
            OpenSource.TreArchive target,
            byte[] rewrittenIffBytes,
            bool createBackup)
        {
            string trePath = target.TrePath;
            int recordIndex = target.RecordIndex;
            string tempPath = null;

            try
            {
                // ── 1. Open + repack ──────────────────────────────────────────
                // TreFile.Open(string) reads the header + TOC + names eagerly then closes the
                // FileStream (lazy payload reads re-open by path on demand inside
                // GetRecordCompressedBytes / GetRecordNameBytes / GetRecordData). The instance
                // itself holds no IDisposable resources so no `using` is needed.
                TreFile original = TreFile.Open(trePath);
                var edits = new Dictionary<int, byte[]> { { recordIndex, rewrittenIffBytes } };
                byte[] repacked = TreWriter.Repack(original, edits);

                // ── 2. Temp-write on the SAME volume as the target archive ───
                // File.Replace requires the source + destination on the same volume; placing
                // the temp file as a sibling of the target guarantees this.
                tempPath = trePath + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        fs.Write(repacked, 0, repacked.Length);
                        fs.Flush(true);
                    }
                }
                catch (IOException ioex) when (IsSharingViolation(ioex))
                {
                    // Defensive — extremely unlikely the temp path itself is held open since
                    // we just generated a GUID for it, but treat any sharing violation here
                    // as the canonical "client holds archive open" failure mode.
                    SafeDelete(tempPath);
                    return TreRepackResult.RefusedClientHoldsArchive_LooseOverrideRecommended;
                }

                // ── 3. Round-trip sanity gate: parse the temp file back via TreFile so we
                //      never let a corrupt repack replace the live archive. This is the
                //      cheapest defense against a regression in TreWriter that survives
                //      unit tests but produces bytes the client cannot resolve.
                try
                {
                    // Use the open as the validation — Open enforces magic + header + TOC
                    // structural invariants. Touch the records to force the TOC parse to
                    // complete (Open already does this via Parse, but be explicit). TreFile
                    // is non-IDisposable; the underlying FileStream is closed by Open()
                    // before this returns.
                    TreFile roundTrip = TreFile.Open(tempPath);
                    if (roundTrip.Records == null)
                    {
                        SafeDelete(tempPath);
                        return TreRepackResult.Failed;
                    }
                }
                catch
                {
                    SafeDelete(tempPath);
                    return TreRepackResult.Failed;
                }

                // ── 4. Optional timestamped backup (08-REVIEWS MEDIUM-10) ────
                // The timestamped-backup-path helper is framework-side
                // (UtinniCoreDotNet.Saving.TreBackupPath) so CI's Utinni-only checkout
                // can unit-test the naming-and-disambiguation contract without a
                // UtinniPlugins checkout. The helper takes an explicit DateTime so
                // production code passes UtcNow while tests pin a known instant.
                string backupPath = null;
                if (createBackup)
                {
                    backupPath = TreBackupPath.NextAvailable(trePath, DateTime.UtcNow);
                    try
                    {
                        File.Copy(trePath, backupPath, overwrite: false);
                    }
                    catch (IOException ioex) when (IsSharingViolation(ioex))
                    {
                        // The client may hold the archive for shared-read; File.Copy reads
                        // the source, so a sharing violation here is the same locked-archive
                        // failure mode. Honest fallback rather than partial-write.
                        SafeDelete(tempPath);
                        return TreRepackResult.RefusedClientHoldsArchive_LooseOverrideRecommended;
                    }
                    catch
                    {
                        // Any other backup failure: refuse to proceed (the user opted into
                        // a backup; silently skipping it would violate the contract).
                        SafeDelete(tempPath);
                        return TreRepackResult.Failed;
                    }
                }

                // ── 5. Atomic replace ────────────────────────────────────────
                try
                {
                    File.Replace(tempPath, trePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch (IOException ioex) when (IsSharingViolation(ioex))
                {
                    // Most likely cause: client holds the archive open for read+share or
                    // exclusive. Do NOT partial-write; delete the temp + (if we made one) the
                    // backup, return the honest "client holds archive" outcome so the UI can
                    // pre-select the loose-override save mode (08-REVIEWS MEDIUM-10).
                    SafeDelete(tempPath);
                    if (backupPath != null) SafeDelete(backupPath);
                    return TreRepackResult.RefusedClientHoldsArchive_LooseOverrideRecommended;
                }

                return createBackup
                    ? TreRepackResult.BackedUpThenReplaced
                    : TreRepackResult.Replaced;
            }
            catch
            {
                SafeDelete(tempPath);
                return TreRepackResult.Failed;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Delegates to the framework-side <see cref="TreRepackLock.IsSharingViolation"/>
        /// so both call sites use the same canonical HResult-based detector. The save
        /// target keeps a 1-line shim only because the BCL <see cref="IOException"/>
        /// catch-when filter cannot directly call a method from another namespace
        /// without an awkward static-import (the framework method is already public
        /// static so this shim keeps the catch-when clause readable).
        /// </summary>
        private static bool IsSharingViolation(IOException ex)
        {
            return TreRepackLock.IsSharingViolation(ex);
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup; surfacing this would mask the original failure.
            }
        }
    }
}
