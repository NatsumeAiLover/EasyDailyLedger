using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DailyLedger
{
    internal sealed partial class LedgerWindow
    {
        private DateTime trendPeriod = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private bool trendMonthly, changingTrend;
        private ExpenseChart expenseChart;
        private ExpenseTrend expenseTrend;
        private void InitializeTrends()
        {
            expenseChart = new ExpenseChart();
            Find<Border>("TrendChartHost").Child = expenseChart;
            Select("TrendYear").ItemsSource = Enumerable.Range(1900, DateTime.Today.Year - 1899).Reverse().ToList();
            Select("TrendMonth").ItemsSource = Enumerable.Range(1, 12).ToList();
            Button("NavTrends").Click += (s, e) => ShowPage("trends");
            Button("TrendDaily").Click += (s, e) => { trendMonthly = false; RenderTrends(); };
            Button("TrendMonthly").Click += (s, e) => { trendMonthly = true; RenderTrends(); };
            Button("TrendPrevious").Click += (s, e) => MoveTrend(-1);
            Button("TrendNext").Click += (s, e) => MoveTrend(1);
            Button("TrendToday").Click += (s, e) => { trendPeriod = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); RenderTrends(); };
            Select("TrendYear").SelectionChanged += (s, e) => SelectTrendPeriod();
            Select("TrendMonth").SelectionChanged += (s, e) => SelectTrendPeriod();
            Find<CheckBox>("TrendCompare").Checked += (s, e) => { if (!changingTrend) RenderTrends(); };
            Find<CheckBox>("TrendCompare").Unchecked += (s, e) => { if (!changingTrend) RenderTrends(); };
            expenseChart.PointSelected += index => ShowTrendPoint(index);
            Button("TrendOpenLedger").Click += (s, e) => OpenTrendLedger();
        }
        private void SelectTrendPeriod()
        {
            if (changingTrend || Select("TrendYear").SelectedItem == null || Select("TrendMonth").SelectedItem == null) return;
            DateTime next = new DateTime((int)Select("TrendYear").SelectedItem, (int)Select("TrendMonth").SelectedItem, 1);
            DateTime current = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            trendPeriod = next > current ? current : next;
            expenseChart.Selected = -1; RenderTrends();
        }
        private void MoveTrend(int direction)
        {
            if (direction < 0 && (trendMonthly ? trendPeriod.Year == 1900 : trendPeriod == new DateTime(1900, 1, 1))) return;
            DateTime next = trendMonthly ? trendPeriod.AddYears(direction) : trendPeriod.AddMonths(direction);
            DateTime current = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            if (next > current) next = current;
            trendPeriod = next; expenseChart.Selected = -1; RenderTrends();
        }
        private void RenderTrends()
        {
            if (expenseChart == null) return;
            changingTrend = true;
            try
            {
                if (!Select("TrendYear").Items.Contains(DateTime.Today.Year)) Select("TrendYear").ItemsSource = Enumerable.Range(1900, DateTime.Today.Year - 1899).Reverse().ToList();
                Select("TrendYear").SelectedItem = trendPeriod.Year;
                Select("TrendMonth").SelectedItem = trendPeriod.Month;
                Find<StackPanel>("TrendMonthField").Visibility = trendMonthly ? Visibility.Collapsed : Visibility.Visible;
                Button("TrendPrevious").Content = trendMonthly ? "‹ 上一年" : "‹ 上一月";
                Button("TrendNext").Content = trendMonthly ? "下一年 ›" : "下一月 ›";
                Button("TrendToday").Content = trendMonthly ? "今年" : "本月";
                Button("TrendPrevious").IsEnabled = trendMonthly ? trendPeriod.Year > 1900 : trendPeriod > new DateTime(1900, 1, 1);
                Button("TrendNext").IsEnabled = trendMonthly ? trendPeriod.Year < DateTime.Today.Year : trendPeriod < new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                Find<CheckBox>("TrendCompare").Content = trendMonthly ? "对比上年" : "对比上月";
                AppTheme.Bind(Button("TrendDaily"), Control.BackgroundProperty, trendMonthly ? "ButtonBackground" : "AccentSoft");
                AppTheme.Bind(Button("TrendMonthly"), Control.BackgroundProperty, trendMonthly ? "AccentSoft" : "ButtonBackground");
                expenseTrend = ExpenseTrends.Build(data, trendPeriod, trendMonthly, DateTime.Today);
                bool compare = Find<CheckBox>("TrendCompare").IsChecked == true;
                if (expenseChart.Model == null || expenseChart.Model.Period != expenseTrend.Period || expenseChart.Model.Monthly != trendMonthly) expenseChart.Selected = -1;
                expenseChart.Reset(expenseTrend, compare);
                string period = trendPeriod.ToString(trendMonthly ? "yyyy 年" : "yyyy 年 M 月");
                bool current = trendMonthly ? trendPeriod.Year == DateTime.Today.Year : LedgerLogic.MonthKey(trendPeriod) == LedgerLogic.MonthKey(DateTime.Today);
                Text("TrendRange").Text = period + (current ? " · 截至 " + DateTime.Today.ToString("M 月 d 日") : " · 完整历史期间");
                Text("TrendTotal").Text = Money(expenseTrend.Total);
                var peak = expenseTrend.Current.Where(x => x != null && x.Amount > 0).OrderByDescending(x => x.Amount).FirstOrDefault();
                Text("TrendPeakLabel").Text = trendMonthly ? "最高支出月份（并列时显示最早）" : "最高支出日期（并列时显示最早）";
                Text("TrendPeak").Text = peak == null ? "暂无支出" : peak.Date.ToString(trendMonthly ? "M 月" : "M 月 d 日") + " · " + Money(peak.Amount);
                Text("TrendLegend").Text = "● 实线：" + period + (compare && expenseTrend.HasPrevious ? "     ○ 虚线：" + expenseTrend.Period.AddMonths(trendMonthly ? -12 : -1).ToString(trendMonthly ? "yyyy 年" : "yyyy 年 M 月") : "") + (compare && !expenseTrend.HasPrevious ? "     暂无对比支出记录" : "");
                Text("TrendRules").Text = "按账目日期汇总支出，收入不抵扣。过去无支出记录按 0 显示，未来和不存在的日期留空。" + (current ? "\n本月未结束，只统计截至今天的记录；对比线展示历史完整期间，不能把本月与历史整月直接视为同等时长。" : "") + "\n鼠标悬停查看金额；点击数据点固定详情。也可用 Tab 聚焦图表，再用左右方向键选择。";
                ShowTrendPoint(expenseChart.Selected);
            }
            finally { changingTrend = false; }
        }
        private void ShowTrendPoint(int index)
        {
            var point = index < 0 || index >= expenseTrend.Current.Length ? null : expenseTrend.Current[index];
            Button("TrendOpenLedger").IsEnabled = point != null;
            Text("TrendDetail").Text = point == null ? "请选择实线上的日期或月份，查看对应支出明细。" : point.Date.ToString(trendMonthly ? "yyyy 年 M 月" : "yyyy 年 M 月 d 日") + "  ·  支出 " + Money(point.Amount) + "  ·  " + point.Count + " 笔" + (point.Count == 0 ? "（无支出记录）" : "");
        }
        private void OpenTrendLedger()
        {
            int index = expenseChart.Selected;
            if (index < 0 || index >= expenseTrend.Current.Length || expenseTrend.Current[index] == null) return;
            DateTime date = expenseTrend.Current[index].Date;
            month = new DateTime(date.Year, date.Month, 1);
            refreshing = true;
            Select("FilterType").SelectedIndex = 1;
            refreshing = false;
            PopulateFilterCategories();
            Select("FilterCategory").SelectedIndex = 0;
            Input("SearchText").Text = trendMonthly ? "" : LedgerLogic.DateKey(date);
            if (!trendMonthly) expandedDays[LedgerLogic.DateKey(date)] = true;
            Refresh(); ShowPage("ledger");
        }
    }
}
