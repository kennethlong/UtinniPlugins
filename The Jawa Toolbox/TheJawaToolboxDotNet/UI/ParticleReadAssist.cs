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
// In-app particle "Explain effect" read-assist (15-06, D-07/D-08). This is READ-ASSIST ONLY: it reuses
// the SAME .prt CLI/MCP read path the 15-04 plan shipped (the `decode-iff` verb that auto-dispatches a
// FORM PEFT root into the particle codec — the identical verb the `summarize_particle` MCP read tool
// dispatches). It NEVER writes .prt bytes, NEVER accepts a prompt-to-mutate, and contains ZERO format
// logic of its own (D-06) — it shells the verb and surfaces the verbatim JSON envelope. If the CLI is
// not present, it degrades to an honest error string rather than re-deriving a decode in-process.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace TJT.UI
{
    /// <summary>
    /// Result of an <see cref="ParticleReadAssist.ExplainAsync"/> call: whether the read succeeded and
    /// the human-readable text to fill the read-only results pane.
    /// </summary>
    public sealed class ParticleReadAssistResult
    {
        /// <summary>True when the read path returned a clean (exit-0) decode.</summary>
        public bool Ok { get; }

        /// <summary>The verbatim CLI stdout (success) or the honest error reason (failure).</summary>
        public string Text { get; }

        private ParticleReadAssistResult(bool ok, string text)
        {
            Ok = ok;
            Text = text ?? "";
        }

        /// <summary>Builds a success result carrying the verbatim decode envelope.</summary>
        public static ParticleReadAssistResult Success(string text) { return new ParticleReadAssistResult(true, text); }

        /// <summary>Builds a failure result carrying the reason (no decode performed in-process).</summary>
        public static ParticleReadAssistResult Failure(string reason) { return new ParticleReadAssistResult(false, reason); }
    }

    /// <summary>
    /// Locates <c>utinni-cli.exe</c> and dispatches the <c>decode-iff</c> read verb against a <c>.prt</c>
    /// path — the D-08 read path the in-app <c>Explain effect</c> button reuses (read-assist only). NO
    /// codec call lives here: the form's AI handler delegates to this, which shells the same verb the MCP
    /// <c>summarize_particle</c> tool calls. The button is read-only — there is no save/write path here.
    /// </summary>
    public static class ParticleReadAssist
    {
        private const string CliExeName = "utinni-cli.exe";

        /// <summary>
        /// Runs <c>utinni-cli decode-iff &lt;path&gt;</c> on a background task and returns the verbatim
        /// JSON envelope on success, or an honest "Couldn't read this effect — {reason}." string on
        /// failure. NEVER writes the file; NEVER mutates anything.
        /// </summary>
        public static async Task<ParticleReadAssistResult> ExplainAsync(string prtPath)
        {
            if (string.IsNullOrEmpty(prtPath) || !File.Exists(prtPath))
            {
                return ParticleReadAssistResult.Failure("Couldn't read this effect — save it to a file first, then try again.");
            }

            string cliPath = LocateCli();
            if (cliPath == null)
            {
                return ParticleReadAssistResult.Failure(
                    "Couldn't read this effect — utinni-cli.exe was not found next to the editor.");
            }

            try
            {
                return await Task.Run(() => RunDecodeIff(cliPath, prtPath)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return ParticleReadAssistResult.Failure("Couldn't read this effect — " + ex.Message);
            }
        }

        // Shells the read verb and maps the exit code 1:1 (0 = decode envelope; non-zero = honest error).
        private static ParticleReadAssistResult RunDecodeIff(string cliPath, string prtPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = cliPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(cliPath) ?? Environment.CurrentDirectory,
            };
            // .NET Framework 4.7.2 has no ArgumentList; build a quoted Arguments string so a path with
            // spaces is passed as a single argument to the verb.
            psi.Arguments = "decode-iff " + Quote(prtPath);

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            using (var proc = new Process { StartInfo = psi })
            {
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                // Bound the wait so a hung CLI can never wedge the editor (SOE tools hang-on-error gotcha).
                if (!proc.WaitForExit(15000))
                {
                    try { proc.Kill(); } catch { /* best-effort */ }
                    return ParticleReadAssistResult.Failure("Couldn't read this effect — the reader timed out.");
                }

                if (proc.ExitCode == 0)
                {
                    return ParticleReadAssistResult.Success(stdout.ToString().Trim());
                }
                string reason = stderr.Length > 0 ? stderr.ToString().Trim() : stdout.ToString().Trim();
                if (string.IsNullOrEmpty(reason)) reason = "exit code " + proc.ExitCode + ".";
                return ParticleReadAssistResult.Failure("Couldn't read this effect — " + reason);
            }
        }

        // Locates utinni-cli.exe: next to the executing assembly first, then next to the host process.
        private static string LocateCli()
        {
            try
            {
                string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string candidate = Path.Combine(asmDir ?? "", CliExeName);
                if (File.Exists(candidate)) return candidate;

                string procDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                candidate = Path.Combine(procDir ?? "", CliExeName);
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // Defensive: locating the CLI must never throw into the click handler.
            }
            return null;
        }

        private static string Quote(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }
    }
}
