using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace mini2nbrowser
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is Visibility v && v == Visibility.Visible;
    }

    public class NonZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => (value is double d && d > 0) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class SearchEngine
    {
        public string Name { get; set; } = "";
        public string UrlTemplate { get; set; } = "";
        public string IconColor { get; set; } = "#2488C8";
        public string IconText { get; set; } = "";
    }

    public class AppConfig
    {
        public bool IsDarkMode { get; set; }
        public string DefaultEngine { get; set; } = "Bing";
        public bool ProtectionEnabled { get; set; } = true;
        public bool AutoMemoryOptimize { get; set; } = true;
        public int MemoryThreshold { get; set; } = 500;
        public List<SearchEngine> SearchEngines { get; set; } = new()
        {
            new SearchEngine { Name = "Bing", UrlTemplate = "https://www.bing.com/search?q={0}", IconColor = "#008373", IconText = "B" },
            new SearchEngine { Name = "百度", UrlTemplate = "https://www.baidu.com/s?wd={0}", IconColor = "#2932E1", IconText = "百" },
            new SearchEngine { Name = "Google", UrlTemplate = "https://www.google.com/search?q={0}", IconColor = "#4285F4", IconText = "G" },
        };
    }

    public class TabInfo : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        private string _title = "新标签页";
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }
        private ImageSource? _favicon;
        public ImageSource? Favicon
        {
            get => _favicon;
            set { _favicon = value; OnPropertyChanged(); }
        }

        /// <summary>该标签页是否为无痕模式</summary>
        public bool IsIncognito { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class UserScript : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Match { get; set; } = "";
        public string Code { get; set; } = "";
        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class HistoryItem
    {
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime VisitedAt { get; set; } = DateTime.Now;
    }

    public class BookmarkItem
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class DownloadItem : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";

        private double _progress;
        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        private string _status = "下载中";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsCompleted)); }
        }

        public bool IsCompleted => _status == "已完成";

        private long _bytesReceived;
        public long BytesReceived
        {
            get => _bytesReceived;
            set { _bytesReceived = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeText)); }
        }

        private long? _totalBytes;
        public long? TotalBytes
        {
            get => _totalBytes;
            set { _totalBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeText)); OnPropertyChanged(nameof(RemainingText)); }
        }

        private double _speed;
        public double Speed
        {
            get => _speed;
            set { _speed = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpeedText)); OnPropertyChanged(nameof(RemainingText)); }
        }

        public string SizeText
        {
            get
            {
                if (TotalBytes.HasValue && TotalBytes > 0)
                    return $"{FormatSize(BytesReceived)} / {FormatSize(TotalBytes.Value)}";
                return FormatSize(BytesReceived);
            }
        }

        public string SpeedText => Speed > 0 ? FormatSize((long)Speed) + "/s" : "";

        public string RemainingText
        {
            get
            {
                if (Speed > 0 && TotalBytes.HasValue && TotalBytes > 0 && BytesReceived < TotalBytes)
                {
                    double sec = (TotalBytes.Value - BytesReceived) / Speed;
                    if (sec < 60) return $"{sec:F0}秒";
                    if (sec < 3600) return $"{sec / 60:F0}分{(sec % 60):F0}秒";
                    return $"{sec / 3600:F1}小时";
                }
                return "";
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
            return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class PasswordEntry : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Site { get; set; } = "";
        public string Username { get; set; } = "";
        public string EncryptedPassword { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        private bool _revealed;
        public bool Revealed
        {
            get => _revealed;
            set
            {
                _revealed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayPassword));
            }
        }

        public string DisplayPassword => Revealed
            ? Dpapi.Unprotect(EncryptedPassword)
            : new string('•', Math.Max(6, Dpapi.Unprotect(EncryptedPassword).Length));

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    internal static class Dpapi
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DATA_BLOB pDataIn, string? szDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved,
            IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(
            ref DATA_BLOB pDataIn, IntPtr pDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved,
            IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        public static string Protect(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return "";
            var data = Encoding.UTF8.GetBytes(plaintext);
            var inBlob = new DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
            Marshal.Copy(data, 0, inBlob.pbData, data.Length);
            var outBlob = new DATA_BLOB();
            try
            {
                if (!CryptProtectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref outBlob))
                    return Convert.ToBase64String(data);
                var result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                return Convert.ToBase64String(result);
            }
            finally
            {
                Marshal.FreeHGlobal(inBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(outBlob.pbData);
            }
        }

        public static string Unprotect(string encryptedBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64)) return "";
            try
            {
                var data = Convert.FromBase64String(encryptedBase64);
                var inBlob = new DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
                Marshal.Copy(data, 0, inBlob.pbData, data.Length);
                var outBlob = new DATA_BLOB();
                try
                {
                    if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref outBlob))
                        return "";
                    var result = new byte[outBlob.cbData];
                    Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                    return Encoding.UTF8.GetString(result);
                }
                finally
                {
                    Marshal.FreeHGlobal(inBlob.pbData);
                    if (outBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(outBlob.pbData);
                }
            }
            catch { return ""; }
        }
    }

    public partial class MainWindow : Window
    {
        private AppConfig _config = new();
        private readonly ObservableCollection<TabInfo> _tabs = new();
        private readonly Dictionary<string, Microsoft.Web.WebView2.Wpf.WebView2> _webViews = new();
        private readonly ObservableCollection<UserScript> _scripts = new();
        private readonly ObservableCollection<HistoryItem> _history = new();
        private readonly ObservableCollection<BookmarkItem> _bookmarks = new();
        private readonly ObservableCollection<DownloadItem> _downloads = new();
        private int _activeDownloads;
        private readonly ObservableCollection<PasswordEntry> _passwords = new();
        private readonly List<HistoryItem> _allHistory = new();
        private string? _editingScriptId;
        private string? _editingEngineName;
        private readonly System.Timers.Timer _memoryTimer;

        // ===== 扩展与无痕：共享 WebView2 环境，开启扩展支持 =====
        private CoreWebView2Environment? _webViewEnvironment;
        private ExtensionsManager? _extensionsManager;
        private bool _extensionsLoadedForDefaultProfile;
        private ExtensionsWindow? _extensionsWindow;

        // ===== 数据目录与 profile（多实例隔离）=====
        private readonly string _dataDir;
        private readonly string _profileName;

        // 系统托盘
        private Hardcodet.Wpf.TaskbarNotification.TaskbarIcon? _trayIcon;
        private bool _isClosingFromTray;

        private const double SidebarCollapsed = 36;
        private const double SidebarExpanded = 180;
        private const double NavMarginCollapsed = 42;
        private const double NavMarginExpanded = 186;

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        #region IsSidebarExpanded
        public static readonly DependencyProperty IsSidebarExpandedProperty =
            DependencyProperty.Register("IsSidebarExpanded", typeof(bool), typeof(MainWindow),
                new PropertyMetadata(false));

        public bool IsSidebarExpanded
        {
            get => (bool)GetValue(IsSidebarExpandedProperty);
            set => SetValue(IsSidebarExpandedProperty, value);
        }
        #endregion

        #region Win32 / DWM (圆角，无边框)
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWCP_DONOTROUND = 1;

        #endregion

        private static readonly HashSet<string> AdDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            "doubleclick.net","googlesyndication.com","googleadservices.com","google-analytics.com",
            "googletagmanager.com","googletagservices.com","adservice.google.com","adsense.com",
            "adnxs.com","adsystem.com","amazon-adsystem.com","adcolony.com","applovin.com",
            "chartbeat.com","scorecardresearch.com","quantserve.com","crashlytics.com",
            "hotjar.com","mixpanel.com","segment.com","fullstory.com","bugsnag.com",
            "facebook.net","connect.facebook.com","analytics.twitter.com","ads.linkedin.com",
            "adroll.com","outbrain.com","taboola.com","criteo.com","pubmatic.com",
            "rubiconproject.com","openx.net","casalemedia.com","moatads.com"
        };

        // 图标预加载缓存（嵌入资源，零 IO 快速切换）
        private static readonly ImageSource _lightIcon = LoadIcon("app.ico");
        private static readonly ImageSource _darkIcon = LoadIcon("app_dark.ico");

        private static ImageSource LoadIcon(string name)
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/{name}", UriKind.Absolute);
                return Application.GetResourceStream(uri) is { } s
                    ? BitmapFrame.Create(s.Stream)
                    : BitmapFrame.Create(uri);
            }
            catch { return null!; }
        }

        public MainWindow(string dataDir, string profileName)
        {
            _dataDir = dataDir;
            _profileName = profileName;
            InitializeComponent();

            // 仅同步加载最关键的配置（决定主题/搜索引擎，影响首屏渲染）
            LoadConfig();

            // 集合绑定即时完成（初始为空，开销极低）
            tabList.ItemsSource = _tabs;
            lbScripts.ItemsSource = _scripts;
            lbHistory.ItemsSource = _history;
            lbDownloads.ItemsSource = _downloads;
            lbBookmarksPopup.ItemsSource = _bookmarks;
            lbPasswords.ItemsSource = _passwords;

            ApplyTheme();
            chkDarkMode.IsChecked = _config.IsDarkMode;
            chkProtection.IsChecked = _config.ProtectionEnabled;
            lbEngines.ItemsSource = _config.SearchEngines;
            foreach (SearchEngine eng in _config.SearchEngines)
            {
                if (eng.Name == _config.DefaultEngine) { lbEngines.SelectedItem = eng; break; }
            }

            // 系统托盘初始化
            _trayIcon = Resources["TrayIcon"] as Hardcodet.Wpf.TaskbarNotification.TaskbarIcon;
            if (_trayIcon != null)
            {
                // 多实例时托盘提示区分 profile
                _trayIcon.ToolTipText = string.IsNullOrEmpty(_profileName)
                    ? "mini2n Browser v1.1.0"
                    : $"mini2n Browser v1.1.0 [{_profileName}]";
                _trayIcon.TrayLeftMouseUp += (s, e) => RestoreFromTray();
            }

            StateChanged += MainWindow_StateChanged;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            PreviewKeyDown += MainWindow_PreviewKeyDown;

            _memoryTimer = new System.Timers.Timer(30000);
            _memoryTimer.Elapsed += (s, e) => CleanMemory();
            _memoryTimer.Start();

            // 初始化扩展管理器（必须在首个标签页创建之前完成，否则扩展不会被加载）
            try
            {
                ExtensionsManager.CheckSupport();
                _extensionsManager = new ExtensionsManager(_dataDir);
                _extensionsManager.LoadConfig();
            }
            catch { _extensionsManager = null; }

            // 延迟到空闲优先级创建首个标签页，让窗口先渲染出来（毫秒级冷启动关键）
            Dispatcher.BeginInvoke(new Action(NewTab),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SetRoundedCorners(true);
            // 窗口已渲染可见后，后台异步加载非首屏数据
            await System.Threading.Tasks.Task.Yield();
            try { LoadScripts(); } catch { }
            try { LoadHistory(); } catch { }
            try { LoadBookmarks(); } catch { }
            try { LoadPasswords(); } catch { }
        }

        /// <summary>获取共享 WebView2 环境（开启扩展支持）。无痕标签也用同一环境，仅 controller 选项不同。</summary>
        private async Task<CoreWebView2Environment> GetWebViewEnvironmentAsync()
        {
            if (_webViewEnvironment != null) return _webViewEnvironment;
            var options = new CoreWebView2EnvironmentOptions
            {
                AreBrowserExtensionsEnabled = true
            };
            string userData = Path.Combine(_dataDir, "WebViewData");
            _webViewEnvironment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: userData, options: options);
            return _webViewEnvironment;
        }

        /// <summary>把已启用扩展加载到默认 profile（仅首次加载，避免重复）</summary>
        private async Task EnsureExtensionsLoadedAsync(CoreWebView2Profile profile)
        {
            if (_extensionsLoadedForDefaultProfile) return;
            _extensionsLoadedForDefaultProfile = true;
            if (_extensionsManager != null)
            {
                _extensionsManager.SetProfile(profile);
                try { await _extensionsManager.LoadAllEnabledAsync(); }
                catch { }
            }
        }

        #region 窗口控制（系统原生动画）
        /// <summary>导航栏整行拖动：双击最大化/还原，单击按下后开始拖拽；排除按钮、地址栏等可交互控件</summary>
        private void NavBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 判断鼠标是否点在可交互控件上（按钮、地址栏、TextBox等），若是则跳过 DragMove
            if (e.OriginalSource is DependencyObject src)
            {
                if (FindParent<ButtonBase>(src) != null) return;
                if (FindParent<TextBox>(src) != null) return;
                if (FindParent<ComboBox>(src) != null) return;
                if (FindParent<CheckBox>(src) != null) return;
            }

            if (e.ClickCount == 2)
            {
                // 双击标题栏 → 切换最大化/还原
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                e.Handled = true;
            }
            else if (e.ButtonState == MouseButtonState.Pressed)
            {
                // 单击按下 → 开始拖动
                // 最大化状态下拖动：先还原窗口，然后按鼠标位置将窗口"挂"到光标处，体验更自然
                if (WindowState == WindowState.Maximized)
                {
                    var ratio = e.GetPosition(this).X / ActualWidth;
                    WindowState = WindowState.Normal;
                    double w = ActualWidth;
                    double h = ActualHeight;
                    double scrW = SystemParameters.WorkArea.Width;
                    double scrH = SystemParameters.WorkArea.Height;
                    var mouseX = e.GetPosition(this).X;
                    Left = Math.Clamp(mouseX - w * ratio, 0, Math.Max(0, scrW - w));
                    Top = Math.Clamp(0, 0, Math.Max(0, scrH - h));
                }
                try { DragMove(); }
                catch { /* 边界情况下可能抛异常，忽略 */ }
                e.Handled = true;
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            UpdateMaximizeButton();
            if (rootGrid == null) return;
            if (WindowState == WindowState.Maximized)
            {
                rootGrid.Margin = new Thickness(0);
                SetRoundedCorners(false);
            }
            else if (WindowState == WindowState.Minimized)
            {
                // 最小化到托盘：隐藏窗口，进程驻留后台，WebView2 全部保活
                Hide();
                // 延迟回收内存，释放工作集
                _ = Task.Delay(500).ContinueWith(_ => Dispatcher.Invoke(CleanMemory));
            }
            else
            {
                rootGrid.Margin = new Thickness(0);
                SetRoundedCorners(true);
            }
        }

        private void UpdateMaximizeButton()
        {
            if (iconMaximize == null) return;
            iconMaximize.Data = WindowState == WindowState.Maximized
                ? Geometry.Parse("M3 0h7v7H8v3H0V3h3z")
                : Geometry.Parse("M0.5 0.5h9v9h-9z");
        }

        #region 系统托盘

        /// <summary>从托盘恢复窗口（热唤醒，毫秒级）</summary>
        public void RestoreFromTray()
        {
            // 若窗口已隐藏（最小化到托盘），先 Show 再恢复
            if (!IsVisible)
            {
                Show();
            }
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            // 用 Topmost 双重赋值强制前置（WPF 标准技巧）
            Topmost = true;
            Topmost = false;
            Focus();
        }

        /// <summary>拦截窗口关闭：点 X 不退出，最小化到托盘</summary>
        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!_isClosingFromTray)
            {
                e.Cancel = true;
                WindowState = WindowState.Minimized;
                Hide();
            }
            else
            {
                // 托盘菜单"完全退出"：真正释放资源
                _trayIcon?.Dispose();
                _memoryTimer?.Stop();
                _memoryTimer?.Dispose();
            }
        }

        /// <summary>托盘菜单：显示浏览器</summary>
        private void TrayShowWindow_Click(object sender, RoutedEventArgs e)
        {
            RestoreFromTray();
        }

        /// <summary>托盘菜单：完全退出</summary>
        private void TrayExitApp_Click(object sender, RoutedEventArgs e)
        {
            _isClosingFromTray = true;
            if (_trayIcon != null) _trayIcon.Visibility = Visibility.Collapsed;
            Close();
        }

        #endregion

        private void SetRoundedCorners(bool round)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                int preference = round ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch { }
        }
        #endregion

        #region 快捷键
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.W) { CloseCurrentTab(); e.Handled = true; }
                else if (e.Key == Key.T) { NewTab(); e.Handled = true; }
                else if (e.Key == Key.D) { ToggleBookmark(); e.Handled = true; }
            }
            // Ctrl+Shift+N：新建无痕标签页
            else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.N)
            {
                NewIncognitoTab();
                e.Handled = true;
            }
        }
        #endregion

        #region 配置 & 主题
        private string ConfigPath => Path.Combine(_dataDir, "config.json");
        private string ScriptsPath => Path.Combine(_dataDir, "scripts.json");
        private string HistoryPath => Path.Combine(_dataDir, "history.json");
        private string BookmarksPath => Path.Combine(_dataDir, "bookmarks.json");
        private string PasswordsPath => Path.Combine(_dataDir, "passwords.json");

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath));
                    if (cfg != null) _config = cfg;
                }
            }
            catch { }
        }

        private void SaveConfig()
        {
            try { File.WriteAllText(ConfigPath, JsonSerializer.Serialize(_config, JsonOpts)); }
            catch { }
        }

        private void ApplyTheme()
        {
            var uri = _config.IsDarkMode ? "DarkTheme.xaml" : "LightTheme.xaml";
            var dict = new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) };
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(dict);

            // 深浅主题切换窗口图标
            Icon = _config.IsDarkMode ? _darkIcon : _lightIcon;

            // 同步托盘图标
            if (_trayIcon != null)
                _trayIcon.IconSource = _config.IsDarkMode ? _darkIcon : _lightIcon;

            try
            {
                foreach (var wv in _webViews.Values)
                {
                    if (wv.CoreWebView2 != null)
                        wv.CoreWebView2.Profile.PreferredColorScheme = _config.IsDarkMode
                            ? CoreWebView2PreferredColorScheme.Dark
                            : CoreWebView2PreferredColorScheme.Light;
                }
            }
            catch { }

            foreach (var tab in _tabs)
            {
                if (_webViews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null)
                {
                    var url = wv.CoreWebView2.Source;
                    if (url.Contains("HomePage.html", StringComparison.OrdinalIgnoreCase))
                        wv.CoreWebView2.Navigate(GetHomeUrl());
                    else
                        wv.Reload();
                }
            }

            // 主题切换后刷新书签图标颜色
            if (tabList?.SelectedItem is TabInfo curTab &&
                _webViews.TryGetValue(curTab.Id, out var curWv) && curWv.CoreWebView2 != null)
            {
                UpdateBookmarkIcon(curWv.CoreWebView2.Source);
            }
        }

        private void ChkDarkMode_Checked(object sender, RoutedEventArgs e)
        {
            _config.IsDarkMode = true; SaveConfig(); ApplyTheme();
        }
        private void ChkDarkMode_Unchecked(object sender, RoutedEventArgs e)
        {
            _config.IsDarkMode = false; SaveConfig(); ApplyTheme();
        }
        private void ChkProtection_Checked(object sender, RoutedEventArgs e)
        {
            _config.ProtectionEnabled = true; SaveConfig();
        }
        private void ChkProtection_Unchecked(object sender, RoutedEventArgs e)
        {
            _config.ProtectionEnabled = false; SaveConfig();
        }
        #endregion

        #region 搜索引擎
        private void LbEngines_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lbEngines.SelectedItem is SearchEngine eng)
            {
                _config.DefaultEngine = eng.Name;
                SaveConfig();
                foreach (var tab in _tabs)
                {
                    if (_webViews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null)
                    {
                        if (wv.CoreWebView2.Source.Contains("HomePage.html", StringComparison.OrdinalIgnoreCase))
                            wv.CoreWebView2.Navigate(GetHomeUrl());
                    }
                }
            }
        }

        private void BtnAddEngine_Click(object sender, RoutedEventArgs e)
        {
            _editingEngineName = null;
            txtEngineEditorTitle.Text = "添加搜索引擎";
            txtEngineName.Text = "";
            txtEngineUrl.Text = "";
            engineEditorOverlay.Visibility = Visibility.Visible;
        }

        private void BtnDeleteEngine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string name)
            {
                if (name == _config.DefaultEngine) return;
                var eng = _config.SearchEngines.FirstOrDefault(x => x.Name == name);
                if (eng != null) { _config.SearchEngines.Remove(eng); SaveConfig(); }
            }
        }

        private void BtnSaveEngine_Click(object sender, RoutedEventArgs e)
        {
            var name = txtEngineName.Text.Trim();
            var url = txtEngineUrl.Text.Trim();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) return;
            if (!url.Contains("{0}")) { MessageBox.Show("URL 模板必须包含 {0}"); return; }

            var colors = new[] { "#2488C8", "#28A870", "#E84C3D", "#8B5CF6", "#F59E0B", "#EC4899", "#06B6D4" };
            var rnd = new Random();

            if (_editingEngineName != null)
            {
                var eng = _config.SearchEngines.FirstOrDefault(x => x.Name == _editingEngineName);
                if (eng != null)
                {
                    eng.Name = name; eng.UrlTemplate = url;
                }
            }
            else
            {
                if (_config.SearchEngines.Any(x => x.Name == name)) { MessageBox.Show("搜索引擎名称已存在"); return; }
                _config.SearchEngines.Add(new SearchEngine
                {
                    Name = name,
                    UrlTemplate = url,
                    IconColor = colors[rnd.Next(colors.Length)],
                    IconText = name.Length > 0 ? name[..1] : "?"
                });
            }
            SaveConfig();
            lbEngines.ItemsSource = null;
            lbEngines.ItemsSource = _config.SearchEngines;
            engineEditorOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnCancelEngine_Click(object sender, RoutedEventArgs e)
            => engineEditorOverlay.Visibility = Visibility.Collapsed;
        #endregion

        #region 智能防护
        private void SetupProtection(Microsoft.Web.WebView2.Wpf.WebView2 wv)
        {
            wv.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            wv.CoreWebView2.WebResourceRequested += (s, e) =>
            {
                if (!_config.ProtectionEnabled) return;
                try
                {
                    var uri = new Uri(e.Request.Uri);
                    if (AdDomains.Any(d => uri.Host.EndsWith(d, StringComparison.OrdinalIgnoreCase)))
                        e.Response = wv.CoreWebView2.Environment.CreateWebResourceResponse(null, 204, "No Content", "");
                }
                catch { }
            };
        }
        #endregion

        #region 内存管理
        private void CleanMemory()
        {
            if (!_config.AutoMemoryOptimize) return;
            try
            {
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                long memMB = proc.WorkingSet64 / 1024 / 1024;
                bool minimized = WindowState == WindowState.Minimized;
                // 最小化时阈值减半，更激进回收；正常时仅超阈值才回收
                long threshold = minimized ? _config.MemoryThreshold / 2 : _config.MemoryThreshold;
                if (memMB > threshold || minimized)
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false, true);
                    SetProcessWorkingSetSize(proc.Handle, -1, -1);
                }
            }
            catch { }
        }
        #endregion

        #region 收藏夹
        private void LoadBookmarks()
        {
            try
            {
                if (File.Exists(BookmarksPath))
                {
                    var list = JsonSerializer.Deserialize<List<BookmarkItem>>(File.ReadAllText(BookmarksPath));
                    if (list != null) { _bookmarks.Clear(); list.ForEach(_bookmarks.Add); }
                }
            }
            catch { }
        }

        private void SaveBookmarks()
        {
            try { File.WriteAllText(BookmarksPath, JsonSerializer.Serialize(_bookmarks.ToList(), JsonOpts)); }
            catch { }
        }

        private void UpdateBookmarkIcon(string url)
        {
            bool isBookmarked = _bookmarks.Any(b => b.Url == url);
            iconBookmark.Data = isBookmarked
                ? Geometry.Parse("M17 3H7c-1.1 0-2 .9-2 2v16l7-3 7 3V5c0-1.1-.9-2-2-2z")
                : Geometry.Parse("M17 3H7c-1.1 0-2 .9-2 2v16l7-3 7 3V5c0-1.1-.9-2-2-2zm0 15l-5-2.18L7 18V5h10v13z");
            iconBookmark.Fill = isBookmarked
                ? (Brush)FindResource("AccentBlue")
                : (Brush)FindResource("TextColor");
        }

        private void BtnBookmark_Click(object sender, RoutedEventArgs e) => ToggleBookmark();

        private void ToggleBookmark()
        {
            var tab = tabList.SelectedItem as TabInfo;
            if (tab == null || !_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 == null) return;
            var url = wv.CoreWebView2.Source;
            if (string.IsNullOrEmpty(url) || url.Contains("HomePage.html", StringComparison.OrdinalIgnoreCase)) return;

            var existing = _bookmarks.FirstOrDefault(b => b.Url == url);
            if (existing != null) { _bookmarks.Remove(existing); }
            else
            {
                _bookmarks.Insert(0, new BookmarkItem
                {
                    Title = string.IsNullOrEmpty(tab.Title) ? url : tab.Title,
                    Url = url
                });
            }
            SaveBookmarks();
            UpdateBookmarkIcon(url);
        }

        private void BtnDeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url)
            {
                var bm = _bookmarks.FirstOrDefault(b => b.Url == url);
                if (bm != null) { _bookmarks.Remove(bm); SaveBookmarks(); }
                var tab = tabList.SelectedItem as TabInfo;
                if (tab != null && _webViews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null)
                    UpdateBookmarkIcon(wv.CoreWebView2.Source);
            }
        }

        private void BookmarkItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url)
            {
                bookmarksOverlay.Visibility = Visibility.Collapsed;
                var tab = tabList.SelectedItem as TabInfo;
                if (tab != null && _webViews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null)
                    wv.CoreWebView2.Navigate(url);
            }
        }

        private void BtnBookmarksList_Click(object sender, RoutedEventArgs e)
        {
            if (bookmarksOverlay.Visibility == Visibility.Visible)
            {
                bookmarksOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                bookmarksOverlay.Opacity = 0;
                bookmarksOverlay.Visibility = Visibility.Visible;
                bookmarksOverlay.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(1, TimeSpan.FromMilliseconds(120))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }

        private void BtnCloseBookmarks_Click(object sender, RoutedEventArgs e)
            => bookmarksOverlay.Visibility = Visibility.Collapsed;

        private void BookmarksOverlay_MouseDown(object sender, MouseButtonEventArgs e)
            => bookmarksOverlay.Visibility = Visibility.Collapsed;
        #endregion

        #region 下载
        private void SetupDownloads(Microsoft.Web.WebView2.Wpf.WebView2 wv)
        {
            wv.CoreWebView2.DownloadStarting += (s, e) =>
            {
                var deferral = e.GetDeferral();
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var sfd = new Microsoft.Win32.SaveFileDialog { FileName = e.ResultFilePath };
                        if (sfd.ShowDialog() == true)
                        {
                            e.ResultFilePath = sfd.FileName;
                            var item = new DownloadItem { FileName = Path.GetFileName(sfd.FileName), FilePath = sfd.FileName };
                            _downloads.Insert(0, item);
                            // 下载开始时自动关闭浮窗，通过角标提示正在下载
                            downloadsOverlay.Visibility = Visibility.Collapsed;
                            _activeDownloads++;
                            UpdateDownloadBadge();

                            long lastBytes = 0;
                            DateTime lastTime = DateTime.Now;

                            e.DownloadOperation.BytesReceivedChanged += (s2, e2) =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    item.BytesReceived = e.DownloadOperation.BytesReceived;
                                    if (e.DownloadOperation.TotalBytesToReceive.HasValue)
                                        item.TotalBytes = (long)e.DownloadOperation.TotalBytesToReceive.Value;

                                    var now = DateTime.Now;
                                    var elapsed = (now - lastTime).TotalSeconds;
                                    if (elapsed >= 0.5)
                                    {
                                        item.Speed = (e.DownloadOperation.BytesReceived - lastBytes) / elapsed;
                                        lastBytes = e.DownloadOperation.BytesReceived;
                                        lastTime = now;
                                    }

                                    if (e.DownloadOperation.TotalBytesToReceive.HasValue &&
                                        e.DownloadOperation.TotalBytesToReceive.Value > 0)
                                        item.Progress = (double)e.DownloadOperation.BytesReceived /
                                            e.DownloadOperation.TotalBytesToReceive.Value * 100;
                                });
                            };
                            e.DownloadOperation.StateChanged += (s2, e2) =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    item.Status = e.DownloadOperation.State switch
                                    {
                                        CoreWebView2DownloadState.Completed => "已完成",
                                        CoreWebView2DownloadState.Interrupted => "已中断",
                                        _ => "下载中"
                                    };
                                    if (item.Status == "已完成" || item.Status == "已中断")
                                    {
                                        item.Speed = 0;
                                        _activeDownloads = Math.Max(0, _activeDownloads - 1);
                                        UpdateDownloadBadge();
                                        if (item.Status == "已完成")
                                            ShowDownloadToast(item);
                                    }
                                });
                            };
                        }
                        else { e.Cancel = true; }
                    }
                    finally { deferral.Complete(); }
                });
            };
        }

        /// <summary>下载完成时右下角弹出短暂通知</summary>
        private async void ShowDownloadToast(DownloadItem item)
        {
            if (downloadToast == null) return;
            toastText.Text = $"下载完成：{item.FileName}";
            toastTag = item;
            downloadToast.Visibility = Visibility.Visible;
            downloadToast.Opacity = 0;
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(250))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            downloadToast.BeginAnimation(OpacityProperty, fadeIn);
            await Task.Delay(3500);
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fadeOut.Completed += (s, e) => downloadToast.Visibility = Visibility.Collapsed;
            downloadToast.BeginAnimation(OpacityProperty, fadeOut);
        }

        private DownloadItem? toastTag;

        private void BtnOpenDownloadedFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path && File.Exists(path))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
                catch { }
            }
        }

        private void BtnOpenDownloadFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path && File.Exists(path))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                }
                catch { }
            }
        }

        private void ToastOpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (toastTag != null && File.Exists(toastTag.FilePath))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(toastTag.FilePath) { UseShellExecute = true }); }
                catch { }
            }
            downloadToast.Visibility = Visibility.Collapsed;
        }

        private void BtnDownloads_Click(object sender, RoutedEventArgs e)
            => downloadsOverlay.Visibility = Visibility.Visible;

        /// <summary>更新下载按钮角标，显示正在下载的数量</summary>
        private void UpdateDownloadBadge()
        {
            if (downloadBadge == null || downloadBadgeText == null) return;
            if (_activeDownloads > 0)
            {
                downloadBadge.Visibility = Visibility.Visible;
                downloadBadgeText.Text = _activeDownloads > 9 ? "9+" : _activeDownloads.ToString();
            }
            else
            {
                downloadBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnCloseDownloads_Click(object sender, RoutedEventArgs e)
            => downloadsOverlay.Visibility = Visibility.Collapsed;

        private void DownloadsOverlay_MouseDown(object sender, MouseButtonEventArgs e)
            => downloadsOverlay.Visibility = Visibility.Collapsed;
        #endregion

        #region 历史记录
        private void LoadHistory()
        {
            try
            {
                if (File.Exists(HistoryPath))
                {
                    var list = JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(HistoryPath));
                    if (list != null) _allHistory.AddRange(list);
                }
            }
            catch { }
            RefreshHistoryList(null);
        }

        private void SaveHistory()
        {
            try { File.WriteAllText(HistoryPath, JsonSerializer.Serialize(_allHistory.Take(500).ToList(), JsonOpts)); }
            catch { }
        }

        private void AddHistory(string url, string title)
        {
            if (string.IsNullOrEmpty(url) || url.StartsWith("about:") ||
                url.Contains("HomePage.html", StringComparison.OrdinalIgnoreCase)) return;
            _allHistory.RemoveAll(h => h.Url == url);
            _allHistory.Insert(0, new HistoryItem { Url = url, Title = string.IsNullOrEmpty(title) ? url : title, VisitedAt = DateTime.Now });
            if (_allHistory.Count > 500) _allHistory.RemoveRange(500, _allHistory.Count - 500);
            SaveHistory();
            RefreshHistoryList(txtHistorySearch?.Text);
        }

        private void RefreshHistoryList(string? filter)
        {
            _history.Clear();
            var items = _allHistory.AsEnumerable();
            if (!string.IsNullOrEmpty(filter))
                items = items.Where(h => h.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || h.Url.Contains(filter, StringComparison.OrdinalIgnoreCase));
            foreach (var item in items.Take(50)) _history.Add(item);
        }

        private void TxtHistorySearch_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshHistoryList(txtHistorySearch.Text);

        private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定清除所有历史记录？", "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _allHistory.Clear(); _history.Clear(); SaveHistory();
            }
        }

        private void HistoryItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url)
            {
                settingsPage.Visibility = Visibility.Collapsed;
                webArea.Visibility = Visibility.Visible;
                var tab = tabList.SelectedItem as TabInfo;
                if (tab != null && _webViews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null)
                    wv.CoreWebView2.Navigate(url);
            }
        }
        #endregion

        #region 油猴脚本
        private void LoadScripts()
        {
            try
            {
                if (File.Exists(ScriptsPath))
                {
                    var list = JsonSerializer.Deserialize<List<UserScript>>(File.ReadAllText(ScriptsPath));
                    if (list != null) list.ForEach(_scripts.Add);
                }
            }
            catch { }
        }

        private void SaveScripts()
        {
            try { File.WriteAllText(ScriptsPath, JsonSerializer.Serialize(_scripts.ToList(), JsonOpts)); }
            catch { }
        }

        private void InjectScripts(Microsoft.Web.WebView2.Wpf.WebView2 wv, string url)
        {
            foreach (var script in _scripts.Where(s => s.Enabled))
            {
                if (MatchesUrl(script.Match, url))
                {
                    try { wv.CoreWebView2.ExecuteScriptAsync(script.Code); }
                    catch { }
                }
            }
        }

        private static bool MatchesUrl(string pattern, string url)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "*") return true;
            if (pattern == "*://*/*") return true;
            pattern = pattern.Replace(".", "\\.").Replace("*", ".*");
            return System.Text.RegularExpressions.Regex.IsMatch(url, pattern);
        }

        private void BtnAddScript_Click(object sender, RoutedEventArgs e)
        {
            _editingScriptId = null;
            txtScriptEditorTitle.Text = "添加脚本";
            txtScriptName.Text = "";
            txtScriptMatch.Text = "*://*/*";
            txtScriptCode.Text = "";
            scriptEditorOverlay.Visibility = Visibility.Visible;
        }

        private void BtnEditScript_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var script = _scripts.FirstOrDefault(s => s.Id == id);
                if (script == null) return;
                _editingScriptId = id;
                txtScriptEditorTitle.Text = "编辑脚本";
                txtScriptName.Text = script.Name;
                txtScriptMatch.Text = script.Match;
                txtScriptCode.Text = script.Code;
                scriptEditorOverlay.Visibility = Visibility.Visible;
            }
        }

        private void BtnDeleteScript_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                if (MessageBox.Show("确定删除此脚本？", "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    var script = _scripts.FirstOrDefault(s => s.Id == id);
                    if (script != null) { _scripts.Remove(script); SaveScripts(); }
                }
            }
        }

        private void BtnSaveScript_Click(object sender, RoutedEventArgs e)
        {
            var name = txtScriptName.Text.Trim();
            var match = txtScriptMatch.Text.Trim();
            var code = txtScriptCode.Text;
            if (string.IsNullOrEmpty(name)) return;

            if (_editingScriptId != null)
            {
                var script = _scripts.FirstOrDefault(s => s.Id == _editingScriptId);
                if (script != null) { script.Name = name; script.Match = match; script.Code = code; }
            }
            else
            {
                _scripts.Add(new UserScript { Name = name, Match = match, Code = code });
            }
            SaveScripts();
            scriptEditorOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnCancelScript_Click(object sender, RoutedEventArgs e)
            => scriptEditorOverlay.Visibility = Visibility.Collapsed;

        private void ScriptEnabled_Checked(object sender, RoutedEventArgs e) => SaveScripts();
        private void ScriptEnabled_Unchecked(object sender, RoutedEventArgs e) => SaveScripts();
        #endregion

        #region 密码管理
        private const string PasswordCaptureScript = @"(function(){
if(window.__pwdCap)return;window.__pwdCap=1;
document.addEventListener('submit',function(e){
var f=e.target;if(!f||!f.querySelectorAll)return;
var p=f.querySelector('input[type=""password""]');if(!p||!p.value)return;
var u=f.querySelector('input[type=""text""],input[type=""email""],input[type=""tel""],input:not([type])');
if(!u||!u.value)return;
window.chrome.webview.postMessage(JSON.stringify({type:""pwd"",site:location.hostname,u:u.value,p:p.value}));
},true);
})();";

        private void LoadPasswords()
        {
            try
            {
                if (File.Exists(PasswordsPath))
                {
                    var list = JsonSerializer.Deserialize<List<PasswordEntry>>(File.ReadAllText(PasswordsPath));
                    if (list != null) { _passwords.Clear(); list.ForEach(_passwords.Add); }
                }
            }
            catch { }
            UpdatePasswordsEmptyState();
        }

        private void SavePasswords()
        {
            try { File.WriteAllText(PasswordsPath, JsonSerializer.Serialize(_passwords.ToList(), JsonOpts)); }
            catch { }
            UpdatePasswordsEmptyState();
        }

        private void UpdatePasswordsEmptyState()
        {
            if (txtNoPasswords != null)
                txtNoPasswords.Visibility = _passwords.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SavePasswordEntry(string site, string username, string plainPassword)
        {
            if (string.IsNullOrEmpty(site) || string.IsNullOrEmpty(username)) return;
            var existing = _passwords.FirstOrDefault(p =>
                p.Site.Equals(site, StringComparison.OrdinalIgnoreCase) &&
                p.Username == username);
            if (existing != null)
            {
                existing.EncryptedPassword = Dpapi.Protect(plainPassword);
            }
            else
            {
                _passwords.Insert(0, new PasswordEntry
                {
                    Site = site,
                    Username = username,
                    EncryptedPassword = Dpapi.Protect(plainPassword)
                });
            }
            SavePasswords();
        }

        private void SetupPasswordCapture(Microsoft.Web.WebView2.Wpf.WebView2 wv)
        {
            wv.CoreWebView2.WebMessageReceived += (s, e) =>
            {
                try
                {
                    string message = e.TryGetWebMessageAsString();
                    if (string.IsNullOrEmpty(message)) return;
                    var msg = JsonSerializer.Deserialize<JsonElement>(message);
                    if (msg.TryGetProperty("type", out var t) && t.GetString() == "pwd")
                    {
                        var site = msg.GetProperty("site").GetString() ?? "";
                        var user = msg.GetProperty("u").GetString() ?? "";
                        var pass = msg.GetProperty("p").GetString() ?? "";
                        if (!string.IsNullOrEmpty(site) && !string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pass))
                            Dispatcher.Invoke(() => SavePasswordEntry(site, user, pass));
                    }
                }
                catch { }
            };
        }

        private void InjectPasswordAutofill(Microsoft.Web.WebView2.Wpf.WebView2 wv, string url)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
                var host = uri.Host;
                var matches = _passwords
                    .Where(p => host.Equals(p.Site, StringComparison.OrdinalIgnoreCase)
                             || host.EndsWith("." + p.Site, StringComparison.OrdinalIgnoreCase))
                    .Take(1)
                    .Select(p => new { site = p.Site, u = p.Username, p = Dpapi.Unprotect(p.EncryptedPassword) })
                    .ToList();
                if (matches.Count == 0) return;
                var json = JsonSerializer.Serialize(matches);
                var script = @"(function(){
