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

using System.Runtime.InteropServices;
using UtinniCore.Utinni;
using UtinniCoreDotNet.Callbacks;
using UtinniCoreDotNet.Editing;
using UtinniCoreDotNet.Formats.Iff;

// ReSharper disable InconsistentNaming — public surface mirrors plan vocabulary

namespace TJT.Saving
{
    /// <summary>
    /// In-memory live-patch save target — D-05.3 implementation half (Phase 8 Plan 06
    /// Task 3). Writes the freshly-rewritten IFF bytes straight into the live SWG
    /// client's mapped memory region via the CON-N-04 <c>Memory.memory.Copy</c>
    /// VirtualProtect bracket on the game thread.
    ///
    /// <para><b>Scope (08-REVIEWS HIGH-2 / round-2 MEDIUM 11):</b> this save target
    /// is INFRA-READY, USER-DISABLED in Phase 8. No current Phase-8 open path
    /// constructs <see cref="OpenSource.ClientMemory"/> — the
    /// <c>FormIffEditor.Save▾ ▸ Patch live client</c> menu item ships disabled with
    /// the honest tooltip "Live patch requires opening from client memory — not
    /// wired in this phase." A follow-up phase will wire a ClientMemory discovery
    /// path; at that point this target is the live-patch writer.</para>
    ///
    /// <para><b>Bounds gate (round-2 HIGH-B):</b> all four bounds-gate preconditions
    /// (live client, non-zero target, same-length rewrite) are validated by the
    /// framework-side pure-function <see cref="LivePatchValidator.Validate"/> BEFORE
    /// anything is queued. The validator is unit-tested with FIVE [Fact]s in
    /// <c>UtinniCoreDotNet.Tests/EditingTests/LivePatchValidatorTests.cs</c> — the
    /// CONTEXT D-05.3 "unit-tested for its bounds gate" claim is structurally
    /// satisfied (no inlined check that would slip past CI).</para>
    ///
    /// <para><b>CON-N-04 (T-08-14):</b> the actual write is the native
    /// <c>memory::copy</c> binding which contains its OWN VirtualProtect save / write /
    /// restore bracket — DO NOT hand-roll a write that skips it. The Apply lambda is
    /// queued via <see cref="GameCallbacks.AddMainLoopCall"/> so the write happens on
    /// the SWG game thread (never the UI thread).</para>
    ///
    /// <para><b>SAME-LENGTH-ONLY V1 (08-REVIEWS HIGH-3 / T-08-15):</b> the V1 gate
    /// refuses BOTH growth (would write off the end of the mapped region) AND shrink
    /// (would leave stale tail bytes the SWG reader would still interpret as part of
    /// the document). A follow-up phase MAY relax this by zero-filling the tail
    /// (08-REVIEWS HIGH-3 alternate); V1 ships the strict gate.</para>
    ///
    /// <para><b>Volatile by design (T-08-16):</b> the patch is lost on reload /
    /// scene change. The intended D-05.3 behavior — the candid UI copy surfaces
    /// this via the confirm dialog ("This writes your edits straight into the running
    /// client. The change is temporary (lost on reload) ...").</para>
    /// </summary>
    public static class LivePatchSaveTarget
    {
        /// <summary>
        /// Outcome of an <see cref="Apply"/> attempt — mirrors
        /// <see cref="LivePatchValidation"/> 1:1 plus the success state. The UI layer
        /// (08-04 + 08-06 Task 4) switches on this to produce the candid status copy.
        /// </summary>
        public enum LivePatchResult
        {
            /// <summary>Bytes were queued for the CON-N-04 write on the game thread.</summary>
            Applied,

            /// <summary><see cref="LivePatchValidation.RefusedNoClient"/> — no live client.</summary>
            RefusedNoClient,

            /// <summary><see cref="LivePatchValidation.RefusedZeroTarget"/> — target address is IntPtr.Zero.</summary>
            RefusedZeroTarget,

            /// <summary><see cref="LivePatchValidation.RefusedSameLength"/> — rewritten length differs from the originally mapped region.</summary>
            RefusedSameLength,
        }

