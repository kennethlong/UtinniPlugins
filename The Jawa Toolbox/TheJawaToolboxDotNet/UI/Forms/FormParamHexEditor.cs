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
// Per-call modal hex/text leaf sub-editor for complex object-template params (Phase 11, D-02
// fallback). Wraps the same Consolas 9pt hex-editing surface the Phase 8 IFF Editor uses for raw
// leaf payloads: the user edits the verbatim value-region bytes as hex; on OK the parsed bytes are
// returned to the host, which routes them through the editor-local controller (dirty + undoable).
// Per-call modal — uses the default WinForms dispose-on-close (the hide-not-dispose pattern applies
// ONLY to plugin-registered GetForms() singletons). Object-template layout/inheritance semantics
// were studied from swg-client-v2 only (no code, comments, identifier names, or test fixtures copied
// from any reference source).

using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using UtinniCoreDotNet.UI.Forms;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Forms
{
    /// <summary>
    /// Thin modal host wrapping the Phase 8 IFF hex/text leaf-edit surface for a single complex
    /// object-template param (struct / weighted-list / dynamic-variable / ambiguous — anything the
    /// generic scalar decoder routed to <c>raw bytes (hex)</c>). Title is
    /// <c>Edit raw bytes — {field}</c>; the byte buffer is rendered as lowercase hex in a
    /// <c>Consolas 9pt</c> multiline text box and parsed back on OK (the same pairs-of-0-9/A-F parse
    /// the IFF Editor's <c>txtHex</c> uses). On OK the parsed bytes are exposed via
    /// <see cref="ResultBytes"/>; on Cancel <see cref="ResultBytes"/> stays null and the host makes
    /// no change.
    /// </summary>
    public sealed partial class FormParamHexEditor : UtinniForm
    {
        /// <summary>
        /// The parsed replacement value-region bytes when the user accepted with OK; null when the
        /// dialog was cancelled or closed without accepting.
        /// </summary>
        public byte[] ResultBytes { get; private set; }

        /// <summary>
        /// Constructs the modal seeded with the field name (title) and the current verbatim
        /// value-region bytes rendered as lowercase hex.
        /// </summary>
        public FormParamHexEditor(string fieldName, byte[] currentBytes)
        {
            InitializeComponent();

            // Theme via Colors.*() accessors only — no raw ARGB literals (Color.Red is the sole raw
            // literal, used for the parse-error status).
            this.pnlButtons.BackColor = Colors.Primary();
            this.pnlStatus.BackColor = Colors.Primary();
            this.lblStatus.ForeColor = Colors.Font();
            this.txtHex.BackColor = Colors.PrimaryHighlight();
            this.txtHex.ForeColor = Colors.Font();

            this.Text = "Edit raw bytes — " + (string.IsNullOrEmpty(fieldName) ? "param" : fieldName);
            this.txtHex.Text = BytesToHex(currentBytes);

            this.btnOk.Click += OnOkClicked;
            this.btnCancel.Click += OnCancelClicked;
        }

        private void OnOkClicked(object sender, EventArgs e)
        {
            byte[] parsed = TryParseHex(txtHex.Text);
            if (parsed == null)
            {
                lblStatus.Text = "Hex must be pairs of 0-9 / A-F. Remove the highlighted characters.";
                lblStatus.ForeColor = Color.Red;
                return;
            }
            ResultBytes = parsed;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            ResultBytes = null;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        // ── Hex format / parse (same pairs-of-0-9/A-F contract as the Phase 8 IFF leaf editor) ──

        private static string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("x2"));
                // Group bytes for readability; 16 bytes per line.
                if ((i + 1) % 16 == 0) sb.Append("\r\n");
                else sb.Append(' ');
            }
            return sb.ToString();
        }

        // Accepts any whitespace-separated hex (pairs of 0-9/A-F); discards whitespace. Returns null
        // when an odd nibble count or a non-hex character is present (the OK gate surfaces the error).
        private static byte[] TryParseHex(string text)
        {
            if (text == null) return new byte[0];
            var sb = new StringBuilder();
            foreach (char c in text)
            {
                if (IsHexChar(c)) sb.Append(c);
                else if (char.IsWhiteSpace(c)) continue;
                else return null; // a stray non-hex, non-whitespace character → invalid.
            }
            if (sb.Length % 2 != 0) return null;
            byte[] result = new byte[sb.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                int hi = HexNibble(sb[i * 2]);
                int lo = HexNibble(sb[i * 2 + 1]);
                if (hi < 0 || lo < 0) return null;
                result[i] = (byte)((hi << 4) | lo);
            }
            return result;
        }

        private static bool IsHexChar(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
        }

        private static int HexNibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return -1;
        }
    }
}
