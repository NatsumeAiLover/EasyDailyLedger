using System;
using System.Linq;
using DailyLedger;

internal static class TrendLogicTests
{
    private static int passed;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
    private static void Add(LedgerData data, string date, decimal amount, string kind)
    {
        data.Entries.Add(new Entry { Id = Guid.NewGuid().ToString("N"), Date = date, Kind = kind, Amount = amount, CategoryId = data.Categories.First(x => x.Kind == kind).Id, Note = "", CreatedAt = date });
    }
    private static int Main()
    {
        try
        {
            DateTime today = new DateTime(2026, 11, 12);
            var data = LedgerData.New();
            Add(data, "2026-11-01", 0.10m, "expense"); Add(data, "2026-11-01", 0.20m, "expense");
            Add(data, "2026-11-12", 100000000m, "expense"); Add(data, "2026-11-12", 80m, "income");
            Add(data, "2026-11-13", 500m, "expense"); // Proves explicit cutoff even for unvalidated input.
            Add(data, "2026-10-31", 5m, "expense"); Add(data, "2025-11-12", 11m, "expense");
            var daily = ExpenseTrends.Build(data, today, false, today);
            Check(daily.Current[0].Amount == 0.30m && daily.Current[0].Count == 2, "Exact cents and multiple entries in one day");
            Check(daily.Total == 100000000.30m, "Large amounts, income exclusion and cutoff");
            Check(daily.Current[1].Amount == 0 && daily.Current[1].Count == 0, "Elapsed empty days are explicit zero");
            Check(daily.Current[11] != null && daily.Current[12] == null, "Today is included; tomorrow is absent");
            Check(daily.Current.Length == 31 && daily.Current[30] == null && daily.Previous[30].Amount == 5m, "31-day comparison against 30-day current month");
            Check(daily.HasCurrent && daily.HasPrevious, "Presence flags reflect expense records");
            Check(daily.Total == LedgerLogic.Summarize(data, today, today).Spent, "Daily total agrees with existing home summary");
            var yearly = ExpenseTrends.Build(data, today, true, today);
            Check(yearly.Current.Length == 12 && yearly.Current[10].Amount == daily.Total, "Monthly aggregation agrees with daily total");
            Check(yearly.Current[11] == null && yearly.Current[0].Amount == 0, "Future month absent; past empty month zero");
            Check(yearly.Previous[10].Amount == 11m, "Previous-year monthly comparison");
            var historical = ExpenseTrends.Build(data, new DateTime(2026, 10, 1), false, today);
            Check(historical.Current.Count(x => x != null) == 31 && historical.Total == 5m, "Historical month includes month end");
            var feb = LedgerData.New(); Add(feb, "2024-02-29", 29m, "expense"); Add(feb, "2024-03-31", 31m, "expense");
            var march = ExpenseTrends.Build(feb, new DateTime(2024, 3, 1), false, today);
            Check(march.Previous[28].Amount == 29m && march.Previous[29] == null && march.Current[30].Amount == 31m, "Leap February ends without invented zero days");
            var leapFeb = ExpenseTrends.Build(feb, new DateTime(2024, 2, 1), false, today);
            Check(leapFeb.Current.Count(x => x != null) == 29 && leapFeb.Current[29] == null, "Leap month length");
            var nonLeap = ExpenseTrends.Build(feb, new DateTime(2025, 2, 1), false, today);
            Check(nonLeap.Current.Count(x => x != null) == 28, "Non-leap month length");
            var january = LedgerData.New(); Add(january, "2025-12-31", 12m, "expense");
            Check(ExpenseTrends.Build(january, new DateTime(2026, 1, 1), false, today).Previous[30].Amount == 12m, "January comparison crosses year boundary");
            var empty = ExpenseTrends.Build(LedgerData.New(), today, false, today);
            Check(!empty.HasCurrent && !empty.HasPrevious && empty.Total == 0, "Empty ledger has no invented history");
            var income = LedgerData.New(); Add(income, "2026-10-01", 10m, "income");
            Check(!ExpenseTrends.Build(income, today, false, today).HasPrevious, "Income-only period is not expense comparison data");
            int count = data.Entries.Count;
            var unchanged = ExpenseTrends.Build(data, today, false, today);
            Check(data.Entries.Count == count && data.Budgets.Count == 0 && unchanged.Total == daily.Total, "Viewing trends does not change records or budgets");
            data.Entries.First().Amount = 0.50m;
            Check(ExpenseTrends.Build(data, today, false, today).Current[0].Amount == 0.70m, "Edits refresh aggregation");
            data.Entries.RemoveAt(0);
            Check(ExpenseTrends.Build(data, today, false, today).Current[0].Amount == 0.20m, "Deletions refresh aggregation");
            Check(ExpenseTrends.Build(data, new DateTime(1900, 1, 1), false, today).Previous[0].Date.Year == 1899, "Earliest selectable month has safe comparison");
            var pastYear = ExpenseTrends.Build(data, new DateTime(2025, 1, 1), true, today);
            Check(pastYear.Current.All(x => x != null) && pastYear.Total == 11m, "Historical year covers twelve complete months");
            Console.WriteLine("Passed " + passed + " trend logic checks."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
