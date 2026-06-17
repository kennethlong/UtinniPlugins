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

namespace TJT.UI.Forms
{
    // Hand-authored partial: FormClientEffectEditor builds its control tree imperatively in the .cs
    // BuildContent() (inside the MEF-safe BuildContentSafe guard) so the Dock.Fill split + Top/Bottom strips
    // claim their edges in the exact Pitfall-8 order and the SplitContainer sets Size BEFORE
    // SplitterDistance. This file carries ONLY the IContainer/Dispose plumbing the WinForms partial-class
    // contract requires plus a no-op InitializeComponent (the ctor calls it before the guarded build).
    partial class FormClientEffectEditor
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                {
                    components.Dispose();
                }
                if (toolTip != null)
                {
                    toolTip.Dispose();
                }
                if (saveMenu != null)
                {
                    saveMenu.Dispose();
                }
                if (addMenu != null)
                {
                    addMenu.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.SuspendLayout();
            //
            // FormClientEffectEditor
            //
            this.Name = "FormClientEffectEditor";
            this.ResumeLayout(false);
        }
    }
}
