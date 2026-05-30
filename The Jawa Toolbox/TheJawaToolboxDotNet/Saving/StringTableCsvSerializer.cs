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
using UtinniCoreDotNet.Formats.StringTable;

namespace TJT.Saving
{
    /// <summary>
    /// TJT-side CSV/TSV file I/O + RFC-4180-ish parser for the string-table editor (Plan 10-04 D-03b).
    /// A thin file-layer over the framework <see cref="StringTableCsvCoercion"/>: <see cref="Export"/>
    /// writes the current document as <c>key,text</c>; <see cref="LoadAndPlan"/> reads + parses a CSV and
    /// delegates the per-entry diff to <see cref="StringTableCsvCoercion.PlanImport"/>;
    /// <see cref="ExportPo"/> writes the framework <see cref="StringTablePoExport"/> output as UTF-8.
    ///
    /// <remarks>
    /// <para><b>Hand-rolled parser:</b> the RFC-4180 state machine in <see cref="ParseCsv"/> is under
    /// 100 lines (in-quotes flag; doubled-quote escape; comma/CRLF terminators; preserves empty cells) —
    /// the same shape as the Phase 9 <c>DatatableCsvSerializer</c>, mapped to two columns. The escape
    /// rule itself lives framework-side in <see cref="StringTableCsvCoercion.SerializeRowToCsv"/> so
    /// export ↔ import is symmetric and xUnit-coverable.</para>
    ///
    /// <para><b>Format:</b> UTF-8 with BOM (D-03b non-ASCII safety); a bare-name <c>key,text</c> header
    /// row; RFC-4180 double-quote escape on any field containing a comma, quote, or newline.</para>
    /// </remarks>
    /// </summary>
    public static class StringTableCsvSerializer
    {
        // ── Export ───────────────────────────────────────────────────────────

        /// <summary>
        /// Writes the document to a CSV file: a <c>key,text</c> header row then one row per NAMED entry
        /// via <see cref="StringTableCsvCoercion.SerializeRowToCsv"/>. UTF-8 with BOM.
        /// </summary>
        public static void Export(MutableStringTableDocument doc, string path)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            if (path == null) throw new ArgumentNullException("path");

            var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var sw = new StreamWriter(stream, utf8Bom))
            {
                sw.WriteLine("key,text");
                foreach (MutableStringTableEntry entry in doc.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // a nameless entry has no key.
                    sw.WriteLine(StringTableCsvCoercion.SerializeRowToCsv(entry.Name, entry.Text));
                }
            }
        }

        /// <summary>Writes the document to a PO/gettext file (UTF-8) via <see cref="StringTablePoExport"/>.</summary>
        public static void ExportPo(MutableStringTableDocument doc, string path)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            if (path == null) throw new ArgumentNullException("path");

            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(path, StringTablePoExport.ToPo(doc), utf8);
        }

        // ── Load + plan ──────────────────────────────────────────────────────

        /// <summary>
        /// Reads + parses the CSV at <paramref name="csvPath"/> and builds a
        /// <see cref="StringTableCsvImportPlan"/> against <paramref name="target"/> via
        /// <see cref="StringTableCsvCoercion.PlanImport"/>. The first non-comment row is the header; the
        /// <c>key</c> + <c>text</c> columns are resolved by name (case-insensitive), defaulting to the
        /// first two columns. UTF-8 BOM is auto-detected.
        /// </summary>
        public static StringTableCsvImportPlan LoadAndPlan(MutableStringTableDocument target, string csvPath)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (csvPath == null) throw new ArgumentNullException("csvPath");

            string text;
            using (var sr = new StreamReader(csvPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                text = sr.ReadToEnd();
            }

            List<List<string>> records = ParseCsv(text);

            // Skip leading #-comment rows, then take the header.
            int idx = 0;
            while (idx < records.Count && records[idx].Count > 0
                   && records[idx][0].StartsWith("#", StringComparison.Ordinal))
            {
                idx++;
            }

            if (idx >= records.Count)
            {
                return new StringTableCsvImportPlan(); // empty CSV — nothing to import.
            }

            List<string> header = records[idx];
            idx++;

            int keyCol = ResolveColumn(header, "key", 0);
            int textCol = ResolveColumn(header, "text", 1);

            var rows = new List<KeyValuePair<string, string>>();
            for (int i = idx; i < records.Count; i++)
            {
                List<string> rec = records[i];
                string key = keyCol >= 0 && keyCol < rec.Count ? rec[keyCol] : string.Empty;
                string val = textCol >= 0 && textCol < rec.Count ? rec[textCol] : string.Empty;
                rows.Add(new KeyValuePair<string, string>(key, val));
            }

            return StringTableCsvCoercion.PlanImport(target, rows);
        }

        // Resolve a header column by case-insensitive name, falling back to a default ordinal.
        private static int ResolveColumn(List<string> header, string name, int fallback)
        {
            for (int i = 0; i < header.Count; i++)
            {
                if (string.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return fallback < header.Count ? fallback : -1;
        }

        // Hand-rolled RFC-4180-ish parser (< 100 lines). In-quotes flag; doubled quote inside quotes is a
        // literal quote; commas split fields outside quotes; CR/LF terminate rows outside quotes (CRLF as
        // one terminator). Preserves empty cells; a trailing newline does not emit a spurious record.
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

            if (sawAnyChar || field.Length > 0 || record.Count > 0)
            {
                record.Add(field.ToString());
                records.Add(record);
            }

            return records;
        }
    }
}
