using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DailyLedger
{
    public sealed class ExpensePoint
    {
        public DateTime Date;
        public decimal Amount;
        public int Count;
    }
    public sealed class ExpenseTrend
    {
        public bool Monthly;
        public DateTime Period;
        public ExpensePoint[] Current, Previous;
        public decimal Total { get { return Current.Where(x => x != null).Sum(x => x.Amount); } }
        public bool HasCurrent { get { return Current.Any(x => x != null && x.Count > 0); } }
        public bool HasPrevious { get { return Previous.Any(x => x != null && x.Count > 0); } }
    }
    public static class ExpenseTrends
    {
        // Null means a future/nonexistent period; an available period without entries is zero.
        public static ExpenseTrend Build(LedgerData data, DateTime period, bool monthly, DateTime today)
        {
            today = today.Date;
            period = monthly ? new DateTime(period.Year, 1, 1) : new DateTime(period.Year, period.Month, 1);
            DateTime previous = monthly ? period.AddYears(-1) : period.AddMonths(-1);
            int slots = monthly ? 12 : Math.Max(DateTime.DaysInMonth(period.Year, period.Month), DateTime.DaysInMonth(previous.Year, previous.Month));
            var result = new ExpenseTrend { Period = period, Monthly = monthly, Current = new ExpensePoint[slots], Previous = new ExpensePoint[slots] };
            var totals = data.Entries.Where(x => x.Kind == "expense" && String.CompareOrdinal(x.Date, LedgerLogic.DateKey(previous)) >= 0 && String.CompareOrdinal(x.Date, LedgerLogic.DateKey(today)) <= 0)
                .GroupBy(x => monthly ? x.Date.Substring(0, 7) : x.Date)
                .ToDictionary(g => g.Key, g => new { Amount = g.Sum(x => x.Amount), Count = g.Count() });
            for (int series = 0; series < 2; series++)
            {
                DateTime start = series == 0 ? period : previous;
                var points = series == 0 ? result.Current : result.Previous;
                int count = monthly ? 12 : DateTime.DaysInMonth(start.Year, start.Month);
                for (int i = 0; i < count; i++)
                {
                    DateTime date = monthly ? start.AddMonths(i) : start.AddDays(i);
                    if (date > today) continue;
                    string key = monthly ? LedgerLogic.MonthKey(date) : LedgerLogic.DateKey(date);
                    var point = new ExpensePoint { Date = date };
                    if (totals.ContainsKey(key)) { point.Amount = totals[key].Amount; point.Count = totals[key].Count; }
                    points[i] = point;
                }
            }
            return result;
        }
    }

    // A vector chart keeps the portable application free of additional runtime dependencies.
    internal sealed class ExpenseChart : FrameworkElement
    {
        internal ExpenseTrend Model;
        internal bool Compare;
        internal int Selected = -1;
        internal event Action<int> PointSelected;
        private int hovered = -1;
        private Rect plot;
        private readonly ToolTip tip = new ToolTip();
        internal ExpenseChart()
        {
            Focusable = true; MinHeight = 310; Height = 350;
            ToolTip = tip;
            SizeChanged += (s, e) => InvalidateVisual();
            AutomationPropertiesWorkaround();
        }
        private void AutomationPropertiesWorkaround() { System.Windows.Automation.AutomationProperties.SetName(this, "支出折线图，使用左右方向键选择数据点，回车查看详情"); }
        private Brush BrushFor(string key) { return (Brush)FindResource(key); }
        private void Label(DrawingContext dc, string text, double x, double y, string color, double size)
        {
            var ft = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), size, BrushFor(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(x, y));
        }
        private double X(int i) { return plot.Left + plot.Width * i / Math.Max(1, Model.Current.Length - 1); }
        private static double Ceiling(double value)
        {
            if (value <= 0) return 100;
            double power = Math.Pow(10, Math.Floor(Math.Log10(value / 4)));
            double unit = value / 4 / power;
            return (unit <= 1 ? 1 : unit <= 2 ? 2 : unit <= 5 ? 5 : 10) * power * 4;
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            if (Model == null || ActualWidth < 140) return;
            plot = new Rect(82, 28, Math.Max(1, ActualWidth - 106), Math.Max(1, ActualHeight - 70));
            var values = Model.Current.Where(x => x != null).Select(x => (double)x.Amount);
            if (Compare && Model.HasPrevious) values = values.Concat(Model.Previous.Where(x => x != null).Select(x => (double)x.Amount));
            double maximum = Ceiling(values.DefaultIfEmpty(0).Max());
            for (int j = 0; j <= 4; j++)
            {
                double y = plot.Bottom - plot.Height * j / 4;
                dc.DrawLine(new Pen(BrushFor("GridLine"), 1), new Point(plot.Left, y), new Point(plot.Right, y));
                double value = maximum * j / 4;
                string label = value >= 100000000 ? (value / 100000000).ToString("0.#") + "亿" : value >= 10000 ? (value / 10000).ToString("0.#") + "万" : value.ToString("0.##");
                Label(dc, label, 4, y - 9, "Muted", 12);
            }
            Label(dc, "金额 / 元", 4, 0, "Muted", 11);
            int step = Model.Monthly ? 1 : ActualWidth < 650 ? 7 : 5;
            for (int i = 0; i < Model.Current.Length; i++)
                if (i == 0 || i == Model.Current.Length - 1 || (i + 1) % step == 0)
                    Label(dc, (i + 1) + (Model.Monthly ? "月" : "日"), X(i) - 10, plot.Bottom + 12, "Muted", 11);
            if (Compare && Model.HasPrevious) DrawSeries(dc, Model.Previous, maximum, "Muted", true);
            DrawSeries(dc, Model.Current, maximum, "Accent", false);
            int active = hovered >= 0 ? hovered : Selected;
            if (active >= 0 && active < Model.Current.Length)
                dc.DrawLine(new Pen(BrushFor("Border"), 1), new Point(X(active), plot.Top), new Point(X(active), plot.Bottom));
            if (!Model.HasCurrent) Label(dc, "本期暂无支出记录", plot.Left + 10, plot.Top + 6, "Muted", 13);
            if (IsKeyboardFocused) dc.DrawRectangle(null, new Pen(BrushFor("Accent"), 1), new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)));
        }
        private void DrawSeries(DrawingContext dc, ExpensePoint[] points, double max, string color, bool dashed)
        {
            var pen = new Pen(BrushFor(color), dashed ? 1.6 : 2.4);
            if (dashed) pen.DashStyle = DashStyles.Dash;
            Point? last = null;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i] == null) { last = null; continue; }
                var p = new Point(X(i), plot.Bottom - plot.Height * (double)points[i].Amount / max);
                if (last.HasValue) dc.DrawLine(pen, last.Value, p);
                dc.DrawEllipse(BrushFor(dashed ? "Surface" : color), new Pen(BrushFor(color), 1.5), p, 3, 3);
                last = p;
            }
        }
        private string Describe(ExpensePoint point)
        {
            if (point == null) return "未来或不存在的日期：无数据";
            string label = point.Date.ToString(Model.Monthly ? "yyyy 年 M 月" : "yyyy 年 M 月 d 日");
            return label + "：¥" + point.Amount.ToString("N2") + " · " + point.Count + " 笔" + (point.Count == 0 ? "（无支出记录）" : "") + (Model.Monthly && point.Date.Year == DateTime.Today.Year && point.Date.Month == DateTime.Today.Month ? "（截至今天，本月未结束）" : "");
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Model == null || plot.Width <= 0) return;
            Point p = e.GetPosition(this);
            if (!plot.Contains(p)) { hovered = -1; tip.IsOpen = false; InvalidateVisual(); return; }
            int i = Math.Max(0, Math.Min(Model.Current.Length - 1, (int)Math.Round((p.X - plot.Left) / plot.Width * (Model.Current.Length - 1))));
            if (hovered != i || !tip.IsOpen)
            {
                hovered = i; tip.Content = Describe(Model.Current[i]) + (Compare && Model.HasPrevious ? "\n对比 " + Describe(Model.Previous[i]) : "");
                tip.PlacementTarget = this; tip.IsOpen = true; InvalidateVisual();
            }
        }
        protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); hovered = -1; tip.IsOpen = false; InvalidateVisual(); }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e); Focus();
            if (hovered >= 0) Choose(hovered);
        }
        private void Choose(int index)
        {
            Selected = index; InvalidateVisual(); if (PointSelected != null) PointSelected(index);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e); if (Model == null) return;
            if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Home || e.Key == Key.End || e.Key == Key.Enter)
            {
                int end = Array.FindLastIndex(Model.Current, x => x != null);
                int next = e.Key == Key.Home ? 0 : e.Key == Key.End ? end : e.Key == Key.Left ? Selected - 1 : e.Key == Key.Right ? Selected + 1 : Selected;
                if (end >= 0) Choose(Math.Max(0, Math.Min(end, next)));
                e.Handled = true;
            }
        }
        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
        internal void Reset(ExpenseTrend model, bool compare)
        {
            Model = model; Compare = compare; hovered = -1; tip.IsOpen = false; InvalidateVisual();
        }
    }
}
