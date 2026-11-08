using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DailyLedger;

internal static class CoreTests
{
    private static int passed;
    private static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
    private static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, name); }
    private static Entry Add(LedgerData data, string date, string kind, decimal amount, string note)
    {
        var entry = new Entry { Id = Guid.NewGuid().ToString("N"), Date = date, Kind = kind, CategoryId = data.Categories.First(x => x.Kind == kind).Id, Amount = amount, Note = note, CreatedAt = date + "T12:00:00" };
        data.Entries.Add(entry); return entry;
    }
    private static int Main(string[] args)
    {
        try
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runs", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(root);
            var data = LedgerData.New();
            data.Budgets["2026-10"] = 3000m;
            Check(LedgerLogic.InheritBudget(data, new DateTime(2026, 11, 1)) && data.Budgets["2026-11"] == 3000m, "New month inherits previous budget");
            data.Budgets["2026-10"] = 4000m;
            Check(!LedgerLogic.InheritBudget(data, new DateTime(2026, 11, 1)) && data.Budgets["2026-11"] == 3000m, "Saved month budget stays independent");
            Check(!LedgerLogic.InheritBudget(data, new DateTime(2026, 9, 1)), "Budget never inherited from a future month");
            Add(data, "2026-10-01", "expense", 420m, "previous");
            Add(data, "2026-10-05", "expense", 999m, "outside comparison");
            Add(data, "2026-11-01", "expense", 160m, "day one");
            Add(data, "2026-11-02", "expense", 216m, "day two");
            Add(data, "2026-11-03", "expense", 48m, "day three");
            Add(data, "2026-11-04", "expense", 62m, "day four");
            Add(data, "2026-11-01", "income", 800m, "");
            var current = LedgerLogic.Summarize(data, new DateTime(2026, 11, 1), new DateTime(2026, 11, 4));
            Check(current.Spent == 486m && current.Remaining == 2514m && current.Net == 314m, "Monthly spending, budget remaining and net are distinct");
            Check(current.Average == 121.5m && current.RemainingDays == 26, "Daily average includes today; allowance starts tomorrow");
            Check(current.PreviousSpent == 420m && current.PreviousCutoffDay == 4, "Comparison excludes later previous-month transactions");
            data.Budgets["2026-11"] = 0m;
            Check(LedgerLogic.Summarize(data, new DateTime(2026, 11, 1), new DateTime(2026, 11, 4)).Remaining == -486m, "Zero budget becomes overspend without division");
            var ending = LedgerLogic.Summarize(data, new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));
            Check(ending.RemainingDays == 0 && ending.ElapsedDays == 30, "Month end has no future days");
            var historical = LedgerLogic.Summarize(data, new DateTime(2026, 10, 1), new DateTime(2026, 11, 4));
            Check(historical.Spent == 1419m && historical.ElapsedDays == 31 && !historical.IsCurrent, "Historical month uses full-month totals");
            var dates = LedgerData.New(); Add(dates, "2024-02-29", "expense", 12m, "leap day");
            var leap = LedgerLogic.Summarize(dates, new DateTime(2024, 3, 1), new DateTime(2024, 3, 31));
            Check(leap.PreviousCutoffDay == 29 && leap.PreviousSpent == 12m, "Short previous month and leap day comparison");
            var empty = LedgerLogic.Summarize(LedgerData.New(), new DateTime(2026, 11, 1), new DateTime(2026, 11, 4));
            Check(!empty.Budget.HasValue && !empty.Remaining.HasValue && empty.Spent == 0m && !empty.HasPreviousEntries, "New ledger has no invented budget or comparison");
            decimal parsed;
            Check(LedgerLogic.TryMoney("12.30", false, out parsed) && parsed == 12.30m, "Exact two-decimal currency parsing");
            Check(!LedgerLogic.TryMoney("-2", false, out parsed) && !LedgerLogic.TryMoney("0", false, out parsed) && !LedgerLogic.TryMoney("1.001", false, out parsed) && !LedgerLogic.TryMoney("1e5", false, out parsed), "Invalid spending amounts rejected");
            Check(LedgerLogic.TryMoney("0", true, out parsed), "Zero monthly budget permitted");
            using (var store = new LedgerStore(Path.Combine(root, "data")))
            {
                Check(store.Load().Entries.Count == 0, "First open is empty");
                store.Save(data);
                var reopened = store.Load();
                Check(reopened.Entries.Count == 7 && reopened.Entries.Sum(x => x.Amount) == data.Entries.Sum(x => x.Amount), "Saved ledger round-trips without money loss");
                Reject(() => { using (var second = new LedgerStore(store.DirectoryPath)) { } }, "Concurrent writers blocked");
                var changed = store.Clone(data); changed.Entries.First(x => x.Date == "2026-11-04").Amount = 72.25m;
                store.Save(changed);
                Check(store.Load().Entries.First(x => x.Date == "2026-11-04").Amount == 72.25m, "Edit persists exact cents");
                string previous = Directory.GetFiles(store.BackupPath, "*.json").Single();
                Check(store.ReadBackup(previous).Entries.First(x => x.Date == "2026-11-04").Amount == 62m, "Pre-edit backup retains original transaction");
                changed.Entries.RemoveAll(x => x.Date == "2026-11-04"); store.Save(changed);
                Check(LedgerLogic.Summarize(store.Load(), new DateTime(2026, 11, 1), new DateTime(2026, 11, 4)).Spent == 424m, "Delete recomputes totals");
                string backupFile = Path.Combine(root, "manual-backup.json"); store.ExportBackup(data, backupFile);
                store.Save(store.ReadBackup(backupFile));
                Check(store.Load().Entries.Count == 7 && Directory.GetFiles(store.BackupPath, "*.json").Length == 3, "Restore keeps previous ledger snapshot");
                var invalid = store.Clone(data); invalid.Entries[0].Amount = -1m;
                string before = File.ReadAllText(store.FilePath);
                Reject(() => store.Save(invalid), "Invalid save rejected");
                Check(File.ReadAllText(store.FilePath) == before, "Invalid save leaves original bytes untouched");
                invalid = store.Clone(data); invalid.Entries[0].CategoryId = "missing";
                Reject(() => LedgerLogic.Validate(invalid), "Backup with missing category rejected");
                invalid = store.Clone(data); invalid.Version = 2;
                Reject(() => LedgerLogic.Validate(invalid), "Unsupported backup version rejected");
                invalid = store.Clone(data); invalid.Entries.Add(invalid.Entries[0]);
                Reject(() => LedgerLogic.Validate(invalid), "Duplicate transaction IDs rejected");
                invalid = store.Clone(data); invalid.Entries[0].Date = "2026-02-30";
                Reject(() => LedgerLogic.Validate(invalid), "Impossible dates rejected");
                invalid = store.Clone(data); invalid.Entries[0].Date = LedgerLogic.DateKey(DateTime.Today.AddDays(1));
                Reject(() => LedgerLogic.Validate(invalid), "Future-dated imported transactions rejected");
                var exportData = store.Clone(data); exportData.Entries[0].Note = "=SUM(1,2)\n中文,备注\"";
                string csvPath = Path.Combine(root, "export.csv"); store.ExportCsv(exportData, exportData.Entries, csvPath);
                byte[] csvBytes = File.ReadAllBytes(csvPath); string csv = File.ReadAllText(csvPath, Encoding.UTF8);
                Check(csvBytes[0] == 239 && csvBytes[1] == 187 && csvBytes[2] == 191 && csv.Contains("中文"), "CSV has UTF-8 BOM for Chinese Excel");
                Check(csv.Contains("\"'=SUM(1,2)\n中文,备注\"\"\"") && LedgerStore.CsvCell(" @SUM(1)").StartsWith("\"'"), "CSV quotes notes and neutralizes spreadsheet formulas");
                File.WriteAllText(store.FilePath, "{broken");
                Reject(() => store.Load(), "Corrupted live ledger is reported");
                Check(File.ReadAllText(store.FilePath) == "{broken", "Corrupted ledger is never silently reset");
                store.Save(store.ReadBackup(backupFile));
            }
            using (var unlocked = new LedgerStore(Path.Combine(root, "data"))) Check(unlocked.Load().Entries.Count == 7, "Closed ledger can reopen with all data");
            Console.WriteLine("ALL " + passed + " CHECKS PASSED");
            Console.WriteLine("Test artifacts: " + root);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
