using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

[assembly: AssemblyTitle("日常账本")]
[assembly: AssemblyDescription("按天记录，按月安排的个人本机账本")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace DailyLedger
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            System.Threading.Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "数据");
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--data-dir") directory = Path.GetFullPath(args[++i]);
            LedgerStore store = null;
            try
            {
                store = new LedgerStore(directory);
                LedgerData data;
                try { data = store.Load(); }
                catch (Exception error)
                {
                    var answer = MessageBox.Show("无法读取当前账本，原文件没有被改动。\n\n" + error.Message + "\n\n要选择一个 JSON 备份来恢复吗？", "账本读取失败", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (answer != MessageBoxResult.Yes) return;
                    var dialog = new OpenFileDialog { Title = "选择账本备份", Filter = "账本备份 (*.json)|*.json", InitialDirectory = Directory.Exists(store.BackupPath) ? store.BackupPath : store.DirectoryPath };
                    if (dialog.ShowDialog() != true) return;
                    data = store.ReadBackup(dialog.FileName);
                    store.Save(data);
                }
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                var controller = new LedgerWindow(store, data);
                app.MainWindow = controller.Window;
                app.DispatcherUnhandledException += (sender, eventArgs) => {
                    LedgerDialogs.Show(controller.Window, "操作未完成：\n" + eventArgs.Exception.Message, "日常账本", MessageBoxButton.OK, MessageBoxImage.Warning);
                    eventArgs.Handled = true;
                };
                app.Run(controller.Window);
            }
            catch (Exception error)
            {
                try { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "启动错误.txt"), error.ToString()); } catch { }
                MessageBox.Show("日常账本未能打开。\n\n" + error.Message + "\n\n请把整个程序文件夹放在可写的位置（例如文档文件夹），并保留其中的“数据”文件夹。", "日常账本", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { if (store != null) store.Dispose(); }
        }
    }
    public sealed class EntryRow
    {
        public string Id { get; set; }
        public string Date { get; set; }
        public string DayGroup { get; set; }
        public string KindLabel { get; set; }
        public string CategoryName { get; set; }
        public string AmountLabel { get; set; }
        public string Note { get; set; }
    }
    internal sealed class LedgerWindow
    {
        private readonly LedgerStore store;
        private LedgerData data;
        public Window Window { get; private set; }
        private DateTime month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private string entryKind = "expense", editId = null, page = "home";
        private bool refreshing;
        private readonly AppearanceStore appearance;
        private string theme;
        private bool updatingThemeControls;
        internal string CurrentTheme { get { return theme; } }
        private readonly System.Windows.Threading.DispatcherTimer dayTimer;
        private DateTime lastDay = DateTime.Today;
        private T Find<T>(string name) where T : FrameworkElement { return (T)Window.FindName(name); }
        private TextBlock Text(string name) { return Find<TextBlock>(name); }
        private Button Button(string name) { return Find<Button>(name); }
        private TextBox Input(string name) { return Find<TextBox>(name); }
        private ComboBox Select(string name) { return Find<ComboBox>(name); }
        private static string Money(decimal value) { return "¥" + value.ToString("N2", CultureInfo.GetCultureInfo("zh-CN")); }
        private static string Signed(decimal value) { return (value >= 0 ? "+" : "−") + Money(Math.Abs(value)); }
        public LedgerWindow(LedgerStore store, LedgerData data)
        {
            this.store = store; this.data = data;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DailyLedger.MainWindow.xaml")) Window = (Window)XamlReader.Load(stream);
            appearance = new AppearanceStore(store.DirectoryPath); theme = appearance.Load();
            AppTheme.Apply(Window, theme); AppTheme.ConfigureWindow(Window);
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DailyLedger.app.ico"))
                if (stream != null) { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); Window.Icon = bitmap; }
            Find<DatePicker>("EntryDate").SelectedDate = DateTime.Today;
            Find<DatePicker>("EntryDate").DisplayDateEnd = DateTime.Today;
            Select("FilterType").ItemsSource = new[] { "全部收支", "仅支出", "仅收入" }; Select("FilterType").SelectedIndex = 0;
            Select("CategoryKind").ItemsSource = new[] { "支出大类", "收入大类" }; Select("CategoryKind").SelectedIndex = 0;
            Select("ThemeMode").ItemsSource = new[] { "浅色", "深色" }; UpdateThemeControls();
            WireEvents();
            EnsureBudget();
            RefreshCategories();
            SetKind("expense", null);
            Refresh();
            if (appearance.LoadNotice != null) Text("StatusText").Text = appearance.LoadNotice;
            dayTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            dayTimer.Tick += (sender, args) => {
                if (DateTime.Today != lastDay)
                {
                    bool wasCurrent = LedgerLogic.MonthKey(month) == LedgerLogic.MonthKey(lastDay);
                    if (wasCurrent) month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                    var picker = Find<DatePicker>("EntryDate");
                    if (editId == null && picker.SelectedDate == lastDay) picker.SelectedDate = DateTime.Today;
                    picker.DisplayDateEnd = DateTime.Today; lastDay = DateTime.Today; EnsureBudget(); Refresh();
                }
            };
            dayTimer.Start();
            Window.Closing += (sender, args) => {
                if (HasDraft() && LedgerDialogs.Show(Window, "记账框中还有未保存的内容。确定关闭吗？", "还有未保存的账目", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) args.Cancel = true;
            };
            Window.Closed += (sender, args) => dayTimer.Stop();
            Window.PreviewKeyDown += (sender, args) => {
                if (args.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && page == "home") { SaveEntry(); args.Handled = true; }
            };
        }
        private void WireEvents()
        {
            Button("NavHome").Click += (s, e) => ShowPage("home");
            Button("NavLedger").Click += (s, e) => ShowPage("ledger");
            Button("NavSettings").Click += (s, e) => ShowPage("settings");
            Button("ToggleTheme").Click += (s, e) => ChangeTheme(theme == "dark" ? "light" : "dark");
            Select("ThemeMode").SelectionChanged += (s, e) => { if (!updatingThemeControls && Select("ThemeMode").SelectedIndex >= 0) ChangeTheme(Select("ThemeMode").SelectedIndex == 1 ? "dark" : "light"); };
            Button("OpenAllEntries").Click += (s, e) => ShowPage("ledger");
            Button("PreviousMonth").Click += (s, e) => ChangeMonth(month.AddMonths(-1));
            Button("NextMonth").Click += (s, e) => ChangeMonth(month.AddMonths(1));
            Button("CurrentMonth").Click += (s, e) => ChangeMonth(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
            Button("SetBudgetHome").Click += (s, e) => SetBudget();
            Button("SetBudgetSettings").Click += (s, e) => SetBudget();
            Button("ExpenseType").Click += (s, e) => SetKind("expense", null);
            Button("IncomeType").Click += (s, e) => SetKind("income", null);
            Button("SaveEntry").Click += (s, e) => SaveEntry();
            Button("CancelEdit").Click += (s, e) => { if (DiscardDraft()) ResetForm(); };
            Select("FilterType").SelectionChanged += (s, e) => { if (!refreshing) { PopulateFilterCategories(); RefreshLedger(); } };
            Select("FilterCategory").SelectionChanged += (s, e) => { if (!refreshing) RefreshLedger(); };
            Input("SearchText").TextChanged += (s, e) => { if (!refreshing) RefreshLedger(); };
            Button("EditEntry").Click += (s, e) => EditSelected();
            Button("DeleteEntry").Click += (s, e) => DeleteSelected();
            Find<DataGrid>("EntriesGrid").MouseDoubleClick += (s, e) => EditSelected();
            Button("ExportMonth").Click += (s, e) => ExportMonth();
            Select("CategoryKind").SelectionChanged += (s, e) => { if (!refreshing) RefreshManageCategories(); };
            Select("ManageCategory").SelectionChanged += (s, e) => {
                var category = Select("ManageCategory").SelectedItem as Category;
                Button("ToggleCategory").Content = category != null && !category.Active ? "启用" : "停用";
            };
            Button("AddCategory").Click += (s, e) => AddCategory();
            Button("RenameCategory").Click += (s, e) => RenameCategory();
            Button("ToggleCategory").Click += (s, e) => ToggleCategory();
            Button("ManualBackup").Click += (s, e) => Backup();
            Button("RestoreBackup").Click += (s, e) => Restore();
            Button("OpenBackupFolder").Click += (s, e) => Run(() => { Directory.CreateDirectory(store.BackupPath); OpenFolder(store.BackupPath); });
            Button("OpenDataFolder").Click += (s, e) => Run(() => OpenFolder(store.DirectoryPath));
        }
        private void UpdateThemeControls()
        {
            updatingThemeControls = true;
            try
            {
                Select("ThemeMode").SelectedIndex = theme == "dark" ? 1 : 0;
                Button("ToggleTheme").Content = theme == "dark" ? "切换浅色" : "切换深色";
            }
            finally { updatingThemeControls = false; }
        }
        internal void ChangeTheme(string value)
        {
            if (value == theme) { UpdateThemeControls(); return; }
            try { appearance.Save(value); }
            catch (Exception error)
            {
                UpdateThemeControls();
                LedgerDialogs.Show(Window, "未能保存外观选择：\n" + error.Message, "外观设置未保存", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            AppTheme.Apply(Window, value); theme = value; UpdateThemeControls();
            Text("StatusText").Text = "已切换至" + (value == "dark" ? "深色" : "浅色") + "模式 · 下次打开会保持此选择";
        }
        private void Run(Action action)
        {
            try { action(); }
            catch (Exception error) { LedgerDialogs.Show(Window, "操作未完成，原有已保存数据仍保留。\n\n" + error.Message, "日常账本", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        private bool Commit(Action<LedgerData> mutate)
        {
            try
            {
                var next = store.Clone(data); mutate(next); store.Save(next); data = next;
                Text("StatusText").Text = "已保存至本机 · " + DateTime.Now.ToString("HH:mm:ss") + "    数据位置：" + store.DirectoryPath;
                return true;
            }
            catch (Exception error) { LedgerDialogs.Show(Window, "保存失败，本次更改尚未写入。\n\n" + error.Message, "保存未完成", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        }
        private void EnsureBudget()
        {
            if (LedgerLogic.BudgetFor(data, month).HasValue) return;
            var next = store.Clone(data);
            if (LedgerLogic.InheritBudget(next, month)) { store.Save(next); data = next; }
        }
        private void ChangeMonth(DateTime value)
        {
            if (value.Year < 1900 || value > new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)) return;
            month = value; EnsureBudget(); Refresh();
        }
        private void ShowPage(string value)
        {
            page = value;
            Find<ScrollViewer>("HomeView").Visibility = value == "home" ? Visibility.Visible : Visibility.Collapsed;
            Find<Grid>("LedgerView").Visibility = value == "ledger" ? Visibility.Visible : Visibility.Collapsed;
            Find<ScrollViewer>("SettingsView").Visibility = value == "settings" ? Visibility.Visible : Visibility.Collapsed;
            foreach (string name in new[] { "NavHome", "NavLedger", "NavSettings" })
            {
                bool selected = (name == "NavHome" && value == "home") || (name == "NavLedger" && value == "ledger") || (name == "NavSettings" && value == "settings");
                if (selected) AppTheme.Bind(Button(name), Control.BackgroundProperty, "Surface"); else Button(name).Background = Brushes.Transparent;
            }
            Text("PageTitle").Text = value == "home" ? "本月概览" : value == "ledger" ? "每日账单" : "设置与备份";
            Text("PageSubtitle").Text = value == "home" ? (month.Month == DateTime.Today.Month && month.Year == DateTime.Today.Year ? "截至今天 · " + DateTime.Today.ToString("M 月 d 日") : "历史月份 · 整月汇总") : value == "ledger" ? "按天整理每一笔支出和收入。双击账目可以修改。" : "安排预算，管理分类，让账本安心留在本机。";
        }
        private void Refresh()
        {
            Text("MonthLabel").Text = month.ToString("yyyy 年 M 月");
            Button("PreviousMonth").IsEnabled = month.Year > 1900 || month.Month > 1;
            Button("NextMonth").IsEnabled = month < new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var summary = LedgerLogic.Summarize(data, month, DateTime.Today);
            Text("SpentValue").Text = Money(summary.Spent);
            Text("AverageValue").Text = Money(summary.Average);
            Text("AverageHint").Text = summary.IsCurrent ? "按 1 日至今天计算，共 " + summary.ElapsedDays + " 天" : "按整月计算，共 " + summary.ElapsedDays + " 天";
            Text("RemainingLabel").Text = summary.Remaining.HasValue && summary.Remaining < 0 ? "已超预算" : "预算还剩";
            Text("RemainingValue").Text = summary.Remaining.HasValue ? Money(Math.Abs(summary.Remaining.Value)) : "待设置";
            AppTheme.Bind(Text("RemainingValue"), TextBlock.ForegroundProperty, summary.Remaining.HasValue && summary.Remaining < 0 ? "Danger" : "Ink");
            if (!summary.Budget.HasValue) Text("FutureValue").Text = "设置预算后，即可计算每天可用金额";
            else if (!summary.IsCurrent) Text("FutureValue").Text = summary.Remaining >= 0 ? "该月预算节余" : "该月超出预算的金额";
            else if (summary.Remaining < 0) Text("FutureValue").Text = "本月预算已用完，可调整后续安排";
            else if (summary.RemainingDays == 0) Text("FutureValue").Text = "今天是本月最后一天";
            else Text("FutureValue").Text = "明日起 " + summary.RemainingDays + " 天 · 每天可用 " + Money(summary.Remaining.Value / summary.RemainingDays);
            Text("CompareValue").Text = Comparison(summary);
            string budgetLabel = summary.Budget.HasValue ? Money(summary.Budget.Value) : "尚未设置";
            Text("BudgetUsage").Text = summary.Budget.HasValue ? "月预算 " + budgetLabel + (summary.Budget > 0 ? " · 已用 " + (summary.Spent / summary.Budget.Value).ToString("P1") : " · 预算为零") : "尚未设置本月预算";
            Text("MonthElapsed").Text = summary.IsCurrent ? "本月已过 " + summary.ElapsedDays + " / " + DateTime.DaysInMonth(month.Year, month.Month) + " 天" : "该月份已结束";
            Find<ProgressBar>("BudgetProgress").Value = !summary.Budget.HasValue ? 0 : summary.Budget == 0 ? (summary.Spent > 0 ? 100 : 0) : (double)Math.Min(100m, summary.Spent / summary.Budget.Value * 100m);
            AppTheme.Bind(Find<ProgressBar>("BudgetProgress"), Control.ForegroundProperty, summary.Remaining < 0 ? "Danger" : "Accent");
            Button("SetBudgetHome").Content = summary.Budget.HasValue ? "修改预算" : "设置预算";
            Text("IncomeSummary").Text = "本月收入 " + Money(summary.Income) + "     收支结余 " + Signed(summary.Net);
            Text("LedgerMonthSummary").Text = "支出 " + Money(summary.Spent) + "    收入 " + Money(summary.Income) + "    收支结余 " + Signed(summary.Net);
            Text("SettingsBudget").Text = month.ToString("yyyy 年 M 月") + "  ·  " + budgetLabel;
            Text("DataLocation").Text = "数据位置：" + store.DirectoryPath + "\n请保留整个“数据”文件夹；备份可复制到其他磁盘。";
            RefreshBars(summary); RefreshRecent(); RefreshLedger(); ShowPage(page);
        }
        private string Comparison(MonthSummary summary)
        {
            if (!summary.HasPreviousEntries) return summary.IsCurrent ? "暂无上月同期记录" : "暂无上月记录";
            string label = summary.IsCurrent ? "较上月 1—" + summary.PreviousCutoffDay + " 日" : "较上月整月";
            decimal difference = summary.Spent - summary.PreviousSpent;
            if (difference == 0) return label + "持平";
            string result = label + (difference > 0 ? "多 " : "少 ") + Money(Math.Abs(difference));
            return result + (summary.PreviousSpent > 0 ? "（" + (difference > 0 ? "+" : "−") + (Math.Abs(difference) / summary.PreviousSpent).ToString("P1") + "）" : "（上期支出为零）");
        }
        private List<Entry> MonthEntries()
        {
            string prefix = LedgerLogic.MonthKey(month) + "-";
            return data.Entries.Where(x => x.Date.StartsWith(prefix, StringComparison.Ordinal)).OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt).ToList();
        }
        private TextBlock Label(string value, double size, string colorKey)
        {
            var label = new TextBlock { Text = value, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            AppTheme.Bind(label, TextBlock.ForegroundProperty, colorKey); return label;
        }
        private void RefreshBars(MonthSummary summary)
        {
            var panel = Find<StackPanel>("CategoryBars"); panel.Children.Clear();
            var groups = MonthEntries().Where(x => x.Kind == "expense").GroupBy(x => x.CategoryId).Select(g => new { Id = g.Key, Amount = g.Sum(x => x.Amount) }).OrderByDescending(x => x.Amount).ToList();
            if (groups.Count == 0) { panel.Children.Add(Label("还没有支出。记下第一笔，看看钱花在哪里。", 12, "Muted")); return; }
            foreach (var group in groups)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                var name = Label(LedgerLogic.CategoryName(data, group.Id), 12, "Ink"); name.Margin = new Thickness(0, 0, 10, 0); row.Children.Add(name);
                var track = new ProgressBar { Value = summary.Spent > 0 ? (double)(group.Amount / summary.Spent * 100m) : 0, Height = 7, Maximum = 100, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
                AppTheme.Bind(track, Control.BackgroundProperty, "ProgressTrack"); AppTheme.Bind(track, Control.ForegroundProperty, "Accent");
                Grid.SetColumn(track, 1); row.Children.Add(track);
                var amount = Label(Money(group.Amount), 12, "Ink"); amount.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(amount, 2); row.Children.Add(amount);
                panel.Children.Add(row);
            }
        }
        private void RefreshRecent()
        {
            var panel = Find<StackPanel>("RecentDays"); panel.Children.Clear();
            var days = MonthEntries().GroupBy(x => x.Date).Take(5).ToList();
            if (days.Count == 0) { panel.Children.Add(Label("这个月还没有账单。右侧可以直接记一笔。", 12, "Muted")); return; }
            foreach (var day in days)
            {
                string text = LedgerLogic.ParseDate(day.Key).ToString("M 月 d 日") + "   ·   支出 " + Money(day.Where(x => x.Kind == "expense").Sum(x => x.Amount));
                decimal income = day.Where(x => x.Kind == "income").Sum(x => x.Amount);
                if (income > 0) text += "  /  收入 " + Money(income);
                var expand = new Expander { Header = text, IsExpanded = day.Key == LedgerLogic.DateKey(DateTime.Today), FontSize = 12, Margin = new Thickness(0, 0, 0, 9) };
                var rows = new StackPanel { Margin = new Thickness(22, 5, 0, 0) };
                foreach (var entry in day)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5), LastChildFill = true };
                    var amount = Label((entry.Kind == "expense" ? "−" : "+") + Money(entry.Amount), 12, "Ink"); amount.Margin = new Thickness(12, 0, 0, 0); DockPanel.SetDock(amount, Dock.Right); row.Children.Add(amount);
                    row.Children.Add(Label(LedgerLogic.CategoryName(data, entry.CategoryId) + (String.IsNullOrWhiteSpace(entry.Note) ? "" : " · " + entry.Note), 12, "Muted")); rows.Children.Add(row);
                }
                expand.Content = rows; panel.Children.Add(expand);
            }
        }
        private void PopulateFilterCategories()
        {
            refreshing = true;
            string previous = (Select("FilterCategory").SelectedItem as Category ?? new Category()).Id;
            string kind = Select("FilterType").SelectedIndex == 1 ? "expense" : Select("FilterType").SelectedIndex == 2 ? "income" : "";
            var list = new List<Category> { new Category { Id = "", Name = "全部大类", Active = true } };
            list.AddRange(data.Categories.Where(x => kind == "" || x.Kind == kind));
            Select("FilterCategory").ItemsSource = list;
            Select("FilterCategory").SelectedItem = list.FirstOrDefault(x => x.Id == previous) ?? list[0];
            refreshing = false;
        }
        private void RefreshLedger()
        {
            if (refreshing) return;
            var all = MonthEntries();
            IEnumerable<Entry> filtered = all;
            int type = Select("FilterType").SelectedIndex;
            if (type == 1 || type == 2) filtered = filtered.Where(x => x.Kind == (type == 1 ? "expense" : "income"));
            var category = Select("FilterCategory").SelectedItem as Category;
            if (category != null && !String.IsNullOrEmpty(category.Id)) filtered = filtered.Where(x => x.CategoryId == category.Id);
            string query = Input("SearchText").Text.Trim();
            Text("SearchHint").Visibility = Input("SearchText").Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (query.Length > 0) filtered = filtered.Where(x => (x.Note + " " + LedgerLogic.CategoryName(data, x.CategoryId) + " " + x.Date).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            var entries = filtered.ToList();
            var rows = new List<EntryRow>();
            foreach (var group in entries.GroupBy(x => x.Date))
            {
                string heading = LedgerLogic.ParseDate(group.Key).ToString("M 月 d 日 dddd") + "    支出 " + Money(group.Where(x => x.Kind == "expense").Sum(x => x.Amount)) + "    收入 " + Money(group.Where(x => x.Kind == "income").Sum(x => x.Amount));
                rows.AddRange(group.Select(x => new EntryRow { Id = x.Id, Date = x.Date, DayGroup = heading, KindLabel = x.Kind == "expense" ? "支出" : "收入", CategoryName = LedgerLogic.CategoryName(data, x.CategoryId), AmountLabel = (x.Kind == "expense" ? "−" : "+") + x.Amount.ToString("N2"), Note = x.Note }));
            }
            var view = new ListCollectionView(rows); view.GroupDescriptions.Add(new PropertyGroupDescription("DayGroup"));
            Find<DataGrid>("EntriesGrid").ItemsSource = view;
            Text("LedgerEmpty").Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Text("LedgerEmpty").Text = all.Count == 0 ? "这个月还没有账目，去首页记下第一笔吧。" : "没有符合筛选条件的账目。";
            Text("FilterSummary").Text = "当前显示 " + entries.Count + " 笔 · 支出 " + Money(entries.Where(x => x.Kind == "expense").Sum(x => x.Amount)) + " · 收入 " + Money(entries.Where(x => x.Kind == "income").Sum(x => x.Amount));
        }
        private void RefreshCategories()
        {
            string selected = (Select("EntryCategory").SelectedItem as Category ?? new Category()).Id;
            SetKind(entryKind, selected); PopulateFilterCategories(); RefreshManageCategories();
        }
        private void RefreshManageCategories()
        {
            string id = (Select("ManageCategory").SelectedItem as Category ?? new Category()).Id;
            string kind = Select("CategoryKind").SelectedIndex == 1 ? "income" : "expense";
            var list = data.Categories.Where(x => x.Kind == kind).ToList();
            Select("ManageCategory").ItemsSource = list;
            Select("ManageCategory").SelectedItem = list.FirstOrDefault(x => x.Id == id) ?? list.FirstOrDefault();
        }
        private void SetKind(string kind, string categoryId)
        {
            if (kind == entryKind && categoryId == null) categoryId = (Select("EntryCategory").SelectedItem as Category ?? new Category()).Id;
            entryKind = kind;
            AppTheme.Bind(Button("ExpenseType"), Control.BackgroundProperty, kind == "expense" ? "AccentSoft" : "ButtonBackground");
            AppTheme.Bind(Button("IncomeType"), Control.BackgroundProperty, kind == "income" ? "AccentSoft" : "ButtonBackground");
            Button("ExpenseType").FontWeight = kind == "expense" ? FontWeights.Medium : FontWeights.Normal;
            Button("IncomeType").FontWeight = kind == "income" ? FontWeights.Medium : FontWeights.Normal;
            Find<StackPanel>("EntryNotePanel").Visibility = kind == "expense" ? Visibility.Visible : Visibility.Collapsed;
            var categories = data.Categories.Where(x => x.Kind == kind && (x.Active || (editId != null && x.Id == categoryId))).ToList();
            Select("EntryCategory").ItemsSource = categories;
            Select("EntryCategory").SelectedItem = categories.FirstOrDefault(x => x.Id == categoryId) ?? categories.FirstOrDefault();
            Button("SaveEntry").Content = editId != null ? "保存修改" : kind == "expense" ? "保存这笔支出" : "保存这笔收入";
        }
        private bool HasDraft() { return editId != null || !String.IsNullOrWhiteSpace(Input("EntryAmount").Text) || !String.IsNullOrWhiteSpace(Input("EntryNote").Text); }
        private bool DiscardDraft() { return !HasDraft() || LedgerDialogs.Show(Window, "放弃记账框中尚未保存的内容吗？", "未保存的账目", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes; }
        private void ResetForm()
        {
            editId = null; Input("EntryAmount").Clear(); Input("EntryNote").Clear(); Find<DatePicker>("EntryDate").SelectedDate = DateTime.Today;
            Text("EntryTitle").Text = "记一笔"; Button("CancelEdit").Visibility = Visibility.Collapsed; Text("EntryFeedback").Text = ""; SetKind(entryKind, null);
        }
        private void SaveEntry()
        {
            decimal amount;
            if (!LedgerLogic.TryMoney(Input("EntryAmount").Text, false, out amount)) { Feedback("请输入大于零的金额，最多两位小数，最高 1 亿元。", true); Input("EntryAmount").Focus(); return; }
            var category = Select("EntryCategory").SelectedItem as Category;
            if (category == null) { Feedback("请先选择大类。", true); return; }
            var date = Find<DatePicker>("EntryDate").SelectedDate;
            if (!date.HasValue || date.Value.Date > DateTime.Today) { Feedback("请选择今天或以前的日期。", true); return; }
            string note = entryKind == "expense" ? Input("EntryNote").Text.Trim() : "";
            string id = editId ?? Guid.NewGuid().ToString("N");
            bool editing = editId != null;
            string dateKey = LedgerLogic.DateKey(date.Value);
            if (!Commit(next => {
                var entry = next.Entries.FirstOrDefault(x => x.Id == id);
                if (entry == null) { entry = new Entry { Id = id, CreatedAt = DateTime.Now.ToString("o") }; next.Entries.Add(entry); }
                entry.Date = dateKey; entry.Kind = entryKind; entry.Amount = amount; entry.CategoryId = category.Id; entry.Note = note;
                // Save an inherited budget with the entry's month, so later changes to older budgets do not affect it.
                LedgerLogic.InheritBudget(next, date.Value);
            })) return;
            month = new DateTime(date.Value.Year, date.Value.Month, 1);
            ResetForm(); RefreshCategories(); Refresh();
            Feedback((editing ? "已修改：" : "已保存：") + date.Value.ToString("M 月 d 日") + " · " + category.Name + " · " + Money(amount), false);
            Input("EntryAmount").Focus();
        }
        private void Feedback(string value, bool error) { Text("EntryFeedback").Text = value; AppTheme.Bind(Text("EntryFeedback"), TextBlock.ForegroundProperty, error ? "Danger" : "Accent"); }
        private EntryRow SelectedRow() { return Find<DataGrid>("EntriesGrid").SelectedItem as EntryRow; }
        private void EditSelected()
        {
            var row = SelectedRow();
            if (row == null) { Text("StatusText").Text = "请先选中一笔账目，再点击修改。"; return; }
            if (!DiscardDraft()) return;
            var entry = data.Entries.First(x => x.Id == row.Id);
            editId = entry.Id; SetKind(entry.Kind, entry.CategoryId);
            Input("EntryAmount").Text = entry.Amount.ToString("0.00", CultureInfo.InvariantCulture);
            Input("EntryNote").Text = entry.Note; Find<DatePicker>("EntryDate").SelectedDate = LedgerLogic.ParseDate(entry.Date);
            Text("EntryTitle").Text = "修改账目"; Text("EntryFeedback").Text = "修改后点击“保存修改”。"; Button("CancelEdit").Visibility = Visibility.Visible;
            ShowPage("home"); Find<ScrollViewer>("HomeView").ScrollToTop(); Input("EntryAmount").Focus(); Input("EntryAmount").SelectAll();
        }
        private void DeleteSelected()
        {
            var row = SelectedRow();
            if (row == null) { Text("StatusText").Text = "请先选中一笔账目，再点击删除。"; return; }
            var entry = data.Entries.First(x => x.Id == row.Id);
            if (LedgerDialogs.Show(Window, "删除这笔账目？\n\n" + entry.Date + " · " + LedgerLogic.CategoryName(data, entry.CategoryId) + " · " + Money(entry.Amount) + "\n\n删除前的账本会保留在自动备份中。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (Commit(next => next.Entries.RemoveAll(x => x.Id == row.Id))) { if (editId == row.Id) ResetForm(); Refresh(); }
        }
        private void SetBudget()
        {
            decimal? old = LedgerLogic.BudgetFor(data, month);
            string result = Prompt(month.ToString("yyyy 年 M 月") + "的预算", "输入本月可支出的总金额。收入不会自动增加这项预算。", old.HasValue ? old.Value.ToString("0.00", CultureInfo.InvariantCulture) : "", value => {
                decimal amount; return LedgerLogic.TryMoney(value, true, out amount) ? null : "请输入 0 至 1 亿元的金额，最多两位小数。";
            });
            if (result == null) return;
            decimal budget; LedgerLogic.TryMoney(result, true, out budget);
            if (Commit(next => next.Budgets[LedgerLogic.MonthKey(month)] = budget)) Refresh();
        }
        private void AddCategory()
        {
            string kind = Select("CategoryKind").SelectedIndex == 1 ? "income" : "expense";
            string name = Prompt("新增" + (kind == "expense" ? "支出" : "收入") + "大类", "输入大类名称，最多 30 个字符。", "", value => ValidateCategoryName(value, kind, null));
            if (name == null) return;
            if (Commit(next => next.Categories.Add(new Category { Id = Guid.NewGuid().ToString("N"), Name = name.Trim(), Kind = kind, Active = true }))) { RefreshCategories(); Refresh(); }
        }
        private string ValidateCategoryName(string value, string kind, string ignoredId)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Trim().Length > 30) return "名称需要 1 至 30 个字符。";
            if (value.Any(Char.IsControl)) return "名称不能含有换行或控制字符。";
            return data.Categories.Any(x => x.Kind == kind && x.Id != ignoredId && String.Equals(x.Name, value.Trim(), StringComparison.OrdinalIgnoreCase)) ? "这个名称已存在（含已停用的大类）。" : null;
        }
        private void RenameCategory()
        {
            var category = Select("ManageCategory").SelectedItem as Category; if (category == null) return;
            string name = Prompt("修改大类名称", "使用这个大类的历史账目也会显示新名称。", category.Name, value => ValidateCategoryName(value, category.Kind, category.Id));
            if (name == null) return;
            if (Commit(next => next.Categories.First(x => x.Id == category.Id).Name = name.Trim())) { RefreshCategories(); Refresh(); }
        }
        private void ToggleCategory()
        {
            var category = Select("ManageCategory").SelectedItem as Category; if (category == null) return;
            if (category.Active && data.Categories.Count(x => x.Kind == category.Kind && x.Active) <= 1) { LedgerDialogs.Show(Window, "每种收支类型至少保留一个启用的大类。", "保留一个大类", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (Commit(next => next.Categories.First(x => x.Id == category.Id).Active = !category.Active)) { RefreshCategories(); Refresh(); }
        }
        private string Prompt(string title, string hint, string initial, Func<string, string> validate)
        {
            var dialog = LedgerDialogs.Create(Window, title, 450);
            var panel = new StackPanel { Margin = new Thickness(24) };
            var heading = Label(title, 21, "Ink"); heading.Margin = new Thickness(0, 0, 0, 12); panel.Children.Add(heading);
            var hintLabel = Label(hint, 12, "Muted"); hintLabel.Margin = new Thickness(0, 0, 0, 16); panel.Children.Add(hintLabel);
            var input = new TextBox { Text = initial, MaxLength = 50 }; panel.Children.Add(input);
            var error = Label("", 12, "Danger"); error.Margin = new Thickness(0, 8, 0, 12); panel.Children.Add(error);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) }; var save = new Button { Content = "保存", IsDefault = true, Style = (Style)Window.Resources["Primary"] };
            save.Click += (s, e) => { string message = validate(input.Text); if (message != null) { error.Text = message; return; } dialog.DialogResult = true; };
            actions.Children.Add(cancel); actions.Children.Add(save); panel.Children.Add(actions); dialog.Content = panel;
            dialog.Loaded += (s, e) => { input.Focus(); input.SelectAll(); };
            return dialog.ShowDialog() == true ? input.Text : null;
        }
        private void Backup()
        {
            var dialog = new SaveFileDialog { Title = "备份全部账本数据", Filter = "账本备份 (*.json)|*.json", FileName = "日常账本备份-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json", DefaultExt = ".json", AddExtension = true };
            if (dialog.ShowDialog(Window) != true) return;
            Run(() => { if (IsInternalPath(dialog.FileName)) throw new InvalidOperationException("请选择“数据”文件夹之外的位置，避免覆盖正在使用的账本。" ); store.ExportBackup(data, dialog.FileName); Text("StatusText").Text = "完整备份已保存：" + dialog.FileName; LedgerDialogs.Show(Window, "备份已保存。\n\n" + dialog.FileName, "备份完成", MessageBoxButton.OK, MessageBoxImage.Information); });
        }
        private bool IsInternalPath(string path)
        {
            return Path.GetFullPath(path).StartsWith(Path.GetFullPath(store.DirectoryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        private void Restore()
        {
            if (!DiscardDraft()) return;
            var dialog = new OpenFileDialog { Title = "选择完整账本备份", Filter = "账本备份 (*.json)|*.json", InitialDirectory = Directory.Exists(store.BackupPath) ? store.BackupPath : store.DirectoryPath };
            if (dialog.ShowDialog(Window) != true) return;
            Run(() => {
                var restored = store.ReadBackup(dialog.FileName);
                string description = "备份包含 " + restored.Entries.Count + " 笔账目、" + restored.Budgets.Count + " 个月的预算。\n\n恢复将替换当前账本，不会与现有账目合并。当前账本会先保留在自动备份中。\n\n确定恢复吗？";
                if (LedgerDialogs.Show(Window, description, "确认恢复备份", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                store.Save(restored); data = restored; ResetForm(); EnsureBudget(); RefreshCategories(); Refresh(); Text("StatusText").Text = "已从备份恢复 · " + DateTime.Now.ToString("HH:mm:ss");
            });
        }
        private void ExportMonth()
        {
            var dialog = new SaveFileDialog { Title = "导出本月全部账目", Filter = "Excel 可打开的表格 (*.csv)|*.csv", FileName = "日常账本-" + LedgerLogic.MonthKey(month) + ".csv", DefaultExt = ".csv", AddExtension = true };
            if (dialog.ShowDialog(Window) != true) return;
            Run(() => { if (IsInternalPath(dialog.FileName)) throw new InvalidOperationException("请选择数据文件夹之外的位置。"); store.ExportCsv(data, MonthEntries(), dialog.FileName); Text("StatusText").Text = "已导出本月全部账目（不受筛选影响）：" + dialog.FileName; LedgerDialogs.Show(Window, "已导出本月全部账目，可用 Excel 打开。\n\n" + dialog.FileName, "导出完成", MessageBoxButton.OK, MessageBoxImage.Information); });
        }
        private static void OpenFolder(string path) { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
    }
}
