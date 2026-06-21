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

// Phase 23 (23-07) — the "Assign field" modal (UI-SPEC Copywriting Contract): opened by the
// select->assign gesture (Interaction 1) on a byte selection in the Template builder pane. It captures a
// Name, a kernel Type, an optional repeat-spec (the 3 D-10 kinds), and an optional enum/flags map with
// the EXPLICIT enum-vs-flags radio (Pitfall 4 — a flags entry is a bit POSITION 1..32, not a mask), plus
// an encoding for the string kinds. On accept it produces a FieldRecord — the SAME engine type the
// headless KernelCodec consumes; the dialog owns NO format logic.
//
// Code-built (no Designer/.resx) on purpose: a small modal with no image resources avoids the MSB3823
// resx-image build path and keeps the whole dialog in one grep-able file.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using UtinniCoreDotNet.Formats.Template;
using UtinniCoreDotNet.UI.Controls;
using UtinniCoreDotNet.UI.Forms;
using UtinniCoreDotNet.UI.Theme;

namespace TJT.UI.Forms
{
    /// <summary>
    /// Small modal that builds a <see cref="FieldRecord"/> from user input. Mirrors the host's candid,
    /// specific voice and the small-dialog idiom (UtinniForm shell + UtinniTextbox/UtinniComboBox +
    /// explicit-verb OK/Cancel). The result is read from <see cref="Result"/> after a DialogResult.OK.
    /// </summary>
    public sealed class FormAssignFieldDialog : UtinniForm
    {
        private readonly UtinniTextbox txtName;
        private readonly UtinniComboBox cboType;
        private readonly UtinniComboBox cboEncoding;
        private readonly UtinniComboBox cboRepeat;
        private readonly UtinniTextbox txtRepeatArg;
        private readonly RadioButton rbEnum;
        private readonly RadioButton rbFlags;
        private readonly UtinniTextbox txtValueMap;

        /// <summary>The field the user defined (null until OK is pressed).</summary>
        public FieldRecord Result { get; private set; }

        /// <summary>The byte width of the selection that opened the dialog (shown for context; the engine
        /// computes the real on-wire width — this is the candor cue the planner asked for).</summary>
        private readonly int selectionWidth;

