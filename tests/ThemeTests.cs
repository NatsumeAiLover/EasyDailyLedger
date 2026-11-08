using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DailyLedger;

internal static class ThemeTests
{
    private static int passed;
    private static void Check(bool valid, string name) { if (!valid) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
    private static double Channel(byte value) { double c = value / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
    private static double Luminance(Color color) { return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B); }
    private static double Contrast(Color a, Color b) { double x = Luminance(a), y = Luminance(b); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }
    private static T Find<T>(LedgerWindow controller, string name) where T : FrameworkElement { return (T)controller.Window.FindName(name); }
    private static Color ColorOf(Brush brush) { return ((SolidColorBrush)brush).Color; }
    private static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, name); }
    private static void Pump()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    private static void Entry(LedgerData data, DateTime date, string kind, string category, decimal amount, string note)
    {
        data.Entries.Add(new DailyLedger.Entry { Id = Guid.NewGuid().ToString("N"), Date = LedgerLogic.DateKey(date), Kind = kind, CategoryId = data.Categories.First(x => x.Kind == kind && x.Name == category).Id, Amount = amount, Note = note, CreatedAt = date.ToString("o") });
    }
    [STAThread]
    private static int Main()
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme-runs", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(root);
            var preferences = new AppearanceStore(root);
            Check(preferences.Load() == "light" && !File.Exists(preferences.FilePath), "Existing users start in light mode without a settings rewrite");
            preferences.Save("dark");
            Check(new AppearanceStore(root).Load() == "dark", "Dark mode persists across settings store instances");
            preferences.Save("light");
            Check(new AppearanceStore(root).Load() == "light", "Light mode can be restored and saved");
            string before = File.ReadAllText(preferences.FilePath);
            Reject(() => preferences.Save("invalid"), "Unsupported theme cannot be saved");
            Check(File.ReadAllText(preferences.FilePath) == before, "Rejected theme leaves saved settings untouched");
            using (var locked = new FileStream(preferences.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                Reject(() => preferences.Save("dark"), "A settings write failure is reported");
            Check(new AppearanceStore(root).Load() == "light" && Directory.GetFiles(root, "*.tmp").Length == 0, "Failed replacement retains previous settings and removes temporary file");
            File.WriteAllText(preferences.FilePath, "{broken");
            Check(preferences.Load() == "light" && preferences.LoadNotice != null && File.ReadAllText(preferences.FilePath) == "{broken", "Corrupt appearance settings fall back without erasing their file");
            File.WriteAllText(preferences.FilePath, "{\"Version\":2,\"Theme\":\"dark\"}");
            Check(preferences.Load() == "light" && preferences.LoadNotice != null, "Unknown appearance version safely falls back");
            preferences.Save("dark");
            preferences.Save("green");
            Check(new AppearanceStore(root).Load() == "green", "Original green dark mode persists as a separate choice");
            preferences.Save("dark");
            Check(AppTheme.Palette("dark")["Surface"].R == AppTheme.Palette("dark")["Surface"].G && AppTheme.Palette("green")["Surface"].G > AppTheme.Palette("green")["Surface"].R, "Charcoal dark is neutral and the original green palette remains available");
            foreach (string mode in new[] { "light", "dark", "green" })
            {
                var palette = AppTheme.Palette(mode);
                foreach (string[] pair in new[] { new[] { "Ink", "Surface" }, new[] { "Muted", "Surface" }, new[] { "Ink", "InputBackground" }, new[] { "OnAccent", "Accent" }, new[] { "SelectionInk", "SelectionBackground" }, new[] { "Danger", "Surface" } })
                    Check(Contrast(palette[pair[0]], palette[pair[1]]) >= 4.5, mode + " text contrast: " + pair[0] + " / " + pair[1]);
            }
            using (var store = new LedgerStore(root))
            {
                var data = LedgerData.New(); DateTime today = DateTime.Today;
                var month = new DateTime(today.Year, today.Month, 1);
                data.Budgets[LedgerLogic.MonthKey(month)] = 3000m;
                Entry(data, today, "expense", "餐饮", 28.50m, "食堂午饭 · 模拟数据");
                Entry(data, month, "expense", "学习", 60m, "教材 · 模拟数据");
                Entry(data, month, "expense", "购物", 120m, "生活用品 · 模拟数据");
                Entry(data, month, "income", "生活费", 800m, "");
                Entry(data, month.AddMonths(-1), "expense", "餐饮", 75m, "模拟数据");
                store.Save(data); string financialBefore = File.ReadAllText(store.FilePath);
                var controller = new LedgerWindow(store, data);
                controller.Window.Content.As<FrameworkElement>().Measure(new Size(1200, 820));
                Check(controller.CurrentTheme == "dark" && Find<ComboBox>(controller, "ThemeMode").SelectedIndex == 1, "Saved dark mode is applied before showing the window");
                Find<TextBox>(controller, "EntryAmount").Text = "45.67";
                Find<TextBox>(controller, "EntryNote").Text = "尚未保存的备注";
                Find<TextBox>(controller, "SearchText").Text = "午饭";
                var grid = Find<DataGrid>(controller, "EntriesGrid"); grid.SelectedIndex = 0; object selected = grid.SelectedItem;
                controller.ChangeTheme("light");
                Check(Find<TextBox>(controller, "EntryAmount").Text == "45.67" && Find<TextBox>(controller, "EntryNote").Text == "尚未保存的备注", "Theme changes preserve the unsaved transaction draft");
                Check(Find<TextBox>(controller, "SearchText").Text == "午饭" && grid.Items.Count == 1 && Object.ReferenceEquals(grid.SelectedItem, selected), "Theme changes preserve search and selected ledger row");
                Check(ColorOf(Find<TextBox>(controller, "EntryAmount").Foreground) == AppTheme.Palette("light")["Ink"], "Existing input text updates to the light palette");
                controller.ChangeTheme("dark");
                Check(ColorOf(Find<TextBox>(controller, "EntryAmount").Foreground) == AppTheme.Palette("dark")["Ink"] && ColorOf(Find<TextBox>(controller, "EntryAmount").Background) == AppTheme.Palette("dark")["InputBackground"], "Existing input background and text update to the dark palette");
                Check(Find<Button>(controller, "ToggleTheme").Content.ToString() == "切换墨绿" && Find<ComboBox>(controller, "ThemeMode").SelectedIndex == 1, "Sidebar shortcut and settings selector stay synchronized");
                Find<Button>(controller, "ToggleTheme").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(controller.CurrentTheme == "green" && Find<ComboBox>(controller, "ThemeMode").SelectedIndex == 2 && Object.ReferenceEquals(grid.SelectedItem, selected), "Shortcut switches to green without losing selection");
                Find<Button>(controller, "ToggleTheme").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(controller.CurrentTheme == "light", "Shortcut cycles from green back to light");
                Find<ComboBox>(controller, "ThemeMode").SelectedIndex = 1;
                Check(controller.CurrentTheme == "dark", "Settings selector switches directly to charcoal dark");
                Check(File.ReadAllText(store.FilePath) == financialBefore && Directory.GetFiles(store.BackupPath, "*.json").Length == 0, "Switching appearance never rewrites the ledger or adds financial backups");
                var calendar = new System.Windows.Controls.Calendar { Style = Find<DatePicker>(controller, "EntryDate").CalendarStyle, DisplayDate = today };
                Find<StackPanel>(controller, "CategoryBars").Children.Add(calendar);
                calendar.ApplyTemplate();
                var calendarItem = (System.Windows.Controls.Primitives.CalendarItem)calendar.Template.FindName("PART_CalendarItem", calendar);
                calendarItem.ApplyTemplate();
                calendar.Measure(new Size(320, 320)); calendar.Arrange(new Rect(0, 0, 320, 320)); Pump();
                var monthView = (Grid)calendarItem.Template.FindName("PART_MonthView", calendarItem);
                var yearView = (Grid)calendarItem.Template.FindName("PART_YearView", calendarItem);
                Check(monthView.Visibility == Visibility.Visible && yearView.Visibility != Visibility.Visible, "Date calendar initially shows days");
                calendar.DisplayMode = CalendarMode.Year;
                Pump();
                Check(monthView.Visibility != Visibility.Visible && yearView.Visibility == Visibility.Visible, "Calendar can switch to month selection");
                calendar.DisplayMode = CalendarMode.Decade;
                Pump();
                Check(yearView.Visibility == Visibility.Visible, "Calendar can switch to year selection");
                calendar.DisplayMode = CalendarMode.Month; Pump();
                var headerButton = (Button)calendarItem.Template.FindName("PART_HeaderButton", calendarItem);
                headerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                Check(calendar.DisplayMode == CalendarMode.Year && monthView.Visibility != Visibility.Visible && yearView.Visibility == Visibility.Visible, "Clicking the month/year header switches from dates to month selection");
                headerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                Check(calendar.DisplayMode == CalendarMode.Decade && yearView.Visibility == Visibility.Visible, "Clicking the header again switches to year selection");
                Find<TextBox>(controller, "EntryAmount").Clear(); Find<TextBox>(controller, "EntryNote").Clear(); controller.Window.Close();
                var reopened = new LedgerWindow(store, store.Load());
                Check(reopened.CurrentTheme == "dark" && store.Load().Entries.Count == 5, "Reopening keeps the dark theme and existing records");
                reopened.Window.Close();
                store.ExportBackup(data, Path.Combine(root, "ui-fixture.json"));
            }
            Console.WriteLine("ALL " + passed + " THEME CHECKS PASSED");
            Console.WriteLine("Test artifacts: " + root);
            app.Shutdown(); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static T As<T>(this object value) { return (T)value; }
}
