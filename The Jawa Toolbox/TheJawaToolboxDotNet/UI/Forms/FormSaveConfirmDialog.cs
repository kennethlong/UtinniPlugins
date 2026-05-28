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

using System;
using System.Drawing;
using System.Windows.Forms;
using UtinniCoreDotNet.UI.Forms;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Forms
{
    /// <summary>
    /// Risk-proportional confirm modal for destructive save modes (Phase 8 Plan 06 Task 1).
    /// Used by 08-06 for the in-memory live-patch confirm; designed for reuse by 08-07's
    /// repack confirm (an optional "back up first" checkbox is exposed via
    /// <see cref="ShowBackupCheckbox"/> / <see cref="BackupRequested"/>).
    ///
    /// <para><b>Lifecycle (not a singleton):</b> instantiated per call site via
    /// <c>using (var dlg = new FormSaveConfirmDialog(...))</c> and disposed by the
    /// <c>using</c> block. Unlike <c>FormIffEditor</c> / <c>FormTreBrowser</c>, this dialog
    /// is NOT registered with the plugin host's <c>GetForms()</c> list — it has no Plugin.cs
    /// registration, no singleton reference held across calls, and no <c>FindOrCreate</c>
    /// path. Default WinForms dispose-on-close is therefore correct; the 08-05
    /// hide-not-dispose intercept pattern does NOT apply. (See 08-05-SUMMARY "Singleton-form
    /// pattern" for the contrast.)</para>
    ///
    /// <para><b>UI-SPEC contract:</b> per 08-UI-SPEC §Destructive, the body emphasis is
    /// <see cref="Color.Red"/> (the documented destructive exception); both buttons carry
    /// explicit verbs — never bare "OK" / "Cancel" — supplied by the caller.</para>
    ///
    /// <para><b>Result reporting:</b> after <see cref="ShowDialog()"/> returns,
    /// <see cref="Outcome"/> indicates whether the user accepted the destructive verb or
    /// cancelled. <see cref="BackupRequested"/> exposes the (optional) backup-checkbox state
    /// when <see cref="ShowBackupCheckbox"/> was set true at construction.</para>
    /// </summary>
    public partial class FormSaveConfirmDialog : UtinniForm
    {
        /// <summary>Outcome of a confirm prompt — the caller switches on this after ShowDialog returns.</summary>
        public enum ConfirmOutcome
        {
            /// <summary>User cancelled (clicked Cancel, closed the dialog, or pressed Esc).</summary>
            Cancelled,
            /// <summary>User accepted the destructive verb (clicked the verb button).</summary>
            Accepted,
        }

        /// <summary>The user's choice after the dialog closes.</summary>
        public ConfirmOutcome Outcome { get; private set; }

        /// <summary>
        /// True iff the optional backup checkbox was shown AND was checked when the user
        /// accepted. Always false when <see cref="ShowBackupCheckbox"/> is false (08-06's
        /// live-patch path passes false; 08-07's repack path passes true).
        /// </summary>
        public bool BackupRequested { get; private set; }

        /// <summary>
        /// True iff the optional backup checkbox should be shown (08-07's repack path).
        /// Defaults to false (08-06's live-patch path).
        /// </summary>
        public bool ShowBackupCheckbox { get; }

        /// <summary>
        /// Constructs the dialog with caller-supplied heading, body, and two explicit verb
        /// captions. The accept-verb caption is the proceed button (e.g. "Patch live",
        /// "Repack"); the cancel caption is the safe-out (typically "Cancel" — UI-SPEC
        /// allows bare "Cancel" for the safe-out, only the destructive verb must be explicit).
        /// </summary>
        /// <param name="heading">Heading text, e.g. "Patch the live client in memory?"</param>
        /// <param name="body">Body text; rendered in Color.Red per UI-SPEC §Destructive.</param>
        /// <param name="acceptVerb">Caption for the destructive verb button, e.g. "Patch live".</param>
        /// <param name="cancelVerb">Caption for the cancel button, typically "Cancel".</param>
        /// <param name="showBackupCheckbox">When true, displays a "Back up the source first" checkbox
        /// (08-07 reuse path). When false (08-06 live-patch path), the checkbox is hidden.</param>
        /// <param name="backupCheckboxLabel">Optional override for the checkbox label when
        /// <paramref name="showBackupCheckbox"/> is true. Defaults to "Back up the source first".</param>
        public FormSaveConfirmDialog(
            string heading,
            string body,
            string acceptVerb,
            string cancelVerb,
            bool showBackupCheckbox = false,
            string backupCheckboxLabel = null)
        {
            InitializeComponent();

            if (string.IsNullOrEmpty(heading)) throw new ArgumentException("heading must be non-empty", "heading");
            if (string.IsNullOrEmpty(body)) throw new ArgumentException("body must be non-empty", "body");
            if (string.IsNullOrEmpty(acceptVerb)) throw new ArgumentException("acceptVerb must be non-empty", "acceptVerb");
            if (string.IsNullOrEmpty(cancelVerb)) throw new ArgumentException("cancelVerb must be non-empty", "cancelVerb");

            ShowBackupCheckbox = showBackupCheckbox;
            Outcome = ConfirmOutcome.Cancelled;
            BackupRequested = false;

            // Window title (the UtinniForm titlebar uses Text as the visible name) — the
            // heading line ALSO appears inside the body region so users see it after focus.
            base.Text = heading;
            lblHeading.Text = heading;
            lblHeading.ForeColor = Colors.Font();

            // Body emphasis in Color.Red — UI-SPEC §Color: the documented destructive
            // exception (and the only place we use a raw Color.Red literal). Pure Colors.*()
            // accessors carry everything else — UI-SPEC §Color.
            lblBody.Text = body;
            lblBody.ForeColor = Color.Red;

            // Explicit verb captions (no bare OK / Cancel on the accept button per UI-SPEC).
            btnAccept.Text = acceptVerb;
            btnCancel.Text = cancelVerb;

            // Backup checkbox (08-07 reuse). Hidden in 08-06's live-patch path.
            chkBackupFirst.Visible = showBackupCheckbox;
            chkBackupFirst.Checked = showBackupCheckbox; // default ON when shown (UI-SPEC §Destructive Assumption #5)
            if (showBackupCheckbox && !string.IsNullOrEmpty(backupCheckboxLabel))
            {
                chkBackupFirst.Text = backupCheckboxLabel;
            }

            // Themed surfaces via Colors.*() accessors only — no ARGB literals here.
            this.BackColor = Colors.Primary();
            pnlButtons.BackColor = Colors.Primary();
            chkBackupFirst.ForeColor = Colors.Font();
            chkBackupFirst.BackColor = Colors.Primary();

            btnAccept.Click += OnAcceptClicked;
            btnCancel.Click += OnCancelClicked;
        }

        private void OnAcceptClicked(object sender, EventArgs e)
        {
            Outcome = ConfirmOutcome.Accepted;
            BackupRequested = ShowBackupCheckbox && chkBackupFirst.Checked;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            Outcome = ConfirmOutcome.Cancelled;
            BackupRequested = false;
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}
