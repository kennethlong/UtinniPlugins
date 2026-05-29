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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using TJT.UI.Forms;
using UtinniCoreDotNet.Editing;
using UtinniCoreDotNet.Formats.Datatable;

namespace TJT.Saving
{
    /// <summary>
    /// TJT-side CSV/TSV file I/O + RFC-4180-ish parser for the datatable editor (Plan 09-06 D-08).
    /// A thin file-layer over the framework <see cref="CsvCellCoercion"/> helper: Export writes the
    /// current document; LoadAndPlan reads + parses a CSV and delegates the per-cell diff + coercion
    /// to <see cref="CsvCellCoercion.PlanImport"/>; ImportAsync runs the preview modal + applies the
    /// single-transaction <see cref="DatatableEditCommands.ApplyCsvImport"/>.
    ///
    /// <remarks>
    /// <para><b>Hand-rolled parser (RESEARCH § Don't Hand-Roll CSV row):</b> the RFC-4180 state
    /// machine in <see cref="ParseCsv"/> is under 100 lines (in-quotes flag; doubled-quote escape;
    /// comma/CRLF field+row terminators; preserves empty cells). The
    /// <c>Microsoft.VisualBasic.FileIO.TextFieldParser</c> alternative is REJECTED — it pulls in the
    /// whole Microsoft.VisualBasic assembly for a parser we can express in &lt; 100 lines, and its
    /// quirks (it trims, it has no streaming control) make the byte-exact-on-untouched contract
    /// harder to reason about.</para>
    ///
    /// <para><b>Format:</b> UTF-8 with BOM (UI-SPEC Assumption 8 + SOE convention); bare-name header
    /// row (RESEARCH Open Question 4 default); an optional <c>#</c>-prefixed second row carrying the
    /// type-spec (Excel-compatible passthrough). RFC-4180 double-quote escape on any field containing
    /// a comma, double-quote, or newline. DT_HashString columns carry the int32 hash, NOT the source
    /// string (item 9 — documented in the export header comment).</para>
    /// </remarks>
    /// </summary>
    public static class DatatableCsvSerializer
    {
        // ── Export ───────────────────────────────────────────────────────────

        /// <summary>
        /// Writes the document to a CSV/TSV file at the given path: a bare-name header row, an optional
        /// <c>#</c>-prefixed type-spec row, then one row per model row (each cell via
        /// <see cref="CsvCellCoercion.SerializeCellToCsv"/>). UTF-8 with BOM; RFC-4180 escape.
        /// </summary>
        public static void Export(MutableDataTableDocument doc, string path, bool includeTypeRow)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            if (path == null) throw new ArgumentNullException("path");

