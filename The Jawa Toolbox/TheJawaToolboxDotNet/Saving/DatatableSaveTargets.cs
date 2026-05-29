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
using System.Threading.Tasks;
using UtinniCoreDotNet.Formats.Datatable;
using UtinniCoreDotNet.Formats.Iff;

namespace TJT.Saving
{
    /// <summary>
    /// Thin composition shim for the Phase 9 datatable Save▾ targets (D-10). Each method builds the
    /// intermediate <see cref="MutableIffDocument"/> from a <see cref="MutableDataTableDocument"/> via
    /// Plan 09-01's <see cref="DataTableWriter"/>, then forwards VERBATIM to the Phase 8 dispatchers —
    /// no new save plumbing, no new path-defense, no new repack orchestration (09-PATTERNS § High-Leverage
    /// Reuse Points #1/#2/#3). The Phase 8 layers contribute, for free:
    /// <list type="bullet">
    ///   <item><see cref="IffSaveTargets"/> — Flush(true) MEDIUM-9 stale-bytes barrier, atomic write,
    ///         LooseOverridePath root-containment, error normalization (modes 1/2/3).</item>
    ///   <item><see cref="TreRepackSaveTarget"/> — temp-write + File.Replace atomic swap, TreRepackLock
    ///         probe, V6000 enumerate-only refusal (WR-06), TreBackupPath timestamped backup (mode 4).</item>
    /// </list>
    ///
    /// <para><b>D-09 writer-layer defense:</b> no <c>DataGridView</c> reference here — the row iteration
    /// lives inside <c>MutableDataTableDocument.BuildMutableIff</c> over the in-memory model, never a UI
    /// binding view (the grep gate stays 0 per Plan 09-06).</para>
    /// </summary>
    public static class DatatableSaveTargets
    {
        /// <summary>
        /// Saves the datatable as a loose-override file under the resolved client root + sub-directory.
        /// <remarks>Composes on <c>IffSaveTargets.SaveLooseOverride</c> — the LooseOverridePath.Resolve
        /// root-containment + Flush(true) barrier come for free; Phase 9 only adds the typed-build.</remarks>
        /// </summary>
        public static Task<IffSaveTargets.SaveResult> SaveLooseOverride(
            MutableDataTableDocument dt,
            OpenSource source,
            string resolvedRoot,
            string looseOverrideSubDir)
        {
            if (dt == null) throw new ArgumentNullException("dt");
            return IffSaveTargets.SaveLooseOverride(BuildMutableIff(dt), source, resolvedRoot, looseOverrideSubDir);
        }

        /// <summary>
        /// Saves the datatable to a user-chosen path.
        /// <remarks>Composes on <c>IffSaveTargets.SaveToPath</c> — Phase 9 only adds the typed-build.</remarks>
        /// </summary>
        public static Task<IffSaveTargets.SaveResult> SaveToPath(MutableDataTableDocument dt, string targetPath)
        {
            if (dt == null) throw new ArgumentNullException("dt");
            return IffSaveTargets.SaveToPath(BuildMutableIff(dt), targetPath);
        }

        /// <summary>
        /// Overwrites the original loose <c>.tab</c> when the source is <see cref="OpenSource.LooseFile"/>.
        /// <remarks>Composes on <c>IffSaveTargets.SaveInPlace</c> — the W-3 provenance contract (reject
        /// non-LooseFile) is enforced by the Phase 8 dispatcher; Phase 9 only adds the typed-build.</remarks>
        /// </summary>
        public static Task<IffSaveTargets.SaveResult> SaveInPlace(MutableDataTableDocument dt, OpenSource source)
        {
            if (dt == null) throw new ArgumentNullException("dt");
            return IffSaveTargets.SaveInPlace(BuildMutableIff(dt), source);
        }

        /// <summary>
        /// Repacks the edited datatable back into its source <c>.tre</c> archive.
        /// <remarks>Composes on <c>TreRepackSaveTarget.Apply</c> with <c>DataTableWriter.Serialize()</c>
        /// raw bytes — atomic File.Replace, locked-archive refusal, V6000 enumerate-only refusal (WR-06),
        /// and the timestamped backup all come for free; Phase 9 only serializes the typed model.</remarks>
        /// </summary>
        public static Task<TreRepackSaveTarget.TreRepackResult> RepackIntoSourceTre(
            MutableDataTableDocument dt,
            OpenSource.TreArchive ta,
            bool createBackup)
        {
            if (dt == null) throw new ArgumentNullException("dt");
            if (ta == null) throw new ArgumentNullException("ta");
            byte[] dtiiBytes = new DataTableWriter(dt).Serialize();
            return TreRepackSaveTarget.Apply(ta, dtiiBytes, createBackup);
        }

        // Builds the intermediate FORM DTII tree the Phase 8 IffSaveTargets dispatchers consume.
        private static MutableIffDocument BuildMutableIff(MutableDataTableDocument dt)
        {
            return new DataTableWriter(dt).BuildMutableIff();
        }
    }
}