        public FormAssignFieldDialog(int selectionWidth)
        {
            this.selectionWidth = selectionWidth;
            Text = "Assign field";
            DrawName = true;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(380, 320);
            StartPosition = FormStartPosition.CenterParent;

            int labelLeft = 12;
            int fieldLeft = 120;
            int fieldWidth = 240;
            int y = 40;
            int rowH = 30;

            Controls.Add(MakeLabel("Name", labelLeft, y + 3));
            txtName = new UtinniTextbox { Left = fieldLeft, Top = y, Width = fieldWidth };
            Controls.Add(txtName);
            y += rowH;

            Controls.Add(MakeLabel("Type", labelLeft, y + 3));
            cboType = new UtinniComboBox { Left = fieldLeft, Top = y, Width = fieldWidth };
            foreach (KernelType kt in Enum.GetValues(typeof(KernelType)))
            {
                cboType.Items.Add(kt.ToString());
            }
            cboType.SelectedItem = KernelType.U32.ToString();
            Controls.Add(cboType);
            y += rowH;

            Controls.Add(MakeLabel("Encoding", labelLeft, y + 3));
            cboEncoding = new UtinniComboBox { Left = fieldLeft, Top = y, Width = fieldWidth };
            cboEncoding.Items.AddRange(new object[] { "(none)", "ascii", "latin1" });
            cboEncoding.SelectedIndex = 0;
            Controls.Add(cboEncoding);
            y += rowH;

            Controls.Add(MakeLabel("Repeat", labelLeft, y + 3));
            cboRepeat = new UtinniComboBox { Left = fieldLeft, Top = y, Width = fieldWidth };
            // The 3 D-10 repeat kinds + the not-an-array default. Sentinel-terminated is NOT offered (deferred).
            cboRepeat.Items.AddRange(new object[]
            {
                "(not an array)", "Fixed count", "Count from field…", "Until end of chunk"
            });
            cboRepeat.SelectedIndex = 0;
            Controls.Add(cboRepeat);
            y += rowH;

            Controls.Add(MakeLabel("Repeat arg", labelLeft, y + 3));
            txtRepeatArg = new UtinniTextbox { Left = fieldLeft, Top = y, Width = fieldWidth, Enabled = false };
            Controls.Add(txtRepeatArg);
            cboRepeat.SelectedIndexChanged += (s, e) =>
            {
                // Fixed count -> integer; Count from field -> prior field name; Until end -> no arg.
                txtRepeatArg.Enabled = cboRepeat.SelectedIndex == 1 || cboRepeat.SelectedIndex == 2;
            };
            y += rowH + 4;

            // Enum vs Flags — the EXPLICIT radio (Pitfall 4). Flags entries are bit POSITIONS 1..32.
            Controls.Add(MakeLabel("Value map", labelLeft, y + 3));
            rbEnum = new RadioButton
            {
                Text = "Enum (one value)", Left = fieldLeft, Top = y, Width = 150, Checked = true,
                ForeColor = Colors.Font(), BackColor = Colors.Primary(),
            };
            rbFlags = new RadioButton
            {
                Text = "Flags (combinable, bit 1..32)", Left = fieldLeft, Top = y + 22, Width = 220,
                ForeColor = Colors.Font(), BackColor = Colors.Primary(),
            };
            Controls.Add(rbEnum);
            Controls.Add(rbFlags);
            y += rowH + 22;

            Controls.Add(MakeLabel("name=value", labelLeft, y + 3));
            txtValueMap = new UtinniTextbox
            {
                Left = fieldLeft, Top = y, Width = fieldWidth,
                // One name=value per comma; for flags, value is the bit position (1..32).
            };
            Controls.Add(txtValueMap);
            y += rowH + 8;

            var btnOk = new UtinniButton
            {
                Text = "Assign", Left = fieldLeft + fieldWidth - 150, Top = y, Width = 70,
                UseDisableColor = true, UseVisualStyleBackColor = true,
            };
            btnOk.Click += OnOk;
            var btnCancel = new UtinniButton
            {
                Text = "Cancel", Left = fieldLeft + fieldWidth - 70, Top = y, Width = 70,
                UseDisableColor = true, UseVisualStyleBackColor = true,
            };
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private static UtinniLabel MakeLabel(string text, int left, int top)
        {
            return new UtinniLabel
            {
                Text = text, Left = left, Top = top, AutoSize = true, ForeColor = Colors.Font(),
            };
        }

        private void OnOk(object sender, EventArgs e)
        {
            string name = (txtName.Text ?? "").Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "Give the field a name — it's how arrays reference their count.",
                    "Assign field", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            KernelType type;
            if (!Enum.TryParse(cboType.SelectedItem as string ?? "U32", out type))
            {
                type = KernelType.U32;
            }

            var field = new FieldRecord
            {
                Name = name,
                Type = type,
                ByteWidth = NeedsWidth(type) ? Math.Max(0, selectionWidth) : 0,
            };

            string enc = cboEncoding.SelectedItem as string;
            if (!string.IsNullOrEmpty(enc) && enc != "(none)")
            {
                field.Encoding = enc;
            }

            // Repeat-spec (D-10). When the user picks a repeat kind, the field becomes an Array carrying
            // the picked element type would normally be a nested struct; here we mark the field Array and
            // record the repeat policy. The host/engine treats a missing element shape as the picked Type.
            RepeatSpec repeat = BuildRepeat();
            if (repeat != null)
            {
                field.Type = KernelType.Array;
                field.Repeat = repeat;
                // Preserve the element kind the user picked as the single struct-field element shape.
                field.StructFields = new List<FieldRecord>
                {
                    new FieldRecord { Name = name + "_elem", Type = type,
                                      ByteWidth = NeedsWidth(type) ? Math.Max(0, selectionWidth) : 0 }
                };
            }

            NamedValueMap map = BuildValueMap();
            if (map != null)
            {
                field.ValueMap = map;
            }

            Result = field;
            DialogResult = DialogResult.OK;
            Close();
        }

        private RepeatSpec BuildRepeat()
        {
            switch (cboRepeat.SelectedIndex)
            {
                case 1: // Fixed count
                    int count;
                    int.TryParse((txtRepeatArg.Text ?? "").Trim(), out count);
                    return new RepeatSpec { Kind = RepeatKind.FixedCount, FixedCount = Math.Max(0, count) };
                case 2: // Count from field…
                    return new RepeatSpec
                    {
                        Kind = RepeatKind.CountFromField,
                        CountFieldName = (txtRepeatArg.Text ?? "").Trim(),
                    };
                case 3: // Until end of chunk
                    return new RepeatSpec { Kind = RepeatKind.UntilEnd };
                default:
                    return null;
            }
        }

        // Parses "name=value, name=value" into a NamedValueMap. IsFlags follows the explicit radio
        // (Pitfall 4): for flags, value is a bit POSITION (1..32), NOT a mask.
        private NamedValueMap BuildValueMap()
        {
            string raw = (txtValueMap.Text ?? "").Trim();
            if (raw.Length == 0) return null;
            var entries = new Dictionary<string, long>();
            foreach (string pair in raw.Split(','))
            {
                string p = pair.Trim();
                if (p.Length == 0) continue;
                int eq = p.IndexOf('=');
                if (eq <= 0) continue;
                string key = p.Substring(0, eq).Trim();
                long val;
                if (!long.TryParse(p.Substring(eq + 1).Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out val))
                {
                    continue;
                }
                if (key.Length > 0) entries[key] = val;
            }
            if (entries.Count == 0) return null;
            return new NamedValueMap { IsFlags = rbFlags.Checked, Entries = entries };
        }

        private static bool NeedsWidth(KernelType t)
        {
            return t == KernelType.FixedChar || t == KernelType.RawBytes || t == KernelType.Pad;
        }
    }
}
