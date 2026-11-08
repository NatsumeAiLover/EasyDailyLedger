using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace DailyLedger
{
    public sealed class Category
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public bool Active { get; set; }
        public string FilterLabel { get { return String.IsNullOrEmpty(Kind) ? Name : Name + (Kind == "expense" ? "（支出）" : "（收入）") + (Active ? "" : " · 已停用"); } }
        public override string ToString() { return Name + (Active ? "" : "（已停用）"); }
    }
    public sealed class Entry
    {
        public string Id { get; set; }
        public string Date { get; set; }
        public string Kind { get; set; }
        public string CategoryId { get; set; }
        public decimal Amount { get; set; }
        public string Note { get; set; }
        public string CreatedAt { get; set; }
    }
    public sealed class LedgerData
    {
        public int Version { get; set; }
        public List<Category> Categories { get; set; }
        public List<Entry> Entries { get; set; }
        public Dictionary<string, decimal> Budgets { get; set; }
        public LedgerData()
        {
            Version = 1;
            Categories = new List<Category>();
            Entries = new List<Entry>();
            Budgets = new Dictionary<string, decimal>();
        }
        public static LedgerData New()
        {
            var data = new LedgerData();
            foreach (var name in new[] { "餐饮", "交通", "购物", "日用", "居住", "学习", "医疗", "娱乐", "其他" })
                data.Categories.Add(new Category { Id = Guid.NewGuid().ToString("N"), Name = name, Kind = "expense", Active = true });
            foreach (var name in new[] { "生活费", "工资 / 兼职", "奖助学金", "其他" })
                data.Categories.Add(new Category { Id = Guid.NewGuid().ToString("N"), Name = name, Kind = "income", Active = true });
            return data;
        }
    }
    public sealed class MonthSummary
    {
        public decimal Spent, Income, PreviousSpent;
        public decimal? Budget;
        public int ElapsedDays, RemainingDays, PreviousCutoffDay;
        public bool IsCurrent, HasPreviousEntries;
        public decimal Average { get { return ElapsedDays == 0 ? 0 : Spent / ElapsedDays; } }
        public decimal? Remaining { get { return Budget.HasValue ? Budget.Value - Spent : (decimal?)null; } }
        public decimal Net { get { return Income - Spent; } }
    }
    public static class LedgerLogic
    {
        public static string MonthKey(DateTime date) { return date.ToString("yyyy-MM", CultureInfo.InvariantCulture); }
        public static string DateKey(DateTime date) { return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        public static DateTime ParseDate(string value)
        {
            DateTime parsed;
            if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                throw new InvalidDataException("账目日期格式不正确。");
            return parsed;
        }
        public static bool TryMoney(string value, bool allowZero, out decimal amount)
        {
            if (!decimal.TryParse(value.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)) return false;
            return amount >= (allowZero ? 0m : 0.01m) && amount <= 100000000m && decimal.Round(amount, 2) == amount;
        }
        public static decimal? BudgetFor(LedgerData data, DateTime month)
        {
            decimal result;
            return data.Budgets.TryGetValue(MonthKey(month), out result) ? result : (decimal?)null;
        }
        public static bool InheritBudget(LedgerData data, DateTime month)
        {
            string key = MonthKey(month);
            if (data.Budgets.ContainsKey(key)) return false;
            string previous = data.Budgets.Keys.Where(x => String.CompareOrdinal(x, key) < 0).OrderByDescending(x => x, StringComparer.Ordinal).FirstOrDefault();
            if (previous == null) return false;
            data.Budgets[key] = data.Budgets[previous];
            return true;
        }
        public static MonthSummary Summarize(LedgerData data, DateTime month, DateTime today)
        {
            month = new DateTime(month.Year, month.Month, 1);
            var current = new DateTime(today.Year, today.Month, 1);
            bool isCurrent = month == current;
            int elapsed = month < current ? DateTime.DaysInMonth(month.Year, month.Month) : isCurrent ? today.Day : 0;
            var previous = month.AddMonths(-1);
            int previousCutoff = isCurrent ? Math.Min(today.Day, DateTime.DaysInMonth(previous.Year, previous.Month)) : DateTime.DaysInMonth(previous.Year, previous.Month);
            var currentRows = data.Entries.Where(x => x.Date.StartsWith(MonthKey(month) + "-", StringComparison.Ordinal) && ParseDate(x.Date).Day <= elapsed).ToList();
            var previousRows = data.Entries.Where(x => x.Date.StartsWith(MonthKey(previous) + "-", StringComparison.Ordinal) && ParseDate(x.Date).Day <= previousCutoff).ToList();
            return new MonthSummary {
                Spent = currentRows.Where(x => x.Kind == "expense").Sum(x => x.Amount),
                Income = currentRows.Where(x => x.Kind == "income").Sum(x => x.Amount),
                PreviousSpent = previousRows.Where(x => x.Kind == "expense").Sum(x => x.Amount),
                HasPreviousEntries = previousRows.Any(), Budget = BudgetFor(data, month),
                ElapsedDays = elapsed, RemainingDays = isCurrent ? DateTime.DaysInMonth(month.Year, month.Month) - today.Day : 0,
                PreviousCutoffDay = previousCutoff, IsCurrent = isCurrent
            };
        }
        public static void Validate(LedgerData data)
        {
            if (data == null || data.Version != 1) throw new InvalidDataException("不支持的账本版本。");
            if (data.Categories == null || data.Entries == null || data.Budgets == null) throw new InvalidDataException("账本内容不完整。");
            if (data.Categories.Count > 1000 || data.Entries.Count > 500000 || data.Budgets.Count > 12000) throw new InvalidDataException("账本规模超出当前版本的限制。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var category in data.Categories)
            {
                if (category == null || String.IsNullOrWhiteSpace(category.Id) || !ids.Add(category.Id) || category.Id.Length > 100 || String.IsNullOrWhiteSpace(category.Name) || category.Name.Length > 30 || !ValidKind(category.Kind))
                    throw new InvalidDataException("大类数据不正确。");
            }
            foreach (string kind in new[] { "expense", "income" })
            {
                if (!data.Categories.Any(x => x.Kind == kind && x.Active)) throw new InvalidDataException("收入和支出各需要至少一个启用的大类。");
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var category in data.Categories.Where(x => x.Kind == kind))
                    if (!names.Add(category.Name.Trim())) throw new InvalidDataException("同一类型的大类名称不能重复。");
            }
            ids.Clear();
            foreach (var entry in data.Entries)
            {
                if (entry == null || String.IsNullOrWhiteSpace(entry.Id) || entry.Id.Length > 100 || !ids.Add(entry.Id) || !ValidKind(entry.Kind) || entry.Amount <= 0 || entry.Amount > 100000000m || decimal.Round(entry.Amount, 2) != entry.Amount || entry.Note == null || entry.Note.Length > 200)
                    throw new InvalidDataException("账目数据不正确。");
                if (ParseDate(entry.Date).Date > DateTime.Today) throw new InvalidDataException("账目不能使用未来日期：" + entry.Date);
                if (!data.Categories.Any(x => x.Id == entry.CategoryId && x.Kind == entry.Kind)) throw new InvalidDataException("账目的大类不存在或类型不匹配。");
            }
            foreach (var pair in data.Budgets)
            {
                DateTime parsed;
                if (!DateTime.TryParseExact(pair.Key, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed) || pair.Value < 0 || pair.Value > 100000000m || decimal.Round(pair.Value, 2) != pair.Value)
                    throw new InvalidDataException("预算数据不正确。");
            }
        }
        private static bool ValidKind(string kind) { return kind == "expense" || kind == "income"; }
        public static string CategoryName(LedgerData data, string id) { return data.Categories.First(x => x.Id == id).Name; }
    }
    public sealed class LedgerStore : IDisposable
    {
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 64000000, RecursionLimit = 50 };
        private readonly FileStream instanceLock;
        public string DirectoryPath { get; private set; }
        public string FilePath { get { return Path.Combine(DirectoryPath, "账本.json"); } }
        public string BackupPath { get { return Path.Combine(DirectoryPath, "自动备份"); } }
        public LedgerStore(string directory)
        {
            DirectoryPath = Path.GetFullPath(directory);
            Directory.CreateDirectory(DirectoryPath);
            try { instanceLock = new FileStream(Path.Combine(DirectoryPath, "账本.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) { throw new IOException("这个账本正在另一个窗口中使用。请先关闭已打开的日常账本。", error); }
        }
        public LedgerData Load()
        {
            if (!File.Exists(FilePath)) return LedgerData.New();
            return ReadBackup(FilePath);
        }
        public LedgerData ReadBackup(string path)
        {
            if (new FileInfo(path).Length > 64000000) throw new InvalidDataException("备份文件过大。");
            var data = serializer.Deserialize<LedgerData>(File.ReadAllText(path, Encoding.UTF8));
            LedgerLogic.Validate(data);
            return data;
        }
        public LedgerData Clone(LedgerData data) { return serializer.Deserialize<LedgerData>(serializer.Serialize(data)); }
        public void Save(LedgerData data)
        {
            LedgerLogic.Validate(data);
            Directory.CreateDirectory(BackupPath);
            string temporary = Path.Combine(DirectoryPath, ".账本-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                WriteDurable(temporary, serializer.Serialize(data));
                if (File.Exists(FilePath))
                {
                    string backup = Path.Combine(BackupPath, "账本-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
                    File.Replace(temporary, FilePath, backup);
                }
                else File.Move(temporary, FilePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static void WriteDurable(string path, string content)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(content);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }
        public void ExportBackup(LedgerData data, string path)
        {
            LedgerLogic.Validate(data);
            // Export uses a temporary sibling and an atomic replacement, too.
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                WriteDurable(temporary, serializer.Serialize(data));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public void ExportCsv(LedgerData data, IEnumerable<Entry> entries, string path)
        {
            var csv = new StringBuilder("日期,类型,大类,金额（元）,备注\r\n");
            foreach (var entry in entries.OrderBy(x => x.Date).ThenBy(x => x.CreatedAt))
                csv.Append(String.Join(",", new[] { CsvCell(entry.Date), CsvCell(entry.Kind == "expense" ? "支出" : "收入"), CsvCell(LedgerLogic.CategoryName(data, entry.CategoryId)), CsvCell(entry.Amount.ToString("0.00", CultureInfo.InvariantCulture)), CsvCell(entry.Note) })).Append("\r\n");
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
        }
        public static string CsvCell(string value)
        {
            value = value ?? "";
            string trimmed = value.TrimStart();
            if (trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        public void Dispose() { instanceLock.Dispose(); }
    }
}
