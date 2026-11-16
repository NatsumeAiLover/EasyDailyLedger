using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DailyLedger
{
    public sealed class CsvImportResult
    {
        public LedgerData Data { get; internal set; }
        public int EntryCount { get; internal set; }
        public int NewCategoryCount { get; internal set; }
        public int ReactivatedCategoryCount { get; internal set; }
    }

    public static class LedgerCsv
    {
        private sealed class Record
        {
            public int Line;
            public List<string> Cells;
        }
        private static InvalidDataException Error(int line, string message)
        { return new InvalidDataException("CSV 第 " + line + " 行：" + message); }

        // Quoted fields may contain commas, escaped quotes and physical newlines.
        private static IEnumerable<Record> ReadRecords(string text, char delimiter)
        {
            var cells = new List<string>(); var cell = new StringBuilder();
            bool quoted = false, closed = false; int line = 1, start = 1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else { quoted = false; closed = true; }
                    }
                    else { cell.Append(c); if (c == '\n' || (c == '\r' && (i + 1 == text.Length || text[i + 1] != '\n'))) line++; }
                    continue;
                }
                if (c == delimiter || c == '\r' || c == '\n')
                {
                    cells.Add(cell.ToString()); cell.Clear(); closed = false;
                    if (c == delimiter) continue;
                    yield return new Record { Line = start, Cells = cells };
                    cells = new List<string>();
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    line++; start = line;
                }
                else if (closed) throw Error(line, "结束引号后只能是列分隔符或换行。");
                else if (c == '"')
                {
                    if (cell.Length != 0) throw Error(line, "字段中的引号必须写为双引号转义，并用引号包围整个字段。");
                    quoted = true;
                }
                else cell.Append(c);
            }
            if (quoted) throw Error(start, "引号未闭合。");
            if (cell.Length > 0 || closed || cells.Count > 0)
            { cells.Add(cell.ToString()); yield return new Record { Line = start, Cells = cells }; }
        }
        private static char DetectDelimiter(string text)
        {
            // Inspect the first nonempty logical header, ignoring separators inside quotes.
            // Some spreadsheet exports use UTF-16 tab-separated text with a .csv extension.
            bool quoted = false, content = false; int commas = 0, tabs = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < text.Length && text[i + 1] == '"') { i++; continue; }
                    quoted = !quoted; continue;
                }
                if (quoted) { if (!Char.IsWhiteSpace(c)) content = true; continue; }
                if (c == '\r' || c == '\n')
                {
                    if (content) break;
                    commas = tabs = 0; continue;
                }
                if (c == ',') commas++;
                else if (c == '\t') tabs++;
                else if (!Char.IsWhiteSpace(c) && c != '\ufeff') content = true;
            }
            return tabs > commas ? '\t' : ',';
        }
        private static string Header(string value)
        {
            switch (value.Trim().TrimStart('\ufeff'))
            {
                case "日期": return "日期";
                case "收支类别": case "收支类型": case "类型": return "收支类别";
                case "类别": case "大类": return "类别";
                case "金额": case "金额（元）": return "金额";
                case "备注": return "备注";
                default: return null;
            }
        }
        private static string Value(Record record, Dictionary<string, int> columns, string name, bool optional)
        {
            int index;
            if (!columns.TryGetValue(name, out index) || index >= record.Cells.Count)
            {
                if (optional) return "";
                throw Error(record.Line, "缺少“" + name + "”的值。");
            }
            return record.Cells[index];
        }
        private static string ReadText(string path)
        {
            if (new FileInfo(path).Length > 64000000) throw new InvalidDataException("CSV 文件不能超过 64 MB。");
            // Excel CSV commonly uses UTF-8, UTF-16 BOM or the Chinese ANSI code page.
            try { using (var reader = new StreamReader(path, new UTF8Encoding(false, true), true)) return reader.ReadToEnd(); }
            catch (DecoderFallbackException)
            {
                return File.ReadAllText(path, Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback));
            }
        }
        public static CsvImportResult PrepareImport(LedgerStore store, LedgerData current, string path)
        {
            var result = new CsvImportResult { Data = store.Clone(current) };
            var columns = new Dictionary<string, int>(); bool headerFound = false;
            var categories = result.Data.Categories.ToDictionary(x => x.Kind + "\n" + x.Name.Trim(), StringComparer.OrdinalIgnoreCase);
            string text = ReadText(path);
            foreach (var record in ReadRecords(text, DetectDelimiter(text)))
            {
                if (record.Cells.All(String.IsNullOrWhiteSpace)) continue;
                if (!headerFound)
                {
                    for (int i = 0; i < record.Cells.Count; i++)
                    {
                        string name = Header(record.Cells[i]); if (name == null) continue;
                        if (columns.ContainsKey(name)) throw Error(record.Line, "“" + name + "”栏目重复。");
                        columns.Add(name, i);
                    }
                    foreach (string name in new[] { "日期", "收支类别", "类别", "金额" })
                        if (!columns.ContainsKey(name)) throw Error(record.Line, "表头缺少“" + name + "”栏目。");
                    headerFound = true; continue;
                }
                DateTime date;
                string dateText = Value(record, columns, "日期", false).Trim();
                if (!DateTime.TryParseExact(dateText, new[] { "yyyy'年'M'月'd'日'", "yyyy'年'MM'月'dd'日'", "yyyy'年'M'月'dd'日'", "yyyy'年'MM'月'd'日'", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                    throw Error(record.Line, "日期格式应为 2026年1月2日。");
                if (date.Year < 1900 || date > DateTime.Today) throw Error(record.Line, "日期须在 1900年1月1日至今天之间。");
                string kindText = Value(record, columns, "收支类别", false).Trim();
                if (kindText != "支出" && kindText != "收入") throw Error(record.Line, "收支类别只能是“支出”或“收入”。");
                string kind = kindText == "支出" ? "expense" : "income";
                string nameValue = DecodeCell(Value(record, columns, "类别", false)).Trim();
                if (String.IsNullOrWhiteSpace(nameValue) || nameValue.Length > 30) throw Error(record.Line, "类别须为 1 至 30 个字符。");
                decimal amount;
                if (!LedgerLogic.TryMoney(Value(record, columns, "金额", false), false, out amount)) throw Error(record.Line, "金额须大于零、最多两位小数且不超过 100000000。");
                string note = DecodeCell(Value(record, columns, "备注", true));
                if (note.Length > 200) throw Error(record.Line, "备注不能超过 200 个字符。");
                Category category; string key = kind + "\n" + nameValue;
                if (!categories.TryGetValue(key, out category))
                {
                    if (result.Data.Categories.Count >= 1000) throw Error(record.Line, "类别总数不能超过 1000 个。");
                    category = new Category { Id = Guid.NewGuid().ToString("N"), Name = nameValue, Kind = kind, Active = true };
                    result.Data.Categories.Add(category); categories.Add(key, category); result.NewCategoryCount++;
                }
                else if (!category.Active) { category.Active = true; result.ReactivatedCategoryCount++; }
                if (result.Data.Entries.Count >= 500000) throw Error(record.Line, "账目总数不能超过 500000 笔。");
                result.Data.Entries.Add(new Entry { Id = Guid.NewGuid().ToString("N"), Date = LedgerLogic.DateKey(date), Kind = kind, CategoryId = category.Id, Amount = amount, Note = note, CreatedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) });
                result.EntryCount++;
            }
            if (!headerFound || result.EntryCount == 0) throw new InvalidDataException("CSV 中没有可导入的账目。");
            LedgerLogic.Validate(result.Data);
            return result;
        }
        // Reverse the exporter escape without removing ordinary leading apostrophes.
        internal static string DecodeCell(string value)
        {
            if (value.StartsWith("''", StringComparison.Ordinal)) return value.Substring(1);
            if (value.StartsWith("'", StringComparison.Ordinal))
            {
                string rest = value.Substring(1).TrimStart();
                if (rest.Length > 0 && "=+-@".IndexOf(rest[0]) >= 0) return value.Substring(1);
            }
            return value;
        }
    }
}