            var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var sw = new StreamWriter(stream, utf8Bom))
            {
                // A leading #-comment documenting the DT_HashString lossiness (item 9) so a human
                // re-importing knows hash columns carry the int32, not the source string.
                sw.WriteLine("# DT_HashString columns carry the stored int32 hash, not the source string (cannot round-trip the source).");

                // Header row — bare column names.
                var headerFields = new List<string>(doc.Columns.Count);
                for (int c = 0; c < doc.Columns.Count; c++)
                {
                    headerFields.Add(CsvEscape(doc.Columns[c].Name));
                }
                sw.WriteLine(string.Join(",", headerFields));

                // Optional #-prefixed type-spec row (Excel-compatible passthrough).
                if (includeTypeRow)
                {
                    var typeFields = new List<string>(doc.Columns.Count);
                    for (int c = 0; c < doc.Columns.Count; c++)
                    {
                        typeFields.Add(CsvEscape(doc.Columns[c].ColumnType.TypeSpec));
                    }
                    sw.WriteLine("#" + string.Join(",", typeFields));
                }

                // Data rows.
                for (int r = 0; r < doc.Rows.Count; r++)
                {
                    var cells = doc.Rows[r].Cells;
                    var fields = new List<string>(doc.Columns.Count);
                    for (int c = 0; c < doc.Columns.Count && c < cells.Count; c++)
                    {
                        fields.Add(CsvEscape(CsvCellCoercion.SerializeCellToCsv(cells[c], doc.Columns[c].ColumnType)));
                    }
                    sw.WriteLine(string.Join(",", fields));
                }
            }
        }

        // RFC-4180 + Excel: wrap in double-quotes + double internal quotes when the value contains a
        // comma, a double-quote, or a newline. A leading-# value is also quoted so it is not mistaken
        // for a type-annotation row on re-import.
        private static string CsvEscape(string value)
        {
            value = value ?? string.Empty;
            bool needsQuote = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0
                || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0
                || value.StartsWith("#", StringComparison.Ordinal);
            if (!needsQuote) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        // ── Load + plan ──────────────────────────────────────────────────────

        /// <summary>
        /// Reads + parses the CSV at the given path and builds a <see cref="CsvImportPlan"/> against the
        /// target document via <see cref="CsvCellCoercion.PlanImport"/>. The first row is the header; a
        /// second row whose first cell starts with <c>#</c> is skipped as a type annotation. UTF-8 BOM
        /// is auto-detected.
        /// </summary>
        public static CsvImportPlan LoadAndPlan(MutableDataTableDocument target, string csvPath)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (csvPath == null) throw new ArgumentNullException("csvPath");

            string text;
            using (var sr = new StreamReader(csvPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                text = sr.ReadToEnd();
            }

            List<List<string>> records = ParseCsv(text);

            // Drop leading #-comment lines (the export header comment + any user comments) and locate
            // the header row.
            int idx = 0;
            while (idx < records.Count && records[idx].Count > 0
                   && records[idx][0].StartsWith("#", StringComparison.Ordinal))
            {
                idx++;
            }

            if (idx >= records.Count)
            {
                // No data — return an empty plan (nothing to import).
                return new CsvImportPlan();
            }

            List<string> header = records[idx];
            idx++;

            // Skip an immediately-following #-prefixed type-annotation row.
            if (idx < records.Count && records[idx].Count > 0
                && records[idx][0].StartsWith("#", StringComparison.Ordinal))
            {
                idx++;
            }

            var rows = new List<IReadOnlyList<string>>();
            for (int i = idx; i < records.Count; i++)
            {
                rows.Add(records[i]);
            }

            return CsvCellCoercion.PlanImport(target, header, rows);
        }

        // Hand-rolled RFC-4180-ish parser (< 100 lines). Tracks an in-quotes flag; a doubled quote
        // inside quotes is a literal quote; commas split fields outside quotes; CR/LF terminate rows
        // outside quotes (CRLF treated as one terminator). Preserves empty cells. A trailing newline
        // does not emit a spurious empty record.
        private static List<List<string>> ParseCsv(string text)
        {
            var records = new List<List<string>>();
            var field = new StringBuilder();
            var record = new List<string>();
            bool inQuotes = false;
            bool sawAnyChar = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++; // consume the escaped quote
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                    sawAnyChar = true;
                }
                else if (c == ',')
                {
                    record.Add(field.ToString());
                    field.Clear();
                    sawAnyChar = true;
                }
                else if (c == '\r' || c == '\n')
                {
                    // CRLF: consume the paired LF.
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = new List<string>();
                    sawAnyChar = false;
                }
                else
                {
                    field.Append(c);
                    sawAnyChar = true;
                }
            }

            // Flush a trailing record if the file did not end with a newline (or had content).
            if (sawAnyChar || field.Length > 0 || record.Count > 0)
            {
                record.Add(field.ToString());
                records.Add(record);
            }

            return records;
        }

        // ── Import (preview modal + single-transaction apply) ────────────────

        /// <summary>
        /// Loads + plans the CSV on a background task (avoids a UI freeze on large CSVs), then on the
        /// UI thread shows the <see cref="FormCsvImportPreviewDialog"/>; on Import, applies the
        /// single-transaction <see cref="DatatableEditCommands.ApplyCsvImport"/> through the controller.
        /// Returns true when the import was applied, false on cancel.
        /// </summary>
        public static async Task<bool> ImportAsync(
            MutableDataTableDocument target,
            string csvPath,
            DatatableEditController controller,
            IWin32Window owner,
            string csvFileName,
            string tabFileName)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (csvPath == null) throw new ArgumentNullException("csvPath");
            if (controller == null) throw new ArgumentNullException("controller");

            CsvImportPlan plan = await Task.Run(() => LoadAndPlan(target, csvPath)).ConfigureAwait(true);

            using (var dlg = new FormCsvImportPreviewDialog(plan, csvFileName, tabFileName))
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
                controller.Apply(DatatableEditCommands.ApplyCsvImport(target, plan));
                return true;
            }
        }
    }
}
