using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DailyLedger;

internal static class CsvTests
{
    private static int passed;
    private static readonly string Header = "日期,收支类别,类别,金额,备注\r\n";
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
    private static void Reject(Action action, string expected, string name)
    {
        try { action(); } catch (InvalidDataException e) { Check(e.Message.Contains(expected), name + " (" + e.Message + ")"); return; }
        throw new Exception("FAIL: " + name);
    }
    private static int Main(string[] args)
    {
        try
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "csv-runs", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(root); string input = Path.Combine(root, "input.csv");
            using (var store = new LedgerStore(Path.Combine(root, "data")))
            {
                var data = LedgerData.New(); data.Budgets["2026-01"] = 500m;
                var inactive = data.Categories.First(x => x.Name == "交通"); inactive.Active = false;
                store.Save(data); string original = File.ReadAllText(store.FilePath);
                File.WriteAllText(input, "忽略,金额,类别,日期,收支类别,备注,其他\r\nX,12.30,新类别,2026年1月2日,支出,\"买了东西,含\\字符\"\"和引号\r\n第二行\",Y\r\nZ,100,新类别,2026年01月03日,收入,,Q\r\nZ,5,交通,2026年1月4日,支出,地铁,Q\r\n\r\n", new UTF8Encoding(true));
                var imported = LedgerCsv.PrepareImport(store, data, input);
                Check(imported.EntryCount == 3 && imported.NewCategoryCount == 2 && imported.ReactivatedCategoryCount == 1, "Unknown names are added per income/expense kind and inactive names reactivate");
                Check(imported.Data.Entries[0].Date == "2026-01-02" && imported.Data.Entries[0].Amount == 12.30m && imported.Data.Entries[0].Note.Contains("\"和引号\r\n第二行"), "Reordered columns, exact cents, quotes, comma and multiline remarks");
                Check(imported.Data.Entries[1].Note == "" && imported.Data.Budgets["2026-01"] == 500m, "Blank optional remarks and budget preservation");
                Check(data.Entries.Count == 0 && !inactive.Active && File.ReadAllText(store.FilePath) == original, "Preview and cancellation do not mutate source or saved ledger");
                store.Save(imported.Data); data = store.Load();
                Check(data.Entries.Count == 3 && data.Categories.Count == 15 && data.Categories.Single(x => x.Name == "交通").Active, "Import entries and categories persist after reload");
                Check(store.ReadBackup(Directory.GetFiles(store.BackupPath, "*.json").Single()).Entries.Count == 0, "Pre-import backup contains original ledger");
                var repeated = LedgerCsv.PrepareImport(store, data, input);
                Check(repeated.EntryCount == 3 && repeated.NewCategoryCount == 0 && repeated.Data.Entries.Count == 6, "Repeated imports append and reuse categories without inventing deduplication");
                File.WriteAllText(input, "日期,收支类别,类别,金额,无关\n2024年2月29日,支出,餐饮,0.01,ignored\n", new UTF8Encoding(false));
                Check(LedgerCsv.PrepareImport(store, data, input).Data.Entries.Last().Note == "", "Missing remarks column and leap day accepted with UTF8 without BOM");
                File.WriteAllText(input, "日期,类型,大类,金额（元）,备注\n2026-01-02,支出,餐饮,1,旧版\n", Encoding.Unicode);
                Check(LedgerCsv.PrepareImport(store, data, input).EntryCount == 1, "Old export headers and ISO date work with UTF16 BOM");
                File.WriteAllText(input, Header + "2026年1月2日,收入,工资 / 兼职,10,中文备注\n", Encoding.GetEncoding(936));
                Check(LedgerCsv.PrepareImport(store, data, input).Data.Entries.Last().Note == "中文备注", "Chinese ANSI/GBK CSV accepted");
                File.WriteAllText(input, "日期\t收支类型\t类别\t账户\t金额\t备注\n2021年09月02日\t支出\t外部类别\t未关联\t30\t\n", Encoding.Unicode);
                var tabImport = LedgerCsv.PrepareImport(store, data, input);
                Check(tabImport.EntryCount == 1 && tabImport.NewCategoryCount == 1 && tabImport.Data.Entries.Last().Amount == 30m && tabImport.Data.Entries.Last().Note == "", "UTF16 tab-delimited CSV with income/expense alias and ignored account column");
                File.WriteAllText(input, "\r\n\t\t\r\n\"日期\"\t\"收支类型\"\t\"类别\"\t\"金额\"\t\"备注\"\t\"额外,栏目\"\r\n2021年9月2日\t收入\t餐饮\t12.30\t\"带,逗号\t制表符\"\"引号\r\n换行\"\t忽略\r\n", new UTF8Encoding(true));
                tabImport = LedgerCsv.PrepareImport(store, data, input);
                Check(tabImport.Data.Entries.Last().Kind == "income" && tabImport.Data.Entries.Last().Note == "带,逗号\t制表符\"引号\r\n换行", "Tab detection skips empty records and ignores separators inside quoted headers and remarks");
                File.WriteAllText(input, "\t\t\r\n\"日期\",\"收支类别\",\"类别\",\"金额\",\"备注\",\"额外\t栏目\"\r\n2021年9月2日,支出,餐饮,1,\"中文\t备注,逗号\",忽略\r\n", new UTF8Encoding(false));
                Check(LedgerCsv.PrepareImport(store, data, input).Data.Entries.Last().Note == "中文\t备注,逗号", "Comma CSV keeps tabs inside quoted cells without choosing tab separator");
                File.WriteAllText(input, "日期,收支类型,类别,金额\n2021年9月2日,支出,餐饮,1\n", new UTF8Encoding(true));
                Check(LedgerCsv.PrepareImport(store, data, input).EntryCount == 1, "Income/expense alias works independently of separator");
                File.WriteAllText(input, "日期\t收支类别\t收支类型\t类别\t金额\n", Encoding.Unicode);
                Reject(() => LedgerCsv.PrepareImport(store, data, input), "重复", "Canonical and alias headers cannot duplicate the same column");
                File.WriteAllText(input, "日期\t收支类型\t类别\t金额\t备注\n2021年9月2日\t支出\t餐饮\t1\t\"跨\n行\"\n错误\t支出\t餐饮\t2\t\n", Encoding.Unicode);
                Reject(() => LedgerCsv.PrepareImport(store, data, input), "第 4 行", "Tab CSV retains physical error line after multiline remark");
                string[] notes = { "=SUM(1,2)\n备注\"", " @SUM(1)", "'原始备注", "''两个单引号", "'普通备注", "-文字", "+文字", "" };
                foreach (string note in notes)
                    data.Entries.Add(new Entry { Id = Guid.NewGuid().ToString("N"), Date = "2026-01-05", Kind = "expense", CategoryId = data.Categories.First().Id, Amount = 2.01m, Note = note, CreatedAt = DateTime.Now.ToString("o") });
                var special = new Category { Id = Guid.NewGuid().ToString("N"), Name = "=特殊类别", Kind = "expense", Active = true }; data.Categories.Add(special); data.Entries.Last().CategoryId = special.Id;
                string output = Path.Combine(root, "export.csv"); store.ExportCsv(data, data.Entries, output);
                string csv = File.ReadAllText(output); byte[] bytes = File.ReadAllBytes(output);
                Check(csv.StartsWith(Header) && csv.Contains("2026年1月2日") && !csv.Contains("2026-01-02") && bytes.Take(3).SequenceEqual(new byte[] { 239,187,191 }), "Export header, unpadded Chinese dates and UTF8 BOM match v1.4 contract");
                var roundTrip = LedgerCsv.PrepareImport(store, LedgerData.New(), output);
                Check(roundTrip.EntryCount == data.Entries.Count && roundTrip.Data.Entries.Sum(x => x.Amount) == data.Entries.Sum(x => x.Amount), "Export and reimport preserve entry count and exact total");
                Check(notes.All(n => roundTrip.Data.Entries.Any(x => x.Note == n)) && roundTrip.Data.Categories.Any(x => x.Name == "=特殊类别"), "Formula escapes, apostrophes and special categories round-trip losslessly");
                store.ExportCsv(data, data.Entries.Take(1), output);
                Check(LedgerCsv.PrepareImport(store, LedgerData.New(), output).EntryCount == 1 && Directory.GetFiles(root, "*.tmp").Length == 0, "Export replaces existing file and cleans temporary sibling");
                string saved = File.ReadAllText(store.FilePath);
                string[,] failures = {
                    { "日期,类别,金额\n2026年1月2日,餐饮,1", "收支类别" },
                    { "日期,收支类别,类别,金额,金额\n", "重复" },
                    { Header + "2026年2月30日,支出,餐饮,1,", "第 2 行" },
                    { Header + "2026年1月2日,转账,餐饮,1,", "收支类别" },
                    { Header + "2026年1月2日,支出,,1,", "类别" },
                    { Header + "2026年1月2日,支出,餐饮,0,", "金额" },
                    { Header + "2026年1月2日,支出,餐饮,-1,", "金额" },
                    { Header + "2026年1月2日,支出,餐饮,1.001,", "金额" },
                    { Header + "2026年1月2日,支出,餐饮,100000001,", "金额" },
                    { Header + "2026年1月2日,支出,餐饮,1," + new String('长',201), "备注" },
                    { Header + "2026年1月2日,支出," + new String('长',31) + ",1,", "类别" },
                    { Header + DateTime.Today.AddDays(1).ToString("yyyy年M月d日", CultureInfo.InvariantCulture) + ",支出,餐饮,1,", "今天" },
                    { Header + "2026年1月2日,支出,餐饮", "金额" },
                    { Header + "2026年1月2日,支出,餐饮,1,\"未闭合", "引号未闭合" },
                    { Header + "2026年1月2日,支出,餐饮,1,\"关闭\"x", "结束引号" },
                    { Header + "2026年1月2日,支出,餐饮,1,错\"误", "引号" },
                    { Header, "没有可导入" },
                    { "", "没有可导入" },
                    { Header + "2026年1月2日,支出,待撤销类别,1,正确\n错误日期,支出,餐饮,2,错误", "第 3 行" },
                    { Header + "2026年1月2日,支出,餐饮,1,\"跨\n行\"\n错误,支出,餐饮,2,", "第 4 行" }
                };
                for (int i = 0; i < failures.GetLength(0); i++)
                {
                    File.WriteAllText(input, failures[i,0], new UTF8Encoding(true));
                    int index = i; Reject(() => LedgerCsv.PrepareImport(store, data, input), failures[i,1], "Reject malformed import " + index);
                }
                Check(File.ReadAllText(store.FilePath) == saved && !data.Categories.Any(x => x.Name == "待撤销类别"), "All validation errors preserve original ledger and categories");
                using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    bool failed = false; try { store.Save(imported.Data); } catch (IOException) { failed = true; }
                    Check(failed && Directory.GetFiles(store.DirectoryPath, "*.tmp").Length == 0, "Failed import save cleans temporary file");
                }
                Check(File.ReadAllText(store.FilePath) == saved, "Failed write preserves saved ledger");
                if (args.Length == 4)
                {
                    string suppliedHash = BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(args[0])));
                    var supplied = LedgerCsv.PrepareImport(store, LedgerData.New(), args[0]);
                    Check(supplied.EntryCount == Int32.Parse(args[1], CultureInfo.InvariantCulture), "Supplied file entry count matches independent CSV parser");
                    Check(supplied.Data.Entries.Where(x => x.Kind == "expense").Sum(x => x.Amount) == Decimal.Parse(args[2], CultureInfo.InvariantCulture) && supplied.Data.Entries.Where(x => x.Kind == "income").Sum(x => x.Amount) == Decimal.Parse(args[3], CultureInfo.InvariantCulture), "Supplied file income and expense totals match independent CSV parser");
                    store.Save(supplied.Data);
                    Check(store.Load().Entries.Count == supplied.EntryCount, "Supplied file persists completely in isolated test ledger");
                    string suppliedExport = Path.Combine(root, "supplied-roundtrip.csv"); store.ExportCsv(supplied.Data, supplied.Data.Entries, suppliedExport);
                    var suppliedRoundtrip = LedgerCsv.PrepareImport(store, LedgerData.New(), suppliedExport);
                    Func<LedgerData, string[]> values = ledger => ledger.Entries.Select(x => x.Date + "\t" + x.Kind + "\t" + LedgerLogic.CategoryName(ledger, x.CategoryId) + "\t" + x.Amount.ToString("0.00", CultureInfo.InvariantCulture) + "\t" + x.Note).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                    Check(values(supplied.Data).SequenceEqual(values(suppliedRoundtrip.Data)), "Supplied file export/reimport preserves all dates, types, categories, amounts and remarks");
                    Check(suppliedHash == BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(args[0]))), "Supplied CSV remains unmodified");
                }
            }
            Console.WriteLine("ALL " + passed + " CSV CHECKS PASSED"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
