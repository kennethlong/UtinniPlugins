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

using System.Windows.Forms;
using UtinniCoreDotNet.UI.Forms;

namespace TJT.UI.Forms
{
    /// <summary>
    /// Small modal dialog for 4-character FourCC tag entry — used by the structural-op flows
    /// (Add chunk / Add FORM sub-type / Rename / retag) in <see cref="FormIffEditor"/>.
    ///
    /// <para>Mirrors the small-dialog pattern of <c>UtinniCoreDotNet.UI.Forms.FormHotkeyEditorDialog</c>:
    /// a <c>UtinniForm</c> shell with a <c>UtinniTextbox</c> (MaxLength = 4) + explicit-verb OK /
    /// Cancel buttons. The <see cref="Value"/> property returns the trimmed 4-char result; the
    /// caller is responsible for FourCC validity (e.g. trailing-space for "CAT ") via the
    /// <c>UI-SPEC</c> copy <c>A chunk tag must be exactly 4 characters…</c>.</para>
    /// </summary>
    public partial class FormFourCcDialog : UtinniForm
    {
        /// <summary>The 4-char value the user typed (last typed value; not validated for length here).</summary>
        public string Value
        {
            get { return txtTag.Text; }
            set { txtTag.Text = value ?? ""; }
        }

        public FormFourCcDialog(string title, string initialValue)
        {
            InitializeComponent();
            base.Text = title ?? "Chunk tag";
            txtTag.MaxLength = 4;
            txtTag.Text = initialValue ?? "";
        }
    }
}
