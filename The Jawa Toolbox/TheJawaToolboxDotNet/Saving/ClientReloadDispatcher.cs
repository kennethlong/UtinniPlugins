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

using System.IO;
using UtinniCore.Utinni;
using UtinniCoreDotNet.Callbacks;
using UtinniCoreDotNet.Saving;

// ReSharper disable InconsistentNaming — public surface mirrors plan vocabulary

namespace TJT.Saving
{
    /// <summary>
    /// Tiered D-06 forced-reload dispatcher (08-REVIEWS HIGH-4, round-2 MEDIUM 7 + 9).
    /// Classifies the just-saved asset by extension (+ optional root IFF TypeId for the
    /// .iff carrier), routes via the framework-side <see cref="ReloadAssetClassifier"/>,
    /// and dispatches the matching binding onto the SWG game thread via
    /// <see cref="GameCallbacks.AddMainLoopCall"/>.
    ///
    /// <para><b>Tiered acceptance (PROD-W1-IFF Criterion 2 / D-06):</b>
    /// <list type="bullet">
    ///   <item>textures → <c>Graphics.ReloadTextures()</c> → in-session pass</item>
    ///   <item>terrain  → <c>GroundScene.Get().ReloadTerrain()</c> → in-session pass.
    ///         <b>INSTANCE</b> ThisCall (round-2 MEDIUM 7) — the bare static form is
    ///         FORBIDDEN and grep-gated.</item>
    ///   <item>datatable / STF / object-template / unknown .iff → <see cref="ReloadTier.PendingNextSceneChange"/>
    ///         (no binding call; the editor surfaces the candid "Reloads on next scene change"
    ///         copy and the user triggers a scene change via the documented TJT chat-command
    ///         parser callback path).</item>
    ///   <item>!<see cref="Game.IsRunning"/> → <see cref="ReloadTier.Unavailable"/>.</item>
    /// </list>
    /// We deliberately do NOT call <c>Game.AddSetSceneCallback</c> from here — that is a
    /// notification hook, NOT a trigger (08-REVIEWS HIGH-4 / Codex HIGH). Fabricating a
    /// scene-change trigger would risk reentrancy + the documented "naked after scene
    /// change" baseline becomes a perceived regression.</para>
    ///
    /// <para><b>Game thread (T-08-12):</b> every reload binding call is wrapped in
    /// <see cref="GameCallbacks.AddMainLoopCall(System.Action)"/>. NEVER calls a binding
    /// directly from the UI thread.</para>
    ///
    /// <para><b>Post-scene-change baseline:</b> per the project memory
    /// <c>project_tjt_scene_change_naked_baseline</c>, landing "naked" after a TJT-driven
    /// scene change is the documented baseline for Utinni-injected SWGEmu and is NOT a
    /// reload-failure signal.</para>
    /// </summary>
    public static class ClientReloadDispatcher
    {
        /// <summary>
        /// Classifies + dispatches the forced reload for the asset at <paramref name="savedPath"/>.
        /// Returns the tier so the caller's UI can reflect the outcome verbatim.
        ///
        /// <para>The dispatcher gates on <see cref="Game.IsRunning"/> FIRST — without a live
        /// client it returns <see cref="ReloadTier.Unavailable"/> without queuing anything.</para>
        /// </summary>
        /// <param name="savedPath">The absolute file path that was just written (used for
        /// extension sniff; the bytes themselves are NOT re-read).</param>
        /// <param name="rootTypeIdOrNull">For .iff carriers, the root chunk's 4-character
        /// TypeId (DTII / SHOT / STOT / SBOT etc.); null otherwise.</param>
        public static ReloadTier Dispatch(string savedPath, string rootTypeIdOrNull)
        {
            // 08-REVIEW WR-05: Game.IsRunning is a P/Invoke read that can throw outside an injected
            // client (no native binding bound, EngineGlobals not initialized, etc.). Every other
            // call site in TJT wraps this in try/catch (FormIffEditor lines ~896/1450,
            // LivePatchSaveTarget line ~136); mirror that defensive pattern here so an unhandled
            // exception cannot tear down the caller (e.g. FormIffEditor.OnReloadClicked).
            bool clientUp;
            try { clientUp = Game.IsRunning; }
            catch { clientUp = false; }
            if (!clientUp)
            {
                return ReloadTier.Unavailable;
            }

            string ext = string.IsNullOrEmpty(savedPath) ? null : Path.GetExtension(savedPath);
            ReloadTier tier = ReloadAssetClassifier.Classify(ext, rootTypeIdOrNull);

            switch (tier)
            {
                case ReloadTier.ReloadedTextures:
                    GameCallbacks.AddMainLoopCall(() =>
                    {
                        Graphics.ReloadTextures();
                    });
                    return ReloadTier.ReloadedTextures;

                case ReloadTier.ReloadedTerrain:
                    GameCallbacks.AddMainLoopCall(() =>
                    {
                        // Round-2 MEDIUM 7 (cursor N-M2): INSTANCE ThisCall. The bare static
                        // form is bound nowhere and would no-op silently if used. Verified
                        // call-site analog: SWG/GroundSceneImpl.cs Reload().
                        GroundScene.Get().ReloadTerrain();
                    });
                    return ReloadTier.ReloadedTerrain;

                case ReloadTier.PendingNextSceneChange:
                    // No binding to dispatch — the running scene caches the asset; the user
                    // triggers a TJT-driven scene change to refresh it. We do NOT fabricate a
                    // scene-change trigger here (08-REVIEWS HIGH-4: AddSetSceneCallback is a
                    // notification hook, NOT a trigger).
                    return ReloadTier.PendingNextSceneChange;

                case ReloadTier.Unavailable:
                default:
                    return ReloadTier.Unavailable;
            }
        }
    }
}
