using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DailyLedger;

internal static class LedgerTests
{
    private static int passed;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
    private static T Find<T>(LedgerWindow controller, string name) where T : FrameworkElement { return (T)controller.Window.FindName(name); }
    private static void Click(LedgerWindow controller, string name) { Find<Button>(controller, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static List<LedgerDay> Days(DataGrid grid) { return ((ListCollectionView)grid.ItemsSource).Groups.Cast<CollectionViewGroup>().Select(x => (LedgerDay)x.Name).ToList(); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T) yield return (T)child;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Layout(LedgerWindow controller, double width)
    {
        var root = (FrameworkElement)controller.Window.Content;
        root.Measure(new Size(width, 800)); root.Arrange(new Rect(0, 0, width, 800)); root.UpdateLayout();
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame); root.UpdateLayout();
    }
    private static void Snapshot(LedgerWindow controller, string file)
    {
        Layout(controller, 1220);
        var root = (FrameworkElement)controller.Window.Content;
        var image = new RenderTargetBitmap(1220, 800, 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var output = File.Create(file)) encoder.Save(output);
    }
    private static void Add(LedgerData data, DateTime date, string kind, decimal amount, string note)
    {
        data.Entries.Add(new DailyLedger.Entry { Id = Guid.NewGuid().ToString("N"), Date = LedgerLogic.DateKey(date), CreatedAt = date.ToString("o"), Kind = kind, Amount = amount, Note = note, CategoryId = data.Categories.First(x => x.Kind == kind).Id });
    }
    [STAThread]
    private static int Main()
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ledger-runs", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
            var data = LedgerData.New();
            data.Budgets[LedgerLogic.MonthKey(month)] = 3000m;
            data.Budgets[LedgerLogic.MonthKey(month.AddMonths(1))] = 3000m;
            Add(data, month, "expense", 10m, "第一天");
            Add(data, month.AddDays(9), "expense", 20m, "中间一天");
            Add(data, month.AddDays(19), "expense", 30m, "最新一天");
            Add(data, month.AddDays(19), "income", 100m, "收入");
            using (var store = new LedgerStore(root))
            {
                store.Save(data); string before = File.ReadAllText(store.FilePath);
                new AppearanceStore(root).Save("dark");
                var controller = new LedgerWindow(store, data);
                Click(controller, "PreviousMonth"); Click(controller, "NavLedger"); Layout(controller, 1220);
                var grid = Find<DataGrid>(controller, "EntriesGrid"); var days = Days(grid);
                Check(days.Count == 3 && days[0].Date == LedgerLogic.DateKey(month.AddDays(19)) && days[2].Date == LedgerLogic.DateKey(month), "Initial dates run from newest to oldest");
                Check(days.Count(x => x.IsExpanded) == 1 && days[0].IsExpanded, "Only the latest day opens, including historical months without today's entries");
                Check(days[0].Summary.Contains("30.00") && days[0].Summary.Contains("100.00"), "Day header shows full-day expense and income totals");
                Snapshot(controller, Path.Combine(root, "ledger-charcoal.png"));
                controller.ChangeTheme("green"); Snapshot(controller, Path.Combine(root, "ledger-green.png"));
                controller.ChangeTheme("light"); Snapshot(controller, Path.Combine(root, "ledger-light.png"));
                controller.ChangeTheme("dark"); Layout(controller, 1220);
                var expanders = Descendants<Expander>(grid).ToList();
                Check(expanders.Count == 3 && expanders.Count(x => x.IsExpanded) == 1, "Rendered day groups use collapsible expanders");
                var latest = expanders.First(x => ((CollectionViewGroup)x.DataContext).Name == days[0]);
                var older = expanders.First(x => ((CollectionViewGroup)x.DataContext).Name == days[1]);
                Check(((ContentPresenter)older.Template.FindName("ExpandedContent", older)).Visibility == Visibility.Collapsed, "Older day details are actually hidden in the control template");
                var toggle = Descendants<ToggleButton>(older).First(); toggle.IsChecked = true; Layout(controller, 1220);
                Check(days[1].IsExpanded && ((ContentPresenter)older.Template.FindName("ExpandedContent", older)).Visibility == Visibility.Visible, "Manual expansion reveals the day's details and updates its state");
                var chosen = grid.Items.Cast<EntryRow>().First(x => x.Date == days[0].Date); grid.SelectedItem = chosen;
                latest.IsExpanded = false; Layout(controller, 1220);
                Check(!days[0].IsExpanded && grid.SelectedItem == null, "Collapsing a selected day clears its hidden selection");
                grid.SelectedItem = grid.Items.Cast<EntryRow>().First(x => x.Date == days[1].Date); string selectedId = ((EntryRow)grid.SelectedItem).Id;
                Click(controller, "DateSort"); Layout(controller, 1220); days = Days(grid);
                Check(days[0].Date == LedgerLogic.DateKey(month) && days[2].Date == LedgerLogic.DateKey(month.AddDays(19)), "Clicking Date reverses the day order");
                Check(!days[2].IsExpanded && days[1].IsExpanded && ((EntryRow)grid.SelectedItem).Id == selectedId, "Sorting preserves manual open/closed choices and the visible selection");
                Click(controller, "DateSort"); Layout(controller, 1220);
                Check(Days(grid)[0].Date == LedgerLogic.DateKey(month.AddDays(19)), "Clicking Date again restores newest first");
                Find<TextBox>(controller, "SearchText").Text = "最新一天"; Layout(controller, 1220);
                Check(grid.Items.Count == 1 && Days(grid)[0].Summary.Contains("100.00"), "Search filters details while headers retain full-day totals including hidden income");
                Find<TextBox>(controller, "SearchText").Clear();
                Find<ComboBox>(controller, "FilterType").SelectedIndex = 1; Layout(controller, 1220);
                Check(grid.Items.Count == 3 && Days(grid)[0].Summary.Contains("100.00"), "Expense filtering also retains full-day income totals");
                Find<ComboBox>(controller, "FilterType").SelectedIndex = 0;
                Find<TextBox>(controller, "SearchText").Text = "没有这笔账"; Layout(controller, 1220);
                Check(grid.Items.Count == 0 && Find<TextBlock>(controller, "LedgerEmpty").Visibility == Visibility.Visible, "An empty search displays the empty-state message");
                Find<TextBox>(controller, "SearchText").Clear(); Layout(controller, 1020);
                var sort = Find<Button>(controller, "DateSort");
                Check(sort.ActualWidth > 0 && grid.ActualWidth > 600 && Descendants<Expander>(grid).Count() == 3, "Sorting and day groups lay out at the minimum window width");
                Check(File.ReadAllText(store.FilePath) == before && Directory.GetFiles(store.BackupPath, "*.json").Length == 0, "Grouping, sorting, filtering and appearance never rewrite financial data");
                controller.Window.Close();
                controller = new LedgerWindow(store, store.Load()); Click(controller, "PreviousMonth"); Click(controller, "NavLedger"); Layout(controller, 1220);
                Check(Days(Find<DataGrid>(controller, "EntriesGrid"))[0].IsExpanded && Days(Find<DataGrid>(controller, "EntriesGrid")).Count(x => x.IsExpanded) == 1, "Reopening restores newest-first order with only the latest date expanded");
                grid = Find<DataGrid>(controller, "EntriesGrid");
                grid.SelectedItem = grid.Items.Cast<EntryRow>().First(x => x.DayGroup.IsExpanded && x.KindLabel == "支出");
                Click(controller, "EditEntry");
                Check(Find<TextBox>(controller, "EntryAmount").Text == "30.00" && Find<TextBox>(controller, "EntryNote").Text == "最新一天", "Expanded rows still open the correct transaction for editing");
                typeof(LedgerWindow).GetMethod("ResetForm", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(controller, null);
                controller.Window.Close();
            }
            app.Shutdown();
            Console.WriteLine("ALL " + passed + " LEDGER CHECKS PASSED"); Console.WriteLine("Test artifacts: " + root); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