        /// <summary>
        /// Validates the bounds gate via <see cref="LivePatchValidator.Validate"/> and,
        /// on <see cref="LivePatchValidation.Ok"/>, queues a game-thread write of
        /// <paramref name="rewritten"/> into the live client at
        /// <c>target.TargetAddr</c> via the CON-N-04 <c>Memory.memory.Copy</c> bracket.
        ///
        /// <para>The bytes are pinned via <see cref="GCHandle.Alloc"/> with
        /// <see cref="GCHandleType.Pinned"/> for the duration of the write and freed
        /// in a <c>finally</c> — the pin is needed because <c>memory::copy</c> takes
        /// raw addresses; without pinning the GC could relocate the array between
        /// <c>AddrOfPinnedObject</c> and the native call.</para>
        ///
        /// <para><b>Null-safety:</b> the bounds gate is fed
        /// <c>rewritten?.Length ?? 0</c> — a null rewritten (defensive — caller bug)
        /// is treated as length-0 which, for any non-zero
        /// <c>OriginalMappedLength</c>, refuses as
        /// <see cref="LivePatchResult.RefusedSameLength"/>. The pure validator never
        /// dereferences the array (it only knows the length); the dereference happens
        /// HERE in the Apply lambda, only AFTER the gate has cleared.</para>
        /// </summary>
        /// <param name="target">Live-client provenance descriptor (08-01 Task 3 type)
        /// carrying the mapped-region <see cref="OpenSource.ClientMemory.TargetAddr"/>
        /// and <see cref="OpenSource.ClientMemory.OriginalMappedLength"/>.</param>
        /// <param name="rewritten">Freshly-serialized IFF bytes from
        /// <c>IffWriter.Write(mutableDoc)</c> — caller responsibility.</param>
        public static LivePatchResult Apply(OpenSource.ClientMemory target, byte[] rewritten)
        {
            if (target == null)
            {
                // Defensive — caller bug. No mapped region to write to. The honest
                // outcome maps to RefusedZeroTarget (the validator's nearest equivalent
                // for "address is unusable"). This branch is unreachable when the UI
                // path is correctly gated on `Source is OpenSource.ClientMemory cm`.
                return LivePatchResult.RefusedZeroTarget;
            }

            // (1) Snapshot Game.IsRunning ONCE on the UI thread and pass it to the
            //     pure validator. The validator stays pure-managed (no UtinniCore.dll
            //     binding) and the IsRunning value is consistent for the whole gate.
            bool gameIsRunning;
            try { gameIsRunning = Game.IsRunning; }
            catch { gameIsRunning = false; /* binding unavailable outside an injected client */ }

            // (2) Pure-function bounds gate — round-2 HIGH-B fold. EXTRACTED from the
            //     save target so it can be unit-tested without a live client.
            LivePatchValidation v = LivePatchValidator.Validate(
                targetAddr: target.TargetAddr,
                originalMappedLength: target.OriginalMappedLength,
                rewrittenLength: rewritten == null ? 0 : rewritten.Length,
                gameIsRunning: gameIsRunning);

            switch (v)
            {
                case LivePatchValidation.RefusedNoClient:
                    return LivePatchResult.RefusedNoClient;
                case LivePatchValidation.RefusedZeroTarget:
                    return LivePatchResult.RefusedZeroTarget;
                case LivePatchValidation.RefusedSameLength:
                    return LivePatchResult.RefusedSameLength;
                case LivePatchValidation.Ok:
                    break; // proceed
                default:
                    // Forward-compat — if the validator grows a new disposition we
                    // refuse safely (no inadvertent write on an unrecognized result).
                    return LivePatchResult.RefusedSameLength;
            }

            // (3) Capture the validated inputs into the lambda's closure. We capture
            //     `target.TargetAddr` + `rewritten` directly — the lambda runs on the
            //     game thread (DequeueMainLoopCalls drains the queue inside the SWG
            //     main loop tick), not the UI thread.
            IntPtrTargetAddrAndLength addrLen;
            addrLen.TargetAddr = target.TargetAddr;
            addrLen.Length = rewritten.Length;
            byte[] payload = rewritten; // explicit local for closure clarity

            // (4) Queue the CON-N-04 write on the game thread. The pin / copy / unpin
            //     dance lives inside the lambda so the GCHandle is acquired and
            //     released on the same thread that performs the write — no
            //     cross-thread pinning issues.
            GameCallbacks.AddMainLoopCall(() =>
            {
                GCHandle pin = default(GCHandle);
                try
                {
                    pin = GCHandle.Alloc(payload, GCHandleType.Pinned);
                    System.IntPtr pinned = pin.AddrOfPinnedObject();
                    // CON-N-04 bracket: the native memory::copy implementation does its
                    // OWN VirtualProtect save / Copy / restore — DO NOT hand-roll a
                    // write that skips it.
                    UtinniCore.Memory.memory.Copy(
                        (uint)addrLen.TargetAddr.ToInt64(),
                        (uint)pinned.ToInt64(),
                        (uint)addrLen.Length);
                }
                finally
                {
                    if (pin.IsAllocated)
                    {
                        pin.Free();
                    }
                }
            });

            return LivePatchResult.Applied;
        }

        // Tiny struct to keep the two captured scalars adjacent in the lambda's
        // closure — avoids capturing the whole OpenSource.ClientMemory reference
        // for no reason.
        private struct IntPtrTargetAddrAndLength
        {
            public System.IntPtr TargetAddr;
            public int Length;
        }
    }
}
