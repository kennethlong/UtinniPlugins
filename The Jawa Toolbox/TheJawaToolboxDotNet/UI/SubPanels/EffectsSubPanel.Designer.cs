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

namespace TJT.UI.SubPanels
{
    // Hand-authored partial: EffectsSubPanel builds its (thin, 417px-fitting) control tree imperatively in
    // the .cs BuildContent() so the docked Top banner/action-row + Bottom footer claim their edges in the
    // exact Pitfall-8 order. This file carries ONLY the IContainer/Dispose plumbing the WinForms
    // partial-class contract requires plus a no-op InitializeComponent (the SubPanel base ctor expects one).
    partial class EffectsSubPanel
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
                // The launched host is a child window we own (singleton, hide-not-dispose during the
                // session) — dispose it when the panel itself is torn down.
                if (effectForm != null && !effectForm.IsDisposed)
                {
                    effectForm.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        // The Designer-contract InitializeComponent. The real layout is built imperatively in BuildContent()
        // (see .cs) — this keeps the control tree out of the brittle designer-serialized region so the
        // Dock order is explicit and the MEF-safe BuildContentSafe() wraps the whole build.
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.SuspendLayout();
            //
            // EffectsSubPanel
            //
            this.Name = "EffectsSubPanel";
            this.Size = new System.Drawing.Size(417, 132);
            this.ResumeLayout(false);
        }
    }
}
