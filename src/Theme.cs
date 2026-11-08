using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DailyLedger
{
    public sealed class AppearancePreferences
    {
        public int Version { get; set; }
        public string Theme { get; set; }
        public AppearancePreferences() { Version = 1; Theme = "light"; }
    }

    // Appearance lives outside the ledger so changing colors never rewrites financial records.
    public sealed class AppearanceStore
    {
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 16384 };
        public string FilePath { get; private set; }
        public string LoadNotice { get; private set; }
        public AppearanceStore(string directory) { FilePath = Path.Combine(directory, "外观设置.json"); }
        public string Load()
        {
            LoadNotice = null;
            if (!File.Exists(FilePath)) return "light";
            try
            {
                if (new FileInfo(FilePath).Length > 16384) throw new InvalidDataException();
                var preferences = serializer.Deserialize<AppearancePreferences>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (preferences == null || preferences.Version != 1 || !AppTheme.IsValid(preferences.Theme)) throw new InvalidDataException();
                return preferences.Theme;
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException)) throw;
                LoadNotice = "外观设置未能读取，暂用浅色；可重新选择主题。";
                return "light";
            }
        }
        public void Save(string theme)
        {
            if (!AppTheme.IsValid(theme)) throw new ArgumentException("不支持的外观模式。", "theme");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(serializer.Serialize(new AppearancePreferences { Theme = theme }));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null); else File.Move(temporary, FilePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public static class AppTheme
    {
        // Each semantic color has a light and dark counterpart. Dynamic resources update open views in place.
        private static readonly string[,] Colors = {
            { "AppBackground", "#F4F6F5", "#151C19" },
            { "SidebarBackground", "#E7EEEA", "#1A241F" },
            { "Surface", "#FFFFFF", "#222E27" },
            { "InputBackground", "#FFFFFF", "#19231E" },
            { "Ink", "#233731", "#EAF2ED" },
            { "Muted", "#5E7167", "#B6C8BC" },
            { "Accent", "#21685C", "#93D2B8" },
            { "OnAccent", "#FFFFFF", "#132F24" },
            { "AccentSoft", "#E1EDE5", "#354F41" },
            { "BudgetSurface", "#E3F0E9", "#283F32" },
            { "ButtonBackground", "#EDF2EF", "#2D3E33" },
            { "HoverBackground", "#E4EDE7", "#3A5042" },
            { "Border", "#D5DFD8", "#48614F" },
            { "Danger", "#A23D32", "#FFB2A4" },
            { "ProgressTrack", "#E1E8E3", "#384C3E" },
            { "AlternateRow", "#F7F9F8", "#26362C" },
            { "GridLine", "#E9EEEA", "#3B4E41" },
            { "GroupBackground", "#EAF0EC", "#2E4436" },
            { "HeaderInk", "#52685C", "#D0E1D5" },
            { "SelectionBackground", "#D7EBDF", "#3B5B48" },
            { "SelectionInk", "#163D2B", "#F3FAF5" }
        };
        public static bool IsValid(string name) { return name == "light" || name == "dark"; }
        public static IDictionary<string, Color> Palette(string name)
        {
            if (!IsValid(name)) throw new ArgumentException("不支持的外观模式。", "name");
            var result = new Dictionary<string, Color>();
            int column = name == "dark" ? 2 : 1;
            for (int i = 0; i < Colors.GetLength(0); i++) result.Add(Colors[i, 0], (Color)ColorConverter.ConvertFromString(Colors[i, column]));
            return result;
        }
        public static void Apply(Window window, string name)
        {
            foreach (var color in Palette(name))
            {
                var brush = new SolidColorBrush(color.Value); brush.Freeze(); window.Resources[color.Key] = brush;
            }
            window.Resources["ThemeName"] = name;
            SetTitleBar(window);
        }
        public static void Bind(FrameworkElement element, DependencyProperty property, string key) { element.SetResourceReference(property, key); }
        public static void ConfigureWindow(Window window)
        {
            Bind(window, Control.BackgroundProperty, "AppBackground");
            Bind(window, Control.ForegroundProperty, "Ink");
            window.SourceInitialized += (sender, args) => SetTitleBar(window);
        }
        // Microsoft's documented DWM attributes affect only this application's window frame.
        // https://learn.microsoft.com/windows/apps/desktop/modernize/ui/apply-windows-themes
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
        private static void SetTitleBar(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            try
            {
                int dark = Object.Equals(window.Resources["ThemeName"], "dark") ? 1 : 0;
                DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
                var background = ((SolidColorBrush)window.Resources["AppBackground"]).Color;
                var foreground = ((SolidColorBrush)window.Resources["Ink"]).Color;
                int caption = background.R | (background.G << 8) | (background.B << 16);
                int text = foreground.R | (foreground.G << 8) | (foreground.B << 16);
                DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
                DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }

    internal static class LedgerDialogs
    {
        internal static Window Create(Window owner, string title, double width)
        {
            var dialog = new Window {
                Owner = owner, Title = title, Width = width, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                FontFamily = owner.FontFamily, FontSize = 14, Resources = owner.Resources,
                ShowInTaskbar = false, Icon = owner.Icon
            };
            AppTheme.ConfigureWindow(dialog);
            return dialog;
        }
        internal static MessageBoxResult Show(Window owner, string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            var dialog = Create(owner, title, 480);
            var panel = new StackPanel { Margin = new Thickness(24) };
            var heading = new TextBlock { Text = title, FontSize = 21, Margin = new Thickness(0, 0, 0, 14), TextWrapping = TextWrapping.Wrap };
            AppTheme.Bind(heading, TextBlock.ForegroundProperty, "Ink"); panel.Children.Add(heading);
            var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 23 };
            AppTheme.Bind(body, TextBlock.ForegroundProperty, "Ink");
            panel.Children.Add(new ScrollViewer { Content = body, MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
            bool confirm = buttons == MessageBoxButton.YesNo;
            if (confirm)
            {
                var cancel = new Button { Content = "取消", IsCancel = true, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
                cancel.Click += (sender, args) => dialog.DialogResult = false; actions.Children.Add(cancel);
            }
            var accept = new Button { Content = confirm ? "确定" : "知道了", IsDefault = !confirm, IsCancel = !confirm, Style = (Style)owner.Resources["Primary"] };
            accept.Click += (sender, args) => dialog.DialogResult = true; actions.Children.Add(accept);
            panel.Children.Add(actions); dialog.Content = panel;
            return dialog.ShowDialog() == true ? (confirm ? MessageBoxResult.Yes : MessageBoxResult.OK) : (confirm ? MessageBoxResult.No : MessageBoxResult.OK);
        }
    }
}