var d=" + json + @";if(!d||!d.length)return;
setTimeout(function(){
var pw=document.querySelector('input[type=""password""]');
var us=document.querySelector('input[type=""text""],input[type=""email""],input[type=""tel""],input:not([type])');
if(us&&d[0].u){us.value=d[0].u;us.dispatchEvent(new Event('input',{bubbles:true}));us.dispatchEvent(new Event('change',{bubbles:true}));}
if(pw&&d[0].p){pw.value=d[0].p;pw.dispatchEvent(new Event('input',{bubbles:true}));pw.dispatchEvent(new Event('change',{bubbles:true}));}
},600);})();";
                wv.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch { }
        }

        private void BtnPasswords_Click(object sender, RoutedEventArgs e)
        {
            settingsPage.Visibility = Visibility.Visible;
            webArea.Visibility = Visibility.Collapsed;
            UpdatePageTitle("密码管理");
        }

        private void BtnRevealPassword_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var entry = _passwords.FirstOrDefault(p => p.Id == id);
                if (entry != null) entry.Revealed = !entry.Revealed;
            }
        }

        private void BtnDeletePassword_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var entry = _passwords.FirstOrDefault(p => p.Id == id);
                if (entry != null && MessageBox.Show($"确定删除 {entry.Site} 的密码？", "确认",
                    MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    _passwords.Remove(entry);
                    SavePasswords();
                }
            }
        }
        #endregion

        #region 标签页管理
        private static string? _homePageTempPath;

        /// <summary>将内嵌的 HomePage.html 解压到临时目录（仅首次），返回文件路径</summary>
        private static string EnsureHomePage()
        {
            if (_homePageTempPath != null) return _homePageTempPath;
            var dir = Path.Combine(Path.GetTempPath(), "mini2nbrowser");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "HomePage.html");
            using var stream = typeof(MainWindow).Assembly
                .GetManifestResourceStream("mini2nbrowser.HomePage.html");
            if (stream != null)
            {
                using var fs = File.Create(path);
                stream.CopyTo(fs);
            }
            _homePageTempPath = path;
            return path;
        }

        private string GetHomeUrl()
        {
            var engine = _config.SearchEngines.FirstOrDefault(x => x.Name == _config.DefaultEngine)
                  ?? _config.SearchEngines[0];
            string engKey = engine.Name switch
            {
                "百度" => "baidu",
                "Google" => "google",
                _ => "bing"
            };
            string theme = _config.IsDarkMode ? "dark" : "light";
            return $"file:///{EnsureHomePage().Replace('\\', '/')}?theme={theme}&engine={engKey}";
        }

        private void BtnNewTab_Click(object sender, RoutedEventArgs e) => NewTab();

        private void NewTab() => CreateAndAddTab(GetHomeUrl(), false);

        private void NewTabWithUrl(string url) => CreateAndAddTab(url, false);

        /// <summary>新建无痕标签页</summary>
        private void NewIncognitoTab() => CreateAndAddTab(GetHomeUrl(), true);

        /// <summary>创建并添加新标签页</summary>
        /// <param name="initialUrl">初始 URL</param>
        /// <param name="incognito">是否为无痕模式</param>
        private async void CreateAndAddTab(string initialUrl, bool incognito)
        {
            // 新建标签页时关闭设置页，回到网页视图
            settingsPage.Visibility = Visibility.Collapsed;
            webArea.Visibility = Visibility.Visible;

            var tab = new TabInfo { IsIncognito = incognito };
            _tabs.Add(tab);
            tabList.SelectedItem = tab;

            var wv = new Microsoft.Web.WebView2.Wpf.WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            webViewContainer.Children.Add(wv);
            _webViews[tab.Id] = wv;

            // 使用共享环境（开启扩展支持），通过 CreationProperties 区分无痕/普通
            var env = await GetWebViewEnvironmentAsync();
            wv.CreationProperties = new Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties
            {
                IsInPrivateModeEnabled = incognito
            };
            await wv.EnsureCoreWebView2Async(env);

            wv.CoreWebView2.Settings.IsPasswordAutosaveEnabled = !incognito;
            wv.CoreWebView2.Settings.IsGeneralAutofillEnabled = !incognito;
            wv.CoreWebView2.Profile.PreferredColorScheme = _config.IsDarkMode
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Light;

            // 仅普通标签页加载扩展（无痕标签页不加载扩展，符合无痕语义）
            if (!incognito)
            {
                await EnsureExtensionsLoadedAsync(wv.CoreWebView2.Profile);
            }

            UpdateIncognitoIndicator();

            SetupPasswordCapture(wv);
            SetupProtection(wv);
            SetupDownloads(wv);

            wv.CoreWebView2.DocumentTitleChanged += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    tab.Title = wv.CoreWebView2.DocumentTitle;
                    if (tabList.SelectedItem == tab) UpdatePageTitle(tab.Title);
                });
            };

            wv.CoreWebView2.FaviconChanged += async (s, args) =>
            {
                try
                {
                    using var iconStream = await wv.CoreWebView2.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
                    if (iconStream == null || iconStream.Length == 0) { Dispatcher.Invoke(() => tab.Favicon = null); return; }
                    using var ms = new MemoryStream();
                    await iconStream.CopyToAsync(ms);
                    ms.Position = 0;
                    Dispatcher.Invoke(() =>
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        tab.Favicon = bmp;
                    });
                }
                catch { Dispatcher.Invoke(() => tab.Favicon = null); }
            };

            wv.CoreWebView2.NavigationStarting += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (tabList.SelectedItem == tab)
                    {
                        txtUrl.Text = e.Uri;
                        btnStop.Visibility = Visibility.Visible;
                        btnReload.Visibility = Visibility.Collapsed;
                    }
                });
            };

            wv.CoreWebView2.NavigationCompleted += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (tabList.SelectedItem == tab)
                    {
                        var url = wv.CoreWebView2.Source;
                        txtUrl.Text = url.Contains("HomePage.html", StringComparison.OrdinalIgnoreCase) ? "" : url;
                        btnStop.Visibility = Visibility.Collapsed;
                        btnReload.Visibility = Visibility.Visible;
                        btnBack.IsEnabled = wv.CoreWebView2.CanGoBack;
                        btnForward.IsEnabled = wv.CoreWebView2.CanGoForward;
                        UpdateBookmarkIcon(url);
                        UpdatePageTitle(tab.Title);
                    }
                    // 无痕标签页：不记历史、不自动填充密码、不捕获密码
                    if (!incognito)
                    {
                        AddHistory(wv.CoreWebView2.Source, tab.Title);
                        InjectScripts(wv, wv.CoreWebView2.Source);
                        InjectPasswordAutofill(wv, wv.CoreWebView2.Source);
                        try { wv.CoreWebView2.ExecuteScriptAsync(PasswordCaptureScript); } catch { }
                    }
                    else
                    {
                        // 无痕标签页仍可注入用户脚本（用户主动选择），但不记录任何数据
                        InjectScripts(wv, wv.CoreWebView2.Source);
                    }
                });
            };

            wv.CoreWebView2.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                // 继承当前标签页的无痕状态
                Dispatcher.Invoke(() => CreateAndAddTab(e.Uri, incognito));
            };

            wv.CoreWebView2.SourceChanged += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (tabList.SelectedItem == tab && !wv.CoreWebView2.Source.Contains("HomePage.html", StringComparison.OrdinalIgnoreCase))
                        txtUrl.Text = wv.CoreWebView2.Source;
                });
            };

            wv.CoreWebView2.Navigate(initialUrl);
        }

        private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (tabList.SelectedItem is not TabInfo tab) return;
            foreach (var child in webViewContainer.Children.OfType<Microsoft.Web.WebView2.Wpf.WebView2>())
                child.Visibility = Visibility.Collapsed;

            if (_webViews.TryGetValue(tab.Id, out var wv))
            {
                wv.Visibility = Visibility.Visible;
                var url = wv.CoreWebView2?.Source ?? "";
                txtUrl.Text = url.Contains("HomePage.html", StringComparison.OrdinalIgnoreCase) ? "" : url;
                UpdateBookmarkIcon(url);
                btnBack.IsEnabled = wv.CoreWebView2?.CanGoBack ?? false;
                btnForward.IsEnabled = wv.CoreWebView2?.CanGoForward ?? false;
            }
            UpdateIncognitoIndicator();
        }

        /// <summary>更新无痕模式视觉指示：URL 栏左侧紫色"无痕"图标、窗口标题前缀</summary>
        private void UpdateIncognitoIndicator()
        {
            if (iconIncognito == null) return;
            var tab = tabList.SelectedItem as TabInfo;
            bool incog = tab?.IsIncognito ?? false;
            iconIncognito.Visibility = incog ? Visibility.Visible : Visibility.Collapsed;
            // 标题前缀由 UpdatePageTitle 自动处理
            UpdatePageTitle(tab?.Title ?? "");
        }

        private void TabList_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                if (e.OriginalSource is DependencyObject dep)
                {
                    var item = FindParent<ListBoxItem>(dep);
                    if (item?.DataContext is TabInfo tab)
                    {
                        CloseTab(tab);
                        e.Handled = true;
                    }
                }
            }
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T found) return found;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var tab = _tabs.FirstOrDefault(t => t.Id == id);
                if (tab != null) CloseTab(tab);
            }
        }

        private void CloseCurrentTab()
        {
            if (tabList.SelectedItem is TabInfo tab) CloseTab(tab);
        }

        private void CloseTab(TabInfo tab)
        {
            int idx = _tabs.IndexOf(tab);
            _tabs.Remove(tab);
            if (_webViews.TryGetValue(tab.Id, out var wv))
            {
                webViewContainer.Children.Remove(wv);
                wv.Dispose();
                _webViews.Remove(tab.Id);
            }
            if (_tabs.Count == 0) { NewTab(); return; }
            tabList.SelectedIndex = Math.Min(idx, _tabs.Count - 1);
        }

        private void UpdatePageTitle(string title)
        {
            string display = string.IsNullOrEmpty(title) ? "2ⁿ Browser" : title;
            // 无痕标签页标题加前缀（仅在当前选中标签页是无痕时）
            var tab = tabList.SelectedItem as TabInfo;
            if (tab?.IsIncognito == true && !display.StartsWith("[无痕]"))
                display = $"[无痕] {display}";
            Title = display;
        }
        #endregion

        #region 设置按钮
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            settingsPage.Visibility = Visibility.Visible;
            webArea.Visibility = Visibility.Collapsed;
            UpdatePageTitle("设置");
        }

        #region 无痕模式
        private void BtnIncognito_Click(object sender, RoutedEventArgs e) => NewIncognitoTab();
        #endregion

        #region 扩展管理
        private void BtnExtensions_Click(object sender, RoutedEventArgs e)
        {
            if (_extensionsManager == null)
            {
                MessageBox.Show("扩展功能初始化失败，请检查 WebView2 运行时是否已安装。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!ExtensionsManager.IsSupported)
            {
                MessageBox.Show("当前 WebView2 运行时版本过低，扩展功能需要 ≥1.0.2045。\n请更新 WebView2 Runtime 后重启应用。",
                    "版本不支持", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // 单实例：已打开则前置
            if (_extensionsWindow != null && _extensionsWindow.IsLoaded)
            {
                _extensionsWindow.Activate();
                return;
            }
            _extensionsWindow = new ExtensionsWindow(_extensionsManager)
            {
                Owner = this
            };
            _extensionsWindow.Closed += (s, e) => _extensionsWindow = null;
            _extensionsWindow.Show();
        }
        #endregion

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            settingsPage.Visibility = Visibility.Collapsed;
            webArea.Visibility = Visibility.Visible;
            if (tabList.SelectedItem is TabInfo tab)
                UpdatePageTitle(tab.Title);
        }
        #endregion

        #region 标签栏伸缩
        private void TabSidebar_MouseEnter(object sender, MouseEventArgs e)
        {
            IsSidebarExpanded = true;
            txtNewTabLabel.Visibility = Visibility.Visible;
            AnimateWidth(tabSidebar, SidebarExpanded);
            AnimateMargin(webArea, NavMarginExpanded, 6, 6, 6);
            AnimateMargin(settingsPage, NavMarginExpanded, 6, 6, 6);
        }

        private void TabSidebar_MouseLeave(object sender, MouseEventArgs e)
        {
            IsSidebarExpanded = false;
            txtNewTabLabel.Visibility = Visibility.Collapsed;
            AnimateWidth(tabSidebar, SidebarCollapsed);
            AnimateMargin(webArea, NavMarginCollapsed, 6, 6, 6);
            AnimateMargin(settingsPage, NavMarginCollapsed, 6, 6, 6);
        }

        private static void AnimateWidth(FrameworkElement el, double to)
        {
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            el.BeginAnimation(WidthProperty, anim);
        }

        private static void AnimateMargin(FrameworkElement el, double left, double top, double right, double bottom)
        {
            var anim = new ThicknessAnimation(
                new Thickness(left, top, right, bottom),
                TimeSpan.FromMilliseconds(200))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            el.BeginAnimation(MarginProperty, anim);
        }
        #endregion

        #region 导航
        private void TxtUrl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var input = txtUrl.Text.Trim();
                if (string.IsNullOrEmpty(input)) return;
                Navigate(input);
            }
        }

        private void Navigate(string input)
        {
            settingsPage.Visibility = Visibility.Collapsed;
            webArea.Visibility = Visibility.Visible;

            string url;
            if (input.StartsWith("about:") || input.StartsWith("file:///"))
                url = input;
            else if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
                     (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                url = input;
            else if (input.Contains('.') && !input.Contains(' '))
                url = "https://" + input;
            else
            {
                var engine = _config.SearchEngines.FirstOrDefault(x => x.Name == _config.DefaultEngine)
                    ?? _config.SearchEngines[0];
                url = engine.UrlTemplate.Replace("{0}", Uri.EscapeDataString(input));
            }

            var tab = tabList.SelectedItem as TabInfo;
            if (tab != null && _webViews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null)
                wv.CoreWebView2.Navigate(url);
        }

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            settingsPage.Visibility = Visibility.Collapsed;
            webArea.Visibility = Visibility.Visible;
            var tab = tabList.SelectedItem as TabInfo;
            if (tab != null && _webViews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null)
                wv.CoreWebView2.Navigate(GetHomeUrl());
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            var tab = tabList.SelectedItem as TabInfo;
            if (tab != null && _webViews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null && wv.CoreWebView2.CanGoBack)
                wv.CoreWebView2.GoBack();
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            var tab = tabList.SelectedItem as TabInfo;
            if (tab != null && _webViews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null && wv.CoreWebView2.CanGoForward)
                wv.CoreWebView2.GoForward();
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            var tab = tabList.SelectedItem as TabInfo;
            if (tab != null && _webViews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null)
                wv.CoreWebView2.Reload();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            var tab = tabList.SelectedItem as TabInfo;
            if (tab != null && _webViews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null)
                wv.CoreWebView2.Stop();
        }
        #endregion
    }
}
