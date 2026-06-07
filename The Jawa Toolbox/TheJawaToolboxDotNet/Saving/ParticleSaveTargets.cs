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
// Thin composition shim for the particle (.prt / FORM PEFT) Save▾ targets (15-06, CF-03). Each method
// forwards the typed particle model's captured MutableIffDocument VERBATIM to the Phase 8 dispatchers —
// no new save plumbing, no new path-defense, no new repack orchestration. The particle analog of
// ObjectTemplateSaveTargets / DatatableSaveTargets. Particle layout was studied from swg-client-v2 only
// (no code, comments, identifier names, or test fixtures copied from any reference source).
// Implementation original to Utinni under MIT.

using System;
using System.Threading.Tasks;
using UtinniCoreDotNet.Formats.Iff;
using UtinniCoreDotNet.Formats.Particle;

namespace TJT.Saving
{
    /// <summary>
    /// Thin composition shim over the Phase 8 save targets for the particle editor. Each method forwards
    /// the <see cref="MutableParticleEffect"/>'s captured byte-source
    /// <see cref="MutableParticleEffect.SourceIff"/> (mutated in place by the hybrid-DOM edit path)
    /// VERBATIM to the Phase 8 dispatchers — <see cref="IffSaveTargets"/> (modes 1/2: loose override,
    /// Save / Save-As) and <see cref="TreRepackSaveTarget"/> (mode 4: .tre repack). The V6000
    /// enumerate-only refusal, atomic File.Replace, repack lock, and timestamped backup all come free
    /// from the Phase 8 layer — this shim adds NONE of them.
    /// </summary>
    public static class ParticleSaveTargets
    {
        /// <summary>Saves the effect as a loose-override file under the resolved client root + sub-directory.</summary>
        public static Task<IffSaveTargets.SaveResult> SaveLooseOverride(
            MutableParticleEffect effect,
            OpenSource source,
            string resolvedRoot,
            string looseOverrideSubDir)
        {
            if (effect == null) throw new ArgumentNullException("effect");
            return IffSaveTargets.SaveLooseOverride(effect.SourceIff, source, resolvedRoot, looseOverrideSubDir);
        }

        /// <summary>Saves the effect to a user-chosen path (Save As…).</summary>
        public static Task<IffSaveTargets.SaveResult> SaveToPath(MutableParticleEffect effect, string targetPath)
        {
            if (effect == null) throw new ArgumentNullException("effect");
            return IffSaveTargets.SaveToPath(effect.SourceIff, targetPath);
        }

        /// <summary>Overwrites the original loose file when the source is <see cref="OpenSource.LooseFile"/>.</summary>
        public static Task<IffSaveTargets.SaveResult> SaveInPlace(MutableParticleEffect effect, OpenSource source)
        {
            if (effect == null) throw new ArgumentNullException("effect");
            return IffSaveTargets.SaveInPlace(effect.SourceIff, source);
        }

        /// <summary>Repacks the edited effect back into its source <c>.tre</c> archive (mode 4).</summary>
        public static Task<TreRepackSaveTarget.TreRepackResult> RepackIntoSourceTre(
            MutableParticleEffect effect,
            OpenSource.TreArchive ta,
            bool createBackup)
        {
            if (effect == null) throw new ArgumentNullException("effect");
            if (ta == null) throw new ArgumentNullException("ta");
            byte[] bytes = effect.Serialize();
            return TreRepackSaveTarget.Apply(ta, bytes, createBackup);
        }
    }
}
