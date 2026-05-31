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
// Thin composition shim for the object-template Save▾ targets (Phase 11, CF-03). Each method forwards
// the typed object-template model's captured MutableIffDocument (or its byte-exact Serialize()) VERBATIM
// to the Phase 8 dispatchers — no new save plumbing, no new path-defense, no new repack orchestration.
// The object-template analog of DatatableSaveTargets. Object-template layout/inheritance semantics were
// studied from swg-client-v2 only (no code, comments, identifier names, or test fixtures copied from any
// reference source). Implementation original to Utinni under MIT.

using System;
using System.Threading.Tasks;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.Formats.ObjectTemplate;

namespace TJT.Saving
{
    /// <summary>
    /// Thin composition shim over the Phase 8 save targets for the object-template editor. Each method
    /// builds the intermediate <see cref="MutableIffDocument"/> from a <see cref="MutableObjectTemplate"/>
    /// (its captured byte-source <see cref="MutableObjectTemplate.SourceIff"/>, mutated in place by the
    /// hybrid-DOM edit path) then forwards VERBATIM to the Phase 8 dispatchers —
    /// <see cref="IffSaveTargets"/> (modes 1/2: loose override, Save / Save-As) and
    /// <see cref="TreRepackSaveTarget"/> (mode 4: .tre repack). The V6000 enumerate-only refusal (WR-06),
    /// atomic File.Replace, repack lock, and timestamped backup all come free from the Phase 8 layer —
    /// this shim adds NONE of them.
    /// </summary>
    public static class ObjectTemplateSaveTargets
    {
        /// <summary>Saves the object template as a loose-override file under the resolved client root + sub-directory.</summary>
        public static Task<IffSaveTargets.SaveResult> SaveLooseOverride(
            MutableObjectTemplate ot,
            OpenSource source,
            string resolvedRoot,
            string looseOverrideSubDir)
        {
            if (ot == null) throw new ArgumentNullException("ot");
            return IffSaveTargets.SaveLooseOverride(BuildMutableIff(ot), source, resolvedRoot, looseOverrideSubDir);
        }

        /// <summary>Saves the object template to a user-chosen path (Save As…).</summary>
        public static Task<IffSaveTargets.SaveResult> SaveToPath(MutableObjectTemplate ot, string targetPath)
        {
            if (ot == null) throw new ArgumentNullException("ot");
            return IffSaveTargets.SaveToPath(BuildMutableIff(ot), targetPath);
        }

        /// <summary>Overwrites the original loose file when the source is <see cref="OpenSource.LooseFile"/>.</summary>
        public static Task<IffSaveTargets.SaveResult> SaveInPlace(MutableObjectTemplate ot, OpenSource source)
        {
            if (ot == null) throw new ArgumentNullException("ot");
            return IffSaveTargets.SaveInPlace(BuildMutableIff(ot), source);
        }

        /// <summary>Repacks the edited object template back into its source <c>.tre</c> archive (mode 4).</summary>
        public static Task<TreRepackSaveTarget.TreRepackResult> RepackIntoSourceTre(
            MutableObjectTemplate ot,
            OpenSource.TreArchive ta,
            bool createBackup)
        {
            if (ot == null) throw new ArgumentNullException("ot");
            if (ta == null) throw new ArgumentNullException("ta");
            byte[] otBytes = ObjectTemplateWriter.Serialize(ot);
            return TreRepackSaveTarget.Apply(ta, otBytes, createBackup);
        }

        // The Phase 8 IffSaveTargets dispatchers consume a MutableIffDocument; the OT model retains its
        // mutated byte-source SourceIff (hybrid-DOM — untouched params re-emit verbatim), so we forward it.
        private static MutableIffDocument BuildMutableIff(MutableObjectTemplate ot)
        {
            return ot.SourceIff;
        }
    }
}
