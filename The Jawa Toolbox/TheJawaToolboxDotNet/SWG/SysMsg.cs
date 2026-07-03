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

using UtinniCore.Utinni;
using UtinniCoreDotNet.Callbacks;

namespace TJT.SWG
{
    /// <summary>
    /// In-client system-message injection (Phase 24 / v14 sysmsg SEND row). Routes through the
    /// advertised <c>systemMessageManager::sendMessage</c> endpoint — on the advertised NGE client
    /// the resolver re-points it at <c>CuiSystemMessageManager::sendFakeSystemMessage</c>; on SWGEmu
    /// it calls the existing literal. The native wrapper carries a version-skew guard (an advertised
    /// exe older than v14 drops the call + logs instead of crashing), so callers can fire-and-forget.
    ///
    /// <para><b>Game thread (T-08-12):</b> the engine send posts into the CUI — every call is
    /// marshaled via <see cref="GameCallbacks.AddMainLoopCall(System.Action)"/>; never call the
    /// binding directly from the UI thread.</para>
    /// </summary>
    public static class SysMsg
    {
        /// <summary>
        /// Quiet editor-action confirmation ("[Utinni] terrain reloaded"): chat box only
        /// (chatBoxOnly=true — no system-message popup treatment).
        /// </summary>
        public static void Notify(string message)
        {
            Send("[Utinni] " + message, true);
        }

        /// <summary>
        /// Full system-message broadcast (chatBoxOnly=false): the raw text, with the engine's
        /// complete system-message treatment. The manual MiscPanel broadcast box uses this.
        /// </summary>
        public static void Broadcast(string message)
        {
            Send(message, false);
        }

        private static void Send(string message, bool chatBoxOnly)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            // Game.IsRunning is a P/Invoke read that can throw outside an injected client
            // (the ClientReloadDispatcher WR-05 pattern) — degrade to a silent no-op.
            bool clientUp;
            try { clientUp = Game.IsRunning; }
            catch { clientUp = false; }
            if (!clientUp)
            {
                return;
            }

            GameCallbacks.AddMainLoopCall(() =>
            {
                SystemMessageManager.SendMessage(message, chatBoxOnly);
            });
        }
    }
}
