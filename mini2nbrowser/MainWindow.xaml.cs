using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace mini2nbrowser
{
    /// <summary>
    /// 用于在 ListBox 的 ControlTemplate 内把 ListBox 的 ActualWidth 暴露给 ItemTemplate（通过 x:Name）。
    /// WPF 不允许 DataTemplate 通过 ElementName 直接引用 ControlTemplate 中的元素，
    /// 所以用 Freezable 作为"中间人"：它在 ControlTemplate 里订阅宽度变化，自身提供 ActualWidth，
    /// 然后在 DataTemplate 里通过 ElementName 引用它本身即可。
    /// </summary>
    public class WidthProxy : Freezable
    {
        protected override Freezable CreateInstanceCore() => new WidthProxy();

        public double ActualWidth
        {
            get => (double)GetValue(ActualWidthProperty);
            private set => SetValue(ActualWidthPropertyKey, value);
        }

        private static readonly DependencyPropertyKey ActualWidthPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(ActualWidth), typeof(double),
                typeof(WidthProxy), new PropertyMetadata(0.0));

        public static readonly DependencyProperty ActualWidthProperty =
            ActualWidthPropertyKey.DependencyProperty;

        public FrameworkElement? Source
        {
            get => (FrameworkElement?)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register(nameof(Source), typeof(FrameworkElement),
                typeof(WidthProxy), new PropertyMetadata(null, OnSourceChanged));

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = (WidthProxy)d;
            if (e.OldValue is FrameworkElement oldEl)
                oldEl.SizeChanged -= p.OnSizeChanged;
            if (e.NewValue is FrameworkElement newEl)
            {
                newEl.SizeChanged += p.OnSizeChanged;
                p.ActualWidth = newEl.ActualWidth;
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ActualWidth = e.NewSize.Width;
        }
    }

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
        /// <summary>快捷关键字（地址栏输入 "gh xxx" 触发 GitHub 搜索）</summary>
        public string Keyword { get; set; } = "";
        /// <summary>搜索URL模板，用 %s 作占位符（如 https://cn.bing.com/search?q=%s）</summary>
        public string SearchUrl { get; set; } = "";
        /// <summary>搜索建议接口URL（可选），用 %s 作占位符</summary>
        public string SuggestUrl { get; set; } = "";
        public string IconColor { get; set; } = "#2488C8";
        public string IconText { get; set; } = "";
        public bool IsDefault { get; set; }

        /// <summary>旧字段兼容：{0} → %s 迁移</summary>
        public string UrlTemplate
        {
            get => SearchUrl;
            set => SearchUrl = string.IsNullOrEmpty(value) ? value : value.Replace("{0}", "%s");
        }
    }

    public class SettingsNavItem
    {
        public string Tag { get; set; } = "";
        public string Label { get; set; } = "";
        public string IconData { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class AppConfig
    {
        public bool IsDarkMode { get; set; }
        public string DefaultEngine { get; set; } = "必应";
        public bool ProtectionEnabled { get; set; } = true;
        /// <summary>防护级别：0=关闭, 1=低(仅广告), 2=中(广告+跟踪), 3=高(广告+跟踪+社交+恶意+CSS隐藏)</summary>
        public int ProtectionLevel { get; set; } = 2;
        /// <summary>自定义拦截域名列表（每行一个域名）</summary>
        public List<string> CustomBlockList { get; set; } = new();
        /// <summary>自定义白名单域名（优先放行）</summary>
        public List<string> CustomAllowList { get; set; } = new();
        /// <summary>累计拦截次数</summary>
        public long BlockCount { get; set; }
        /// <summary>是否启用标签冻结（闲置标签自动释放内存）</summary>
        public bool TabFreezeEnabled { get; set; } = true;
        /// <summary>标签闲置多少分钟后冻结（默认10分钟）</summary>
        public int TabFreezeMinutes { get; set; } = 10;
        public bool AutoMemoryOptimize { get; set; } = true;
        public int MemoryThreshold { get; set; } = 500;
        public List<SearchEngine> SearchEngines { get; set; } = GetBuiltInEngines();

        /// <summary>预置7个搜索引擎（必应默认）</summary>
        public static List<SearchEngine> GetBuiltInEngines() => new()
        {
            new SearchEngine { Name = "必应", Keyword = "bing", SearchUrl = "https://cn.bing.com/search?q=%s", SuggestUrl = "https://cn.bing.com/osjson.aspx?query=%s", IconColor = "#0078D4", IconText = "B" },
            new SearchEngine { Name = "百度", Keyword = "bd", SearchUrl = "https://www.baidu.com/s?wd=%s", SuggestUrl = "https://suggestion.baidu.com/su?wd=%s", IconColor = "#2932E1", IconText = "百" },
            new SearchEngine { Name = "搜狗", Keyword = "sg", SearchUrl = "https://www.sogou.com/web?query=%s", IconColor = "#FF6600", IconText = "搜" },
            new SearchEngine { Name = "360搜索", Keyword = "so", SearchUrl = "https://www.so.com/s?q=%s", IconColor = "#19B955", IconText = "360" },
            new SearchEngine { Name = "头条搜索", Keyword = "tt", SearchUrl = "https://so.toutiao.com/search?q=%s", IconColor = "#FF0000", IconText = "头" },
            new SearchEngine { Name = "GitHub", Keyword = "gh", SearchUrl = "https://github.com/search?q=%s", IconColor = "#24292E", IconText = "GH" },
            new SearchEngine { Name = "StackOverflow", Keyword = "sover", SearchUrl = "https://stackoverflow.com/search?q=%s", IconColor = "#F48024", IconText = "SO" }
        };

        // 自定义字体（空=默认）
        public string CustomFontFamily { get; set; } = "";
        // 自定义主题颜色（空=默认）
        public string CustomAccentColor { get; set; } = "";
        public string CustomToolbarBg { get; set; } = "";
        public string CustomWindowBg { get; set; } = "";
        // 首页背景：图片路径或HTML文件路径（空=默认）
        public string HomeBackgroundImage { get; set; } = "";
        public string HomeCustomHtml { get; set; } = "";
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

        /// <summary>所属分组/工作区名称（空=未分组）</summary>
        public string Group { get; set; } = "";
        /// <summary>是否已冻结（闲置释放内存）</summary>
        public bool IsFrozen { get; set; }
        /// <summary>冻结前保存的 URL，恢复时重新导航</summary>
        public string? FrozenUrl { get; set; }
        /// <summary>最后活跃时间</summary>
        public DateTime LastActiveTime { get; set; } = DateTime.Now;
        /// <summary>是否为 PDF 标签页</summary>
        public bool IsPdf { get; set; }

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
        // ===== 媒体嗅探 =====
        private readonly MediaSniffer _mediaSniffer = new();
        private readonly MediaDownloader _mediaDownloader = new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "mini2nbrowser", "media_site_cache.json"));
        private readonly Dictionary<MediaItem, System.Threading.CancellationTokenSource> _mediaDownloadCts = new();
        private int _activeMediaDownloads;
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

        // 动态创建的设置页控件引用
        private CheckBox? _chkDarkMode;
        private CheckBox? _chkProtection;
        private ListBox? _lbEngines;
        private TextBlock? _txtCurrentProfile;
        private ItemsControl? _lbPasswords;
        private TextBlock? _txtNoPasswords;
        private ItemsControl? _lbScripts;
        private TextBlock? _txtBlockCount;
        private ComboBox? _cbProtectionLevel;
        private TextBox? _txtCustomBlock;
        private TextBox? _txtCustomAllow;
        // 标签管理
        private bool _tabSearchVisible;
        private bool _groupsCollapsed;
        private readonly Dictionary<string, bool> _collapsedGroups = new();
        private readonly System.Timers.Timer _freezeTimer;

        // 系统托盘
        private Hardcodet.Wpf.TaskbarNotification.TaskbarIcon? _trayIcon;
        private bool _isClosingFromTray;

        private const double SidebarCollapsed = 32;
        private const double SidebarExpanded = 170;
        private const double NavMarginCollapsed = 38;
        private const double NavMarginExpanded = 176;

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        // ===== 地址栏联想 (v1.5.0) =====
        private BrowserLocalDb? _localDb;
        private readonly ObservableCollection<AddressSuggestItem> _suggestItems = new();
        private CancellationTokenSource? _suggestCts;
        private static readonly HttpClient _httpClient = new(new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(2),
            DefaultRequestHeaders =
            {
                { "User-Agent", "mini2nbrowser/1.5 (+https://github.com/mini2nbrowser)" },
                { "Accept-Language", "zh-CN,zh;q=0.9,en;q=0.7" }
            }
        };
        private const int SuggestTakeLocalEach = 5;
        private const int SuggestCloudTake = 10;
        private const int SuggestDebounceMs = 250;

        // SQLite 数据库路径（历史/书签）—— 按 Profile 隔离，放在 _dataDir 下
        private string LocalDbPath => Path.Combine(_dataDir, "browser.db");
        // 开关：是否允许云端联想（用户可设置，默认开；无痕模式自动禁用）
        private bool EnableCloudSuggest => true;
        // 开关：本地联想总开关
        private bool EnableLocalSuggest => true;

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

        // ---- 多级别防护规则源 ----
        // 级别1：广告域名
        private static readonly HashSet<string> AdDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            "doubleclick.net","googlesyndication.com","googleadservices.com","google-analytics.com",
            "googletagmanager.com","googletagservices.com","adservice.google.com","adsense.com",
            "adnxs.com","adsystem.com","amazon-adsystem.com","adcolony.com","applovin.com",
            "adroll.com","outbrain.com","taboola.com","criteo.com","pubmatic.com",
            "rubiconproject.com","openx.net","casalemedia.com","moatads.com",
            "adsrvr.org","adform.net","yieldmo.com","smartadserver.com","revcontent.com",
            "contentabc.com","adtech.de","adtech.com","contextweb.com","gravity.com",
            "3lift.com","bidswitch.net","liadm.com","epsilon.com","demdex.net",
            "rlcdn.com","bluekai.com","krxd.net","pippio.com","tapad.com",
            "eyeota.net","exelate.com","audiencegrid.com","rfihub.com",
            "adsymptotic.com","adsterra.com","propellerads.com","popads.net","popcash.net",
            "admantx.com","w55c.net","p-td.com","turn.com","serving-sys.com",
            "mookie1.com","mathtag.com","mediavine.com","monetate.net","certona.net",
            "res-x.com","richrelevance.com","chango.com","sundaysky.com","dynamicdc.com"
        };

        // 级别2：跟踪/分析域名
        private static readonly HashSet<string> TrackerDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            "chartbeat.com","scorecardresearch.com","quantserve.com","crashlytics.com",
            "hotjar.com","mixpanel.com","segment.com","fullstory.com","bugsnag.com",
            "newrelic.com","amplitude.com","mouseflow.com","crazyegg.com","optimizely.com",
            "clicktale.net","trackjs.com","rollbar.com","sentry.io","raygun.io",
            "fullstory.com","logrocket.com","smartlook.com","usersnap.com",
            "inspectlet.com","luckyorange.com","etracker.com","etracker.de",
            "clarity.ms","adobedtm.com","demdex.net","omtrdc.net","aa-metrics.com",
            "metric.gstatic.com","www.googletagmanager.com","www.google-analytics.com",
            "stats.g.doubleclick.net","pixel.facebook.com","analytics.tiktok.com",
            "tr.snapchat.com","analytics.linkedin.com","snap.licdn.com",
            "t.co","analytics.twitter.com","ads.twitter.com",
            "bat.bing.com","clarity.ms","tagmanager.google.com"
        };

        // 级别3：社交插件/第三方组件
        private static readonly HashSet<string> SocialDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            "facebook.net","connect.facebook.net","connect.facebook.com","platform.twitter.com",
            "platform.linkedin.com","static.addtoany.com","assets.pinterest.com",
            "widgets.pinterest.com","platform.instagram.com","platform.tumblr.com",
            "disqus.com","addthis.com","sharethis.com","shareaholic.com",
            "sumome.com","sumo.com","addtoany.com","po.st","sharethrough.com"
        };

        // 级别3：已知恶意/挖矿/钓鱼域名
        private static readonly HashSet<string> MaliciousDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            "coinhive.com","coin-hive.com","coinhive.io","cryptoloot.com","crypto-loot.com",
            "mineralt.io","deepmine.io","webmine.cz","coinerra.com","coinhave.com",
            "miner.pr0gramm.com","authedmine.com","authedmine.eu","coin盲.com",
            "admixer.net","exoclick.com","exosrv.com","exdynsrv.com","juicyads.com",
            "trafficjunky.net","trafficjunky.com","ero-advertising.com","adxpansion.com",
            "plugrush.com","trafficforce.com","popcash.net","propellerads.com"
        };

        // 高级别CSS隐藏规则（注入页面隐藏广告元素）
        private const string AdBlockCss = @"
            [class*='ad-'],[class*='ad_'],[class*='ads-'],[class*='ads_'],[class*='advert'],
            [id*='ad-'],[id*='ad_'],[id*='ads-'],[id*='ads_'],[id*='advert'],
            [class*='banner-ad'],[class*='sponsor'],[id*='sponsor'],
            [class*='google-ad'],[id*='google-ad'],ins.adsbygoogle,
            [class*='dfp-'],[id*='dfp-'],[class*='gpt-'],[id*='gpt-'],
            [data-ad],[data-ad-slot],[data-ad-client],
            iframe[src*='doubleclick'],iframe[src*='googlesyndication'],
            iframe[src*='amazon-adsystem'],iframe[src*='adnxs'],
            div[class*='ad-container'],div[class*='ad_banner'],div[id*='ad-container'],
            [class*='promo-box'],[class*='newsletter-popup'],[class*='popup-ad']
            { display:none !important; visibility:hidden !important; }
        ";

        /// <summary>根据防护级别判断是否拦截请求</summary>
        private bool ShouldBlock(string host)
        {
            if (host == null) return false;
            // 白名单优先
            if (_config.CustomAllowList != null)
            {
                foreach (var allow in _config.CustomAllowList)
                {
                    if (host.EndsWith(allow, StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            // 自定义黑名单
            if (_config.CustomBlockList != null)
            {
                foreach (var block in _config.CustomBlockList)
                {
                    if (host.EndsWith(block, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            int level = _config.ProtectionLevel;
            if (level <= 0) return false;
            if (AdDomains.Contains(host) || AdDomains.Any(d => host.EndsWith(d, StringComparison.OrdinalIgnoreCase))) return true;
            if (level >= 2 && (TrackerDomains.Contains(host) || TrackerDomains.Any(d => host.EndsWith(d, StringComparison.OrdinalIgnoreCase)))) return true;
            if (level >= 3 && (
                SocialDomains.Contains(host) || SocialDomains.Any(d => host.EndsWith(d, StringComparison.OrdinalIgnoreCase)) ||
                MaliciousDomains.Contains(host) || MaliciousDomains.Any(d => host.EndsWith(d, StringComparison.OrdinalIgnoreCase))
            )) return true;
            return false;
        }

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

            // ===== v1.5.0：SQLite 本地数据库（按 Profile 隔离）=====
            try
            {
                _localDb = new BrowserLocalDb(LocalDbPath);
                // JSON→SQLite 一次性迁移（只有当数据库内为空、且有旧 JSON 时执行）
                _localDb.ImportFromJsonIfNeeded(HistoryPath, BookmarksPath);
            }
            catch
            {
                // 数据库失败不阻塞启动；所有查询自动降级为仅内存 JSON
                _localDb = null;
            }

            // 集合绑定即时完成（初始为空，开销极低）
            tabList.ItemsSource = _tabs;
            lbDownloads.ItemsSource = _downloads;
            lbMedia.ItemsSource = _mediaSniffer.Items;
            SuggestListBox.ItemsSource = _suggestItems;
            _mediaSniffer.Items.CollectionChanged += (s, e) =>
                Dispatcher.Invoke(() => mediaCountText.Text = _mediaSniffer.Items.Count > 0 ? $"({_mediaSniffer.Items.Count})" : "");

            ApplyTheme();
            if (_chkDarkMode != null) _chkDarkMode.IsChecked = _config.IsDarkMode;
            if (_chkProtection != null) _chkProtection.IsChecked = _config.ProtectionEnabled;

            // 系统托盘初始化
            _trayIcon = Resources["TrayIcon"] as Hardcodet.Wpf.TaskbarNotification.TaskbarIcon;
            if (_trayIcon != null)
            {
                // 多实例时托盘提示区分 profile
                _trayIcon.ToolTipText = string.IsNullOrEmpty(_profileName)
                    ? "mini2n Browser v1.5.0"
                    : $"mini2n Browser v1.5.0 [{_profileName}]";
                _trayIcon.TrayLeftMouseUp += (s, e) => RestoreFromTray();
            }

            StateChanged += MainWindow_StateChanged;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            PreviewKeyDown += MainWindow_PreviewKeyDown;

            _memoryTimer = new System.Timers.Timer(30000);
            _memoryTimer.Elapsed += (s, e) => CleanMemory();
            _memoryTimer.Start();

            // 标签冻结定时器（每2分钟检查一次闲置标签）
            _freezeTimer = new System.Timers.Timer(120000);
            _freezeTimer.Elapsed += (s, e) => CheckAndFreezeIdleTabs();
            _freezeTimer.Start();

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
            InitPdfPanel();
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
                // 迁移：旧配置 DefaultEngine="Bing" → "必应"
                if (_config.SearchEngines.Count > 0 &&
                    !_config.SearchEngines.Any(x => x.Name == _config.DefaultEngine))
                {
                    _config.DefaultEngine = "必应";
                }
                // 迁移：旧 {0} 格式 → %s 格式
                foreach (var eng in _config.SearchEngines)
                {
                    if (!string.IsNullOrEmpty(eng.SearchUrl) && eng.SearchUrl.Contains("{0}"))
                        eng.SearchUrl = eng.SearchUrl.Replace("{0}", "%s");
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

            void OverrideColor(string key, string? hex)
            {
                if (!string.IsNullOrEmpty(hex) && hex.StartsWith("#") && hex.Length >= 7)
                {
                    try
                    {
                        var color = (Color)ColorConverter.ConvertFromString(hex);
                        Application.Current.Resources[key] = new SolidColorBrush(color);
                    }
                    catch { }
                }
            }
            OverrideColor("AccentBlue", _config.CustomAccentColor);
            OverrideColor("ToolbarBg", _config.CustomToolbarBg);
            OverrideColor("WindowBg", _config.CustomWindowBg);

            if (!string.IsNullOrEmpty(_config.CustomFontFamily))
            {
                try
                {
                    var ff = new FontFamily(_config.CustomFontFamily);
                    if (!Application.Current.Resources.Contains("GlobalFontFamily"))
                        Application.Current.Resources.Add("GlobalFontFamily", ff);
                    else
                        Application.Current.Resources["GlobalFontFamily"] = ff;
                    this.FontFamily = ff;
                }
                catch { }
            }
            else
            {
                this.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
            }

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
            _config.ProtectionEnabled = true;
            if (_config.ProtectionLevel <= 0) _config.ProtectionLevel = 2;
            SaveConfig();
            ApplyProtectionToAllTabs();
        }
        private void ChkProtection_Unchecked(object sender, RoutedEventArgs e)
        {
            _config.ProtectionEnabled = false; SaveConfig();
        }

        private void CbProtectionLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cbProtectionLevel == null || _cbProtectionLevel.SelectedItem == null) return;
            _config.ProtectionLevel = _cbProtectionLevel.SelectedIndex;
            _config.ProtectionEnabled = _config.ProtectionLevel > 0;
            if (_chkProtection != null) _chkProtection.IsChecked = _config.ProtectionEnabled;
            SaveConfig();
            ApplyProtectionToAllTabs();
        }

        private void BtnSaveCustomLists_Click(object sender, RoutedEventArgs e)
        {
            if (_txtCustomBlock != null)
            {
                _config.CustomBlockList = _txtCustomBlock.Text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
            if (_txtCustomAllow != null)
            {
                _config.CustomAllowList = _txtCustomAllow.Text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
            SaveConfig();
            MessageBox.Show("自定义规则已保存", "完成");
        }
        #endregion

        #region 搜索引擎
        private void RefreshEngineList()
        {
            foreach (var eng in _config.SearchEngines)
                eng.IsDefault = eng.Name == _config.DefaultEngine;
            if (_lbEngines != null)
            {
                _lbEngines.ItemsSource = null;
                _lbEngines.ItemsSource = _config.SearchEngines;
            }
        }

        private void LbEngines_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_lbEngines != null && _lbEngines.SelectedItem is SearchEngine eng)
            {
                _config.DefaultEngine = eng.Name;
                SaveConfig();
                RefreshEngineList();
                // 刷新设置页中的默认引擎下拉框
                if (settingsContent.Content is FrameworkElement fe)
                    foreach (var cb in FindVisualChildren<ComboBox>(fe))
                        if (cb.DisplayMemberPath == "Name")
                        {
                            cb.SelectedItem = eng;
                            break;
                        }
                // 刷新首页
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

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t) yield return t;
                foreach (var grandChild in FindVisualChildren<T>(child))
                    yield return grandChild;
            }
        }

        private void BtnAddEngine_Click(object sender, RoutedEventArgs e)
        {
            _editingEngineName = null;
            txtEngineEditorTitle.Text = "添加搜索引擎";
            txtEngineName.Text = "";
            txtEngineKeyword.Text = "";
            txtEngineUrl.Text = "";
            txtEngineSuggest.Text = "";
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
            var keyword = txtEngineKeyword.Text.Trim();
            var url = txtEngineUrl.Text.Trim();
            var suggest = txtEngineSuggest.Text.Trim();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) return;

            // 统一为 %s 格式
            if (url.Contains("{0}")) url = url.Replace("{0}", "%s");
            if (!url.Contains("%s")) { MessageBox.Show("URL 必须包含 %s 作为搜索词占位符"); return; }
            if (suggest.Contains("{0}")) suggest = suggest.Replace("{0}", "%s");

            var colors = new[] { "#2488C8", "#28A870", "#E84C3D", "#8B5CF6", "#F59E0B", "#EC4899", "#06B6D4" };
            var rnd = new Random();

            if (_editingEngineName != null)
            {
                var eng = _config.SearchEngines.FirstOrDefault(x => x.Name == _editingEngineName);
                if (eng != null)
                {
                    eng.Name = name;
                    eng.Keyword = keyword;
                    eng.SearchUrl = url;
                    eng.SuggestUrl = suggest;
                }
            }
            else
            {
                if (_config.SearchEngines.Any(x => x.Name == name)) { MessageBox.Show("搜索引擎名称已存在"); return; }
                _config.SearchEngines.Add(new SearchEngine
                {
                    Name = name,
                    Keyword = keyword,
                    SearchUrl = url,
                    SuggestUrl = suggest,
                    IconColor = colors[rnd.Next(colors.Length)],
                    IconText = name.Length > 0 ? name[..1] : "?"
                });
            }
            SaveConfig();
            RefreshEngineList();
            engineEditorOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnCancelEngine_Click(object sender, RoutedEventArgs e)
            => engineEditorOverlay.Visibility = Visibility.Collapsed;
        #endregion

        #region 智能防护
        private void SetupProtection(Microsoft.Web.WebView2.Wpf.WebView2 wv)
        {
            // 请求拦截（多级别）
            wv.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            wv.CoreWebView2.WebResourceRequested += (s, e) =>
            {
                if (!_config.ProtectionEnabled || _config.ProtectionLevel <= 0) return;
                try
                {
                    var uri = new Uri(e.Request.Uri);
                    if (ShouldBlock(uri.Host))
                    {
                        e.Response = wv.CoreWebView2.Environment.CreateWebResourceResponse(null, 204, "No Content", "");
                        _config.BlockCount++;
                        if (_txtBlockCount != null)
                            Dispatcher.Invoke(() => _txtBlockCount.Text = _config.BlockCount.ToString());
                    }
                }
                catch { }
            };

            // 高级别：注入CSS隐藏广告元素
            wv.CoreWebView2.NavigationCompleted += (s, e) =>
            {
                if (!_config.ProtectionEnabled || _config.ProtectionLevel < 3) return;
                try
                {
                    if (e.IsSuccess)
                        _ = wv.CoreWebView2.ExecuteScriptAsync(
                            "(function(){var s=document.createElement('style');s.textContent=" +
                            System.Text.Json.JsonSerializer.Serialize(AdBlockCss) +
                            ";document.head.appendChild(s);})();");
                }
                catch { }
            };
        }

        /// <summary>防护级别切换后重新应用到所有标签页</summary>
        private void ApplyProtectionToAllTabs()
        {
            foreach (var wv in _webViews.Values)
            {
                if (wv.CoreWebView2 != null)
                {
                    // 高级别时注入CSS
                    if (_config.ProtectionLevel >= 3 && _config.ProtectionEnabled)
                    {
                        try
                        {
                            _ = wv.CoreWebView2.ExecuteScriptAsync(
                                "(function(){var s=document.createElement('style');s.textContent=" +
                                System.Text.Json.JsonSerializer.Serialize(AdBlockCss) +
                                ";document.head.appendChild(s);})();");
                        }
                        catch { }
                    }
                }
            }
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
            if (existing != null) { _bookmarks.Remove(existing); try { _localDb?.RemoveBookmark(url); } catch { } }
            else
            {
                var title = string.IsNullOrEmpty(tab.Title) ? url : tab.Title;
                _bookmarks.Insert(0, new BookmarkItem
                {
                    Title = title,
                    Url = url
                });
                try { _localDb?.AddBookmark(url, title); } catch { }
            }
            SaveBookmarks();
            UpdateBookmarkIcon(url);
        }

        private void BtnDeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url)
            {
                var bm = _bookmarks.FirstOrDefault(b => b.Url == url);
                if (bm != null) { _bookmarks.Remove(bm); SaveBookmarks(); try { _localDb?.RemoveBookmark(url); } catch { } }
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

        /// <summary>导出收藏夹为 JSON 文件</summary>
        private void BtnExportBookmarks_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                FileName = $"bookmarks_{DateTime.Now:yyyyMMdd}.json",
                Title = "导出收藏夹"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var data = JsonSerializer.Serialize(_bookmarks.ToList(), JsonOpts);
                    File.WriteAllText(dlg.FileName, data);
                    MessageBox.Show($"已导出 {_bookmarks.Count} 条收藏夹记录", "完成");
                }
                catch (Exception ex) { MessageBox.Show("导出失败：" + ex.Message, "错误"); }
            }
        }

        /// <summary>从 JSON 文件导入收藏夹</summary>
        private void BtnImportBookmarks_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                Title = "导入收藏夹"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<BookmarkItem>>(File.ReadAllText(dlg.FileName));
                    if (list == null || list.Count == 0)
                    {
                        MessageBox.Show("文件中没有有效的收藏夹数据", "提示");
                        return;
                    }
                    int added = 0, skipped = 0;
                    foreach (var item in list)
                    {
                        if (string.IsNullOrEmpty(item.Url)) { skipped++; continue; }
                        if (_bookmarks.Any(b => b.Url == item.Url)) { skipped++; continue; }
                        _bookmarks.Add(item);
                        added++;
                    }
                    SaveBookmarks();
                    MessageBox.Show($"导入完成：新增 {added} 条，跳过 {skipped} 条（已存在或无效）", "完成");
                }
                catch (Exception ex) { MessageBox.Show("导入失败：" + ex.Message, "错误"); }
            }
        }

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

        #region 媒体嗅探
        private void SetupMediaSniffer(Microsoft.Web.WebView2.Wpf.WebView2 wv)
        {
            // WebResourceResponseReceived：响应阶段触发，能拿到 Content-Type，无需注册 filter，性能开销小
            _mediaSniffer.Attach(wv.CoreWebView2, () =>
            {
                try
                {
                    if (wv.CoreWebView2 == null) return ("", "");
                    return (wv.CoreWebView2.DocumentTitle ?? "", wv.CoreWebView2.Source ?? "");
                }
                catch { return ("", ""); }
            });
        }

        private void BtnMediaSniffer_Click(object sender, RoutedEventArgs e)
        {
            mediaSnifferOverlay.Visibility = mediaSnifferOverlay.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnCloseMediaSniffer_Click(object sender, RoutedEventArgs e)
            => mediaSnifferOverlay.Visibility = Visibility.Collapsed;

        private void MediaSnifferOverlay_MouseDown(object sender, MouseButtonEventArgs e)
            => mediaSnifferOverlay.Visibility = Visibility.Collapsed;

        private void BtnClearMediaList_Click(object sender, RoutedEventArgs e)
            => _mediaSniffer.Clear();

        private void BtnRemoveMedia_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is MediaItem item)
            {
                if (_mediaDownloadCts.TryGetValue(item, out var cts))
                {
                    cts.Cancel();
                    _mediaDownloadCts.Remove(item);
                }
                _mediaSniffer.Remove(item);
            }
        }

        private async void BtnDownloadMedia_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not MediaItem item) return;
            if (item.Status == "下载中") return;

            var ext = string.IsNullOrEmpty(item.Ext) ? "bin" : item.Ext;
            if (item.Ext.Equals("m3u8", StringComparison.OrdinalIgnoreCase)) ext = "ts";
            var baseName = "media_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"{baseName}.{ext}",
                Filter = $"{ext.ToUpper()} 文件|*.{ext}|所有文件|*.*"
            };
            if (sfd.ShowDialog() != true) return;

            var path = sfd.FileName;
            var cts = new System.Threading.CancellationTokenSource();
            _mediaDownloadCts[item] = cts;
            item.Status = "下载中";
            item.Progress = 0;
            _activeMediaDownloads++;
            UpdateMediaBadge();

            try
            {
                // MediaDownloader 内部会直接更新 item 各字段（Status/Progress/Speed/Eta/Threads...）
                var progress = new Progress<MediaDownloadProgress>(_ => { });
                await _mediaDownloader.DownloadAsync(item, path, progress, cts.Token);
            }
            catch (OperationCanceledException)
            {
                item.Status = "已取消";
            }
            catch (NotSupportedException ex)
            {
                item.Status = "不支持";
                System.Windows.MessageBox.Show(ex.Message, "无法下载", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                item.Status = "失败";
                System.Windows.MessageBox.Show($"下载失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _activeMediaDownloads = Math.Max(0, _activeMediaDownloads - 1);
                UpdateMediaBadge();
                _mediaDownloadCts.Remove(item);
            }
        }

        private void BtnCopyMediaUrl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is MediaItem item)
            {
                try { System.Windows.Clipboard.SetText(item.Url); } catch { }
            }
        }

        private void UpdateMediaBadge()
        {
            if (mediaBadge == null || mediaBadgeText == null) return;
            if (_activeMediaDownloads > 0)
            {
                mediaBadge.Visibility = Visibility.Visible;
                mediaBadgeText.Text = _activeMediaDownloads > 9 ? "9+" : _activeMediaDownloads.ToString();
            }
            else mediaBadge.Visibility = Visibility.Collapsed;
        }
        #endregion

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
            // v1.5.0：同步写入 SQLite（联想主索引）
            try { _localDb?.AddHistory(url, title); } catch { }
            RefreshHistoryList(null);
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
            => RefreshHistoryList((sender as TextBox)?.Text);

        private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定清除所有历史记录？", "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _allHistory.Clear(); _history.Clear(); SaveHistory();
                try { _localDb?.ClearHistory(); } catch { }
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

        /// <summary>导出历史记录为 JSON 文件</summary>
        private void BtnExportHistory_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                FileName = $"history_{DateTime.Now:yyyyMMdd}.json",
                Title = "导出历史记录"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var data = JsonSerializer.Serialize(_allHistory.ToList(), JsonOpts);
                    File.WriteAllText(dlg.FileName, data);
                    MessageBox.Show($"已导出 {_allHistory.Count} 条历史记录", "完成");
                }
                catch (Exception ex) { MessageBox.Show("导出失败：" + ex.Message, "错误"); }
            }
        }

        /// <summary>从 JSON 文件导入历史记录</summary>
        private void BtnImportHistory_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                Title = "导入历史记录"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(dlg.FileName));
                    if (list == null || list.Count == 0)
                    {
                        MessageBox.Show("文件中没有有效的历史记录数据", "提示");
                        return;
                    }
                    int added = 0, skipped = 0;
                    foreach (var item in list)
                    {
                        if (string.IsNullOrEmpty(item.Url)) { skipped++; continue; }
                        if (_allHistory.Any(h => h.Url == item.Url)) { skipped++; continue; }
                        _allHistory.Add(item);
                        added++;
                    }
                    if (_allHistory.Count > 500)
                        _allHistory.RemoveRange(500, _allHistory.Count - 500);
                    SaveHistory();
                    RefreshHistoryList(null);
                    MessageBox.Show($"导入完成：新增 {added} 条，跳过 {skipped} 条（已存在或无效）", "完成");
                }
                catch (Exception ex) { MessageBox.Show("导入失败：" + ex.Message, "错误"); }
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
            if (_txtNoPasswords != null)
                _txtNoPasswords.Visibility = _passwords.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
            UpdatePageTitle("设置");
            if (lbSettingsNav.ItemsSource is IList<SettingsNavItem> items)
            {
                var match = items.FirstOrDefault(i => i.Tag == "passwords");
                if (match != null)
                {
                    lbSettingsNav.SelectedItem = match;
                }
            }
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
        private static string? _dinoGameTempPath;

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

        /// <summary>将内嵌的 DinoGame.html 解压到临时目录（仅首次），返回 file:// URL</summary>
        private static string EnsureDinoGame()
        {
            if (_dinoGameTempPath != null) return _dinoGameTempPath;
            var dir = Path.Combine(Path.GetTempPath(), "mini2nbrowser");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "DinoGame.html");
            using var stream = typeof(MainWindow).Assembly
                .GetManifestResourceStream("mini2nbrowser.DinoGame.html");
            if (stream != null)
            {
                using var fs = File.Create(path);
                stream.CopyTo(fs);
            }
            _dinoGameTempPath = path;
            return path;
        }

        private string GetHomeUrl()
        {
            if (!string.IsNullOrEmpty(_config.HomeCustomHtml) && File.Exists(_config.HomeCustomHtml))
            {
                return $"file:///{_config.HomeCustomHtml.Replace('\\', '/')}";
            }

            var engine = _config.SearchEngines.FirstOrDefault(x => x.Name == _config.DefaultEngine)
                  ?? _config.SearchEngines[0];
            string engKey = engine.Name switch
            {
                "百度" => "baidu",
                "Google" => "google",
                _ => "bing"
            };
            string theme = _config.IsDarkMode ? "dark" : "light";
            string bgImg = "";
            if (!string.IsNullOrEmpty(_config.HomeBackgroundImage) && File.Exists(_config.HomeBackgroundImage))
            {
                bgImg = "&bg=" + Uri.EscapeDataString(_config.HomeBackgroundImage);
            }
            return $"file:///{EnsureHomePage().Replace('\\', '/')}?theme={theme}&engine={engKey}{bgImg}";
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
            SetupMediaSniffer(wv);
            SetupPdfHandling(wv, tab);

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
                    tab.LastActiveTime = DateTime.Now;

                    // ===== 离线检测：导航失败（断网/DNS失败/服务器拒绝）→ 显示小恐龙游戏 =====
                    if (!e.IsSuccess)
                    {
                        var curSrc = wv.CoreWebView2.Source ?? "";
                        // 已经在游戏页/主页 → 不再重复跳转（防止死循环）
                        if (curSrc.Contains("DinoGame.html", StringComparison.OrdinalIgnoreCase)
                            || curSrc.Contains("HomePage.html", StringComparison.OrdinalIgnoreCase))
                        {
                            btnStop.Visibility = Visibility.Collapsed;
                            btnReload.Visibility = Visibility.Visible;
                            return;
                        }
                        wv.CoreWebView2.Navigate("file:///" + EnsureDinoGame().Replace('\\', '/'));
                        tab.Title = "离线了 — 小恐龙游戏";
                        if (tabList.SelectedItem == tab) UpdatePageTitle(tab.Title);
                        btnStop.Visibility = Visibility.Collapsed;
                        btnReload.Visibility = Visibility.Visible;
                        return;
                    }

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
            // 如果切换到已冻结标签，自动解冻
            if (tab.IsFrozen) UnfreezeTab(tab);
            // 更新活跃时间
            tab.LastActiveTime = DateTime.Now;

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

            if (tab.IsPdf) ShowPdfSidePanel();
            else HidePdfSidePanel();
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
            LoadProfiles();
            PopulateSettingsNav();
            if (lbSettingsNav.SelectedIndex < 0)
                lbSettingsNav.SelectedIndex = 0;
        }

        #region 设置页分类导航
        private static readonly List<SettingsNavItem> SettingsNavItems = new()
        {
            new() { Tag = "user", Label = "用户", Description = "管理您的用户配置文件和数据",
                IconData = "M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z" },
            new() { Tag = "appearance", Label = "外观", Description = "自定义浏览器的外观、主题和字体",
                IconData = "M12 3c-4.97 0-9 4.03-9 9 0 4.97 4.03 9 9 9 .55 0 1-.45 1-1 0-.26-.1-.5-.26-.68-.15-.18-.24-.41-.24-.68 0-.55.45-1 1-1h2.33c3.04 0 5.5-2.46 5.5-5.5C21 6.91 17.09 3 12 3zM7 10c-.83 0-1.5-.67-1.5-1.5S6.17 7 7 7s1.5.67 1.5 1.5S7.83 10 7 10zm5-3c-.83 0-1.5-.67-1.5-1.5S11.17 4 12 4s1.5.67 1.5 1.5S12.83 7 12 7zm5 0c-.83 0-1.5-.67-1.5-1.5S15.17 4 16 4s1.5.67 1.5 1.5S16.83 7 16 7zm4 5c-.83 0-1.5-.67-1.5-1.5S19.17 9 20 9s1.5.67 1.5 1.5S20.83 12 20 12z" },
            new() { Tag = "personalize", Label = "自定义", Description = "自定义字体、主题颜色、首页背景",
                IconData = "M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04c.39-.39.39-1.02 0-1.41l-2.34-2.34c-.39-.39-1.02-.39-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z" },
            new() { Tag = "search", Label = "搜索引擎", Description = "管理默认搜索引擎和自定义搜索引擎",
                IconData = "M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z" },
            new() { Tag = "privacy", Label = "隐私与安全", Description = "管理浏览数据和安全设置",
                IconData = "M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm-2 16l-4-4 1.41-1.41L10 14.17l6.59-6.59L18 9l-8 8z" },
            new() { Tag = "passwords", Label = "密码管理", Description = "查看和管理已保存的密码",
                IconData = "M18 8h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2zm-6 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm3.1-9H8.9V6c0-1.71 1.39-3.1 3.1-3.1 1.71 0 3.1 1.39 3.1 3.1v2z" },
            new() { Tag = "extensions", Label = "扩展", Description = "管理浏览器扩展程序",
                IconData = "M20.5 11H19V7c0-1.1-.9-2-2-2h-4V3.5C13 2.12 11.88 1 10.5 1S8 2.12 8 3.5V5H4c-1.1 0-1.99.9-1.99 2v3.8H3.5C4.88 11.8 6 12.93 6 14.3s-1.12 2.5-2.5 2.5H2V20c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2v-9c0-1.1-.9-2-2-2z" },
            new() { Tag = "scripts", Label = "油猴脚本", Description = "管理用户脚本，自定义网页行为",
                IconData = "M9.4 16.6L4.8 12l4.6-4.6L8 6l-6 6 6 6 1.4-1.4zm5.2 0L19.2 12l-4.6-4.6L16 6l6 6-6 6-1.4-1.4z" },
            new() { Tag = "about", Label = "关于", Description = "关于 mini2n Browser",
                IconData = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z" }
        };

        private void PopulateSettingsNav()
        {
            lbSettingsNav.ItemsSource = SettingsNavItems;
        }

        private void SettingsNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lbSettingsNav.SelectedItem is not SettingsNavItem item) return;
            settingsPageTitle.Text = item.Label;
            settingsPageDesc.Text = item.Description;
            settingsContent.Content = BuildSettingsPage(item.Tag);
        }

        private FrameworkElement BuildSettingsPage(string tag)
        {
            return tag switch
            {
                "user" => BuildUserPage(),
                "appearance" => BuildAppearancePage(),
                "personalize" => BuildPersonalizePage(),
                "search" => BuildSearchPage(),
                "privacy" => BuildPrivacyPage(),
                "passwords" => BuildPasswordsPage(),
                "extensions" => BuildExtensionsPage(),
                "scripts" => BuildScriptsPage(),
                "about" => BuildAboutPage(),
                _ => BuildUserPage()
            };
        }

        private Border MakeSection(string title, object content)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextColor"),
                Margin = new Thickness(4, 0, 0, 8)
            });
            panel.Children.Add(new Border
            {
                Background = (Brush)FindResource("ToolbarBg"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20),
                Child = (UIElement)content
            });
            return new Border { Child = panel, Margin = new Thickness(0, 0, 0, 20) };
        }

        private FrameworkElement BuildPersonalizePage()
        {
            var stack = new StackPanel();

            var fontPanel = new StackPanel();
            var fontInfo = new TextBlock
            {
                Text = "选择全局字体，支持导入 .ttf 字体文件。",
                FontSize = 12, Foreground = (Brush)FindResource("TextColor"),
                Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            fontPanel.Children.Add(fontInfo);

            var fontRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            fontRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fontRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            fontRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fontRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            fontRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var txtFont = new TextBox
            {
                Height = 32, Text = _config.CustomFontFamily,
                Style = (Style)FindResource("UrlBox"),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsReadOnly = true
            };
            Grid.SetColumn(txtFont, 0); fontRow.Children.Add(txtFont);

            var btnImportFont = new Button { Content = "导入 TTF", Width = 96, Height = 32 };
            btnImportFont.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "TrueType 字体 (*.ttf)|*.ttf|所有文件 (*.*)|*.*",
                    Title = "选择 TTF 字体文件"
                };
                if (dlg.ShowDialog() == true)
                {
                    try
                    {
                        var destDir = Path.Combine(_dataDir, "Fonts");
                        Directory.CreateDirectory(destDir);
                        var destPath = Path.Combine(destDir, Path.GetFileName(dlg.FileName));
                        File.Copy(dlg.FileName, destPath, true);
                        var families = Fonts.GetFontFamilies(destPath);
                        var fam = families.FirstOrDefault();
                        var fontFamilyName = fam != null
                            ? fam.FamilyNames.Values.FirstOrDefault() ?? fam.Source
                            : Path.GetFileNameWithoutExtension(destPath);
                        _config.CustomFontFamily = $"{destPath}#{fontFamilyName}";
                        SaveConfig();
                        txtFont.Text = fontFamilyName;
                        ApplyTheme();
                        MessageBox.Show("字体已导入：" + fontFamilyName, "完成");
                    }
                    catch (Exception ex) { MessageBox.Show("字体导入失败：" + ex.Message, "错误"); }
                }
            };
            Grid.SetColumn(btnImportFont, 2); fontRow.Children.Add(btnImportFont);

            var btnResetFont = new Button { Content = "重置", Width = 64, Height = 32 };
            btnResetFont.Click += (s, e) =>
            {
                _config.CustomFontFamily = "";
                SaveConfig();
                txtFont.Text = "";
                ApplyTheme();
            };
            Grid.SetColumn(btnResetFont, 4); fontRow.Children.Add(btnResetFont);

            fontPanel.Children.Add(fontRow);

            if (!string.IsNullOrEmpty(_config.CustomFontFamily) && _config.CustomFontFamily.Contains("#"))
            {
                var label = new TextBlock
                {
                    Text = "当前字体：" + _config.CustomFontFamily.Split('#').Last(),
                    FontSize = 12, Foreground = (Brush)FindResource("AccentBlue"),
                    Margin = new Thickness(0, 8, 0, 0)
                };
                fontPanel.Children.Add(label);
            }

            var colorPanel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var colorTip = new TextBlock
            {
                Text = "自定义主题颜色，留空使用默认。",
                FontSize = 12, Foreground = (Brush)FindResource("TextColor"),
                Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            colorPanel.Children.Add(colorTip);

            var accentRow = MakeColorRow("主题色（Accent）", _config.CustomAccentColor, v => _config.CustomAccentColor = v);
            var toolbarRow = MakeColorRow("工具栏背景", _config.CustomToolbarBg, v => _config.CustomToolbarBg = v);
            var windowRow = MakeColorRow("窗口背景", _config.CustomWindowBg, v => _config.CustomWindowBg = v);
            colorPanel.Children.Add(accentRow);
            colorPanel.Children.Add(toolbarRow);
            colorPanel.Children.Add(windowRow);

            var btnApplyColor = new Button
            {
                Content = "应用颜色", Width = 100, Height = 32,
                Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left
            };
            btnApplyColor.Click += (s, e) => { SaveConfig(); ApplyTheme(); };
            colorPanel.Children.Add(btnApplyColor);

            var btnResetColor = new Button
            {
                Content = "恢复默认颜色", Width = 120, Height = 32,
                Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left
            };
            btnResetColor.Click += (s, e) =>
            {
                _config.CustomAccentColor = "";
                _config.CustomToolbarBg = "";
                _config.CustomWindowBg = "";
                SaveConfig();
                ApplyTheme();
                MessageBox.Show("已恢复默认主题颜色", "完成");
            };
            colorPanel.Children.Add(btnResetColor);

            var homePanel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var homeTip = new TextBlock
            {
                Text = "自定义新标签页背景。可设置图片，或通过 HTML 文件完全自定义（类似 Via 浏览器）。",
                FontSize = 12, Foreground = (Brush)FindResource("TextColor"),
                Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            homePanel.Children.Add(homeTip);

            var imgRow = new Grid();
            imgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            imgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            imgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            imgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            imgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var txtImg = new TextBox
            {
                Height = 32, Text = _config.HomeBackgroundImage,
                Style = (Style)FindResource("UrlBox"),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsReadOnly = true
            };
            Grid.SetColumn(txtImg, 0); imgRow.Children.Add(txtImg);

            var btnImg = new Button { Content = "选择图片", Width = 96, Height = 32 };
            btnImg.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "图片文件 (*.jpg;*.jpeg;*.png;*.bmp;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.webp|所有文件 (*.*)|*.*",
                    Title = "选择首页背景图片"
                };
                if (dlg.ShowDialog() == true)
                {
                    try
                    {
                        var destDir = Path.Combine(_dataDir, "HomeAssets");
                        Directory.CreateDirectory(destDir);
                        var ext = Path.GetExtension(dlg.FileName).ToLower();
                        var destPath = Path.Combine(destDir, "home_bg" + ext);
                        File.Copy(dlg.FileName, destPath, true);
                        _config.HomeBackgroundImage = destPath;
                        SaveConfig();
                        txtImg.Text = destPath;
                        foreach (var tab in _tabs)
                        {
                            if (_webViews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null)
                            {
                                var url = wv.CoreWebView2.Source;
                                if (url.Contains("HomePage.html") || url.Contains("CustomHomePage"))
                                    wv.CoreWebView2.Navigate(GetHomeUrl());
                            }
                        }
                    }
                    catch (Exception ex) { MessageBox.Show("设置失败：" + ex.Message); }
                }
            };
            Grid.SetColumn(btnImg, 2); imgRow.Children.Add(btnImg);

            var btnClearImg = new Button { Content = "清除", Width = 64, Height = 32 };
            btnClearImg.Click += (s, e) =>
            {
                _config.HomeBackgroundImage = "";
                SaveConfig();
                txtImg.Text = "";
            };
            Grid.SetColumn(btnClearImg, 4); imgRow.Children.Add(btnClearImg);

            var lblImg = new TextBlock
            {
                Text = "首页背景图片（覆盖主题色）",
                FontSize = 12, Foreground = (Brush)FindResource("TextColor"),
                Opacity = 0.6, Margin = new Thickness(0, 4, 0, 12)
            };
            homePanel.Children.Add(imgRow);
            homePanel.Children.Add(lblImg);

            var htmlRow = new Grid();
            htmlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            htmlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            htmlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            htmlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            htmlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var txtHtml = new TextBox
            {
                Height = 32, Text = _config.HomeCustomHtml,
                Style = (Style)FindResource("UrlBox"),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsReadOnly = true
            };
            Grid.SetColumn(txtHtml, 0); htmlRow.Children.Add(txtHtml);

            var btnHtml = new Button { Content = "选择 HTML", Width = 96, Height = 32 };
            btnHtml.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "HTML 文件 (*.html;*.htm)|*.html;*.htm|所有文件 (*.*)|*.*",
                    Title = "选择自定义首页 HTML"
                };
                if (dlg.ShowDialog() == true)
                {
                    try
                    {
                        var destDir = Path.Combine(_dataDir, "HomeAssets");
                        Directory.CreateDirectory(destDir);
                        var destPath = Path.Combine(destDir, "homepage.html");
                        File.Copy(dlg.FileName, destPath, true);
                        _config.HomeCustomHtml = destPath;
                        SaveConfig();
                        txtHtml.Text = destPath;
                        MessageBox.Show("自定义首页已设置，将用于新标签页。", "完成");
                    }
                    catch (Exception ex) { MessageBox.Show("设置失败：" + ex.Message); }
                }
            };
            Grid.SetColumn(btnHtml, 2); htmlRow.Children.Add(btnHtml);

            var btnClearHtml = new Button { Content = "清除", Width = 64, Height = 32 };
            btnClearHtml.Click += (s, e) =>
            {
                _config.HomeCustomHtml = "";
                SaveConfig();
                txtHtml.Text = "";
            };
            Grid.SetColumn(btnClearHtml, 4); htmlRow.Children.Add(btnClearHtml);

            var lblHtml = new TextBlock
            {
                Text = "自定义首页 HTML（优先级最高，完全替换默认新标签页）",
                FontSize = 12, Foreground = (Brush)FindResource("TextColor"),
                Opacity = 0.6, Margin = new Thickness(0, 4, 0, 0)
            };
            homePanel.Children.Add(htmlRow);
            homePanel.Children.Add(lblHtml);

            var allPanel = new StackPanel();
            allPanel.Children.Add(MakeSection("自定义字体", fontPanel));
            allPanel.Children.Add(MakeSection("自定义主题颜色", colorPanel));
            allPanel.Children.Add(MakeSection("首页自定义", homePanel));
            return allPanel;
        }

        private Grid MakeColorRow(string label, string initValue, Action<string> setter)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = label, FontSize = 13, Foreground = (Brush)FindResource("TextColor"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0); row.Children.Add(lbl);

            var txt = new TextBox
            {
                Height = 32, Text = initValue,
                Style = (Style)FindResource("UrlBox"),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas")
            };
            txt.TextChanged += (s, e) => { setter(txt.Text?.Trim() ?? ""); };
            Grid.SetColumn(txt, 1); row.Children.Add(txt);

            var picker = new Button { Content = "选择", Width = 56, Height = 32 };
            picker.Click += (s, e) =>
            {
                var dlg = new WinForms.ColorDialog
                {
                    FullOpen = true,
                    AllowFullOpen = true
                };
                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                {
                    var c = dlg.Color;
                    var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                    txt.Text = hex;
                }
            };
            Grid.SetColumn(picker, 3); row.Children.Add(picker);

            return row;
        }

        private FrameworkElement BuildUserPage()
        {
            var stack = new StackPanel();
            // 当前用户
            var currentGrid = new Grid();
            currentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            currentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            currentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = new TextBlock { Text = "👤", FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock
            {
                Text = "当前用户：", FontSize = 13,
                Foreground = (Brush)FindResource("TextColor"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            var name = new TextBlock
            {
                Text = string.IsNullOrEmpty(_profileName) ? "默认" : _profileName,
                FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentBlue"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0); Grid.SetColumn(label, 1); Grid.SetColumn(name, 2);
            currentGrid.Children.Add(icon); currentGrid.Children.Add(label); currentGrid.Children.Add(name);
            _txtCurrentProfile = name;

            var currentBorder = new Border
            {
                Background = (Brush)FindResource("ToolbarBg"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Child = currentGrid
            };
            stack.Children.Add(currentBorder);

            // 用户列表
            var listPanel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var profiles = GetAllProfileNames();
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? App.DataDir;

            // 默认用户
            listPanel.Children.Add(CreateProfileItem("", "默认", ""));
            foreach (var p in profiles)
            {
                var dir = Path.Combine(exeDir, "Profiles", p);
                listPanel.Children.Add(CreateProfileItem(p, p, "占用 " + GetDirectorySizeText(dir)));
            }
            stack.Children.Add(listPanel);

            var btnNew = new Button
            {
                Content = "➕ 新建用户",
                Width = 120, Height = 32,
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            btnNew.Click += (s, e) => BtnNewProfile_Click(s, e);
            stack.Children.Add(btnNew);

            var tip = new TextBlock
            {
                Text = "每个用户拥有独立的书签、历史、密码、扩展和登录状态，互相隔离。",
                FontSize = 12, Foreground = (Brush)FindResource("TextColor"),
                Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 0)
            };
            stack.Children.Add(tip);

            return MakeSection("用户", stack);
        }

        private Border CreateProfileItem(string name, string displayName, string sizeText)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock { Text = "👤", FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(icon, 0);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text = displayName, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextColor")
            });
            if (!string.IsNullOrEmpty(sizeText))
                info.Children.Add(new TextBlock
                {
                    Text = sizeText, FontSize = 11,
                    Foreground = (Brush)FindResource("TextColor"), Opacity = 0.6
                });
            Grid.SetColumn(info, 1);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var btnSwitch = new Button
            {
                Content = "切换", Width = 56, Height = 26, FontSize = 11,
                Tag = name, Margin = new Thickness(0, 0, 6, 0)
            };
            btnSwitch.Click += (s, e) => BtnSwitchProfile_Click(
                new Button { Tag = name }, e);
            var btnDel = new Button
            {
                Content = "删除", Width = 56, Height = 26, FontSize = 11,
                Tag = name
            };
            btnDel.Click += (s, e) => BtnDeleteProfile_Click(
                new Button { Tag = name }, e);
            actions.Children.Add(btnSwitch);
            actions.Children.Add(btnDel);
            Grid.SetColumn(actions, 2);

            grid.Children.Add(icon); grid.Children.Add(info); grid.Children.Add(actions);

            return new Border
            {
                Background = (Brush)FindResource("WindowBg"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 4, 0, 0),
                Child = grid
            };
        }

        private FrameworkElement BuildAppearancePage()
        {
            var allPanel = new StackPanel();

            var stack = new StackPanel();
            var chk = new CheckBox
            {
                Content = "深色模式",
                FontSize = 13,
                Foreground = (Brush)FindResource("TextColor"),
                Margin = new Thickness(0, 4, 0, 0)
            };
            _chkDarkMode = chk;
            chk.Checked += ChkDarkMode_Checked;
            chk.Unchecked += ChkDarkMode_Unchecked;
            stack.Children.Add(chk);

            allPanel.Children.Add(MakeSection("外观", stack));

            // 标签冻结设置
            var freezePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            var chkFreeze = new CheckBox
            {
                Content = "自动冻结闲置标签（释放内存）",
                FontSize = 13,
                Foreground = (Brush)FindResource("TextColor"),
                IsChecked = _config.TabFreezeEnabled,
                Margin = new Thickness(0, 4, 0, 0)
            };
            chkFreeze.Checked += (s, e) => { _config.TabFreezeEnabled = true; SaveConfig(); };
            chkFreeze.Unchecked += (s, e) => { _config.TabFreezeEnabled = false; SaveConfig(); };
            freezePanel.Children.Add(chkFreeze);

            var freezeTip = new TextBlock
            {
                Text = "闲置超过指定时间的标签页将被冻结（导航到空白页释放内存），切换到该标签时自动恢复。",
                FontSize = 12, Foreground = (Brush)FindResource("TextColor"),
                Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 12)
            };
            freezePanel.Children.Add(freezeTip);

            var minLabel = new TextBlock
            {
                Text = "闲置冻结时间（分钟）", FontSize = 13,
                Foreground = (Brush)FindResource("TextColor"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            freezePanel.Children.Add(minLabel);
            var cbMin = new ComboBox { Width = 120, Height = 32, FontSize = 13 };
            cbMin.Items.Add("5 分钟");
            cbMin.Items.Add("10 分钟");
            cbMin.Items.Add("15 分钟");
            cbMin.Items.Add("30 分钟");
            cbMin.SelectedIndex = _config.TabFreezeMinutes switch
            {
                5 => 0, 10 => 1, 15 => 2, 30 => 3, _ => 1
            };
            cbMin.SelectionChanged += (s, e) =>
            {
                _config.TabFreezeMinutes = cbMin.SelectedIndex switch
                {
                    0 => 5, 1 => 10, 2 => 15, 3 => 30, _ => 10
                };
                SaveConfig();
            };
            freezePanel.Children.Add(cbMin);

            allPanel.Children.Add(MakeSection("标签冻结", freezePanel));

            return allPanel;
        }

        private FrameworkElement BuildSearchPage()
        {
            var stack = new StackPanel();

            // 默认引擎选择
            var defaultLabel = new TextBlock
            {
                Text = "默认搜索引擎",
                FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextColor"),
                Margin = new Thickness(4, 0, 0, 6)
            };
            stack.Children.Add(defaultLabel);

            var cbDefault = new ComboBox
            {
                Width = 300, Height = 32, FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4),
                DisplayMemberPath = "Name"
            };
            foreach (var eng in _config.SearchEngines)
                cbDefault.Items.Add(eng);
            var cur = _config.SearchEngines.FirstOrDefault(x => x.Name == _config.DefaultEngine);
            if (cur != null) cbDefault.SelectedItem = cur;
            cbDefault.SelectionChanged += (s, e) =>
            {
                if (cbDefault.SelectedItem is SearchEngine eng)
                {
                    _config.DefaultEngine = eng.Name;
                    SaveConfig();
                    // 刷新首页
                    foreach (var tab in _tabs)
                        if (_webViews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null &&
                            wv.CoreWebView2.Source.Contains("HomePage.html", StringComparison.OrdinalIgnoreCase))
                            wv.CoreWebView2.Navigate(GetHomeUrl());
                }
            };
            stack.Children.Add(cbDefault);

            var tipLabel = new TextBlock
            {
                Text = "提示：地址栏输入关键字 + 空格 + 搜索词可快速切换引擎\n例如：gh wpf webview2 → GitHub 搜索",
                FontSize = 11, Opacity = 0.6,
                Foreground = (Brush)FindResource("TextColor"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 12)
            };
            stack.Children.Add(tipLabel);

            // 引擎列表
            var listLabel = new TextBlock
            {
                Text = "所有引擎",
                FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextColor"),
                Margin = new Thickness(4, 0, 0, 6)
            };
            stack.Children.Add(listLabel);

            var lb = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                MaxHeight = 300,
                Margin = new Thickness(0, 4, 0, 0),
                ItemTemplate = (DataTemplate)FindResource("SearchEngineItemTemplate")
            };
            _lbEngines = lb;
            lb.ItemsSource = _config.SearchEngines;
            lb.SelectionChanged += LbEngines_SelectionChanged;
            stack.Children.Add(lb);

            var btnAdd = new Button
            {
                Content = "➕ 添加自定义引擎",
                Width = 160, Height = 32,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            btnAdd.Click += (s, e) => BtnAddEngine_Click(s, e);
            stack.Children.Add(btnAdd);

            // 设置 IsDefault 标记
            foreach (var eng in _config.SearchEngines)
                eng.IsDefault = eng.Name == _config.DefaultEngine;

            return MakeSection("搜索引擎", stack);
        }

        private FrameworkElement BuildPrivacyPage()
        {
            var allPanel = new StackPanel();

            // ===== 安全防护 =====
            var protStack = new StackPanel();

            var chkProt = new CheckBox
            {
                Content = "启用安全防护",
                FontSize = 13,
                Foreground = (Brush)FindResource("TextColor"),
                Margin = new Thickness(0, 4, 0, 0)
            };
            _chkProtection = chkProt;
            chkProt.IsChecked = _config.ProtectionEnabled;
            chkProt.Checked += ChkProtection_Checked;
            chkProt.Unchecked += ChkProtection_Unchecked;
            protStack.Children.Add(chkProt);

            // 防护级别
            var levelLabel = new TextBlock
            {
                Text = "防护级别", FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextColor"),
                Margin = new Thickness(0, 16, 0, 6)
            };
            protStack.Children.Add(levelLabel);

            var cbLevel = new ComboBox
            {
                Width = 280, Height = 32, FontSize = 13,
                Margin = new Thickness(0, 0, 0, 0)
            };
            cbLevel.Items.Add("关闭 — 不拦截任何请求");
            cbLevel.Items.Add("低 — 仅拦截广告域名");
            cbLevel.Items.Add("中 — 拦截广告 + 跟踪/分析 (推荐)");
            cbLevel.Items.Add("高 — 广告 + 跟踪 + 社交 + 恶意 + CSS隐藏");
            cbLevel.SelectedIndex = _config.ProtectionLevel;
            _cbProtectionLevel = cbLevel;
            cbLevel.SelectionChanged += CbProtectionLevel_SelectionChanged;
            protStack.Children.Add(cbLevel);

            var levelDesc = new TextBlock
            {
                FontSize = 12, Foreground = (Brush)FindResource("TextColor"),
                Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            void UpdateLevelDesc()
            {
                levelDesc.Text = cbLevel.SelectedIndex switch
                {
                    0 => "不拦截任何请求，所有内容正常加载。",
                    1 => "拦截主要广告网络域名，减少广告加载，兼容性最佳。",
                    2 => "拦截广告网络 + 跟踪分析脚本，推荐日常使用。",
                    3 => "最高级别防护：广告 + 跟踪 + 社交插件 + 恶意/挖矿域名拦截，并注入CSS隐藏页面广告元素。",
                    _ => ""
                };
            }
            UpdateLevelDesc();
            cbLevel.SelectionChanged += (s, e) => UpdateLevelDesc();
            protStack.Children.Add(levelDesc);

            // 拦截统计
            var statsGrid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var statsLabel = new TextBlock
            {
                Text = "累计拦截次数：", FontSize = 13,
                Foreground = (Brush)FindResource("TextColor"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(statsLabel, 0);
            var statsVal = new TextBlock
            {
                Text = _config.BlockCount.ToString(),
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("AccentBlue"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            _txtBlockCount = statsVal;
            Grid.SetColumn(statsVal, 1);
            statsGrid.Children.Add(statsLabel);
            statsGrid.Children.Add(statsVal);
            protStack.Children.Add(statsGrid);

            // 无痕窗口
            var btnIncog = new Button
            {
                Content = "🗕 打开无痕窗口",
                Width = 140, Height = 32,
                Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            btnIncog.Click += (s, e) => NewIncognitoTab();
            protStack.Children.Add(btnIncog);

            allPanel.Children.Add(MakeSection("安全防护", protStack));

            // ===== 自定义规则 =====
            var rulesStack = new StackPanel();

            var rulesTip = new TextBlock
            {
                Text = "自定义拦截/白名单域名，每行一个。白名单优先于黑名单和内置规则。",
                FontSize = 12, Foreground = (Brush)FindResource("TextColor"),
                Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            rulesStack.Children.Add(rulesTip);

            // 黑名单
            var blockLabel = new TextBlock
            {
                Text = "拦截域名（黑名单）", FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextColor"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            rulesStack.Children.Add(blockLabel);
            var txtBlock = new TextBox
            {
                Height = 100, FontSize = 12, FontFamily = new FontFamily("Consolas"),
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = string.Join("\r\n", _config.CustomBlockList ?? new List<string>()),
                Style = (Style)FindResource("UrlBox")
            };
            _txtCustomBlock = txtBlock;
            rulesStack.Children.Add(txtBlock);

            // 白名单
            var allowLabel = new TextBlock
            {
                Text = "放行域名（白名单）", FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextColor"),
                Margin = new Thickness(0, 12, 0, 6)
            };
            rulesStack.Children.Add(allowLabel);
            var txtAllow = new TextBox
            {
                Height = 80, FontSize = 12, FontFamily = new FontFamily("Consolas"),
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = string.Join("\r\n", _config.CustomAllowList ?? new List<string>()),
                Style = (Style)FindResource("UrlBox")
            };
            _txtCustomAllow = txtAllow;
            rulesStack.Children.Add(txtAllow);

            var btnSaveRules = new Button
            {
                Content = "保存自定义规则", Width = 140, Height = 32,
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            btnSaveRules.Click += BtnSaveCustomLists_Click;
            rulesStack.Children.Add(btnSaveRules);

            allPanel.Children.Add(MakeSection("自定义规则", rulesStack));

            // ===== 数据管理 =====
            var dataPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            var dataTip = new TextBlock
            {
                Text = "导入或导出收藏夹和历史记录，便于备份和迁移。",
                FontSize = 12, Foreground = (Brush)FindResource("TextColor"),
                Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            dataPanel.Children.Add(dataTip);

            var bmLabel = new TextBlock
            {
                Text = "收藏夹", FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextColor"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            dataPanel.Children.Add(bmLabel);

            var bmBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
            var btnExportBm = new Button
            {
                Content = "📤 导出收藏夹", Width = 130, Height = 32, Margin = new Thickness(0, 0, 8, 0)
            };
            btnExportBm.Click += BtnExportBookmarks_Click;
            bmBtnRow.Children.Add(btnExportBm);

            var btnImportBm = new Button
            {
                Content = "📥 导入收藏夹", Width = 130, Height = 32
            };
            btnImportBm.Click += BtnImportBookmarks_Click;
            bmBtnRow.Children.Add(btnImportBm);
            dataPanel.Children.Add(bmBtnRow);

            var hsLabel = new TextBlock
            {
                Text = "历史记录", FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextColor"),
                Margin = new Thickness(0, 16, 0, 6)
            };
            dataPanel.Children.Add(hsLabel);

            var hsBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
            var btnExportHs = new Button
            {
                Content = "📤 导出历史记录", Width = 130, Height = 32, Margin = new Thickness(0, 0, 8, 0)
            };
            btnExportHs.Click += BtnExportHistory_Click;
            hsBtnRow.Children.Add(btnExportHs);

            var btnImportHs = new Button
            {
                Content = "📥 导入历史记录", Width = 130, Height = 32
            };
            btnImportHs.Click += BtnImportHistory_Click;
            hsBtnRow.Children.Add(btnImportHs);
            dataPanel.Children.Add(hsBtnRow);

            allPanel.Children.Add(MakeSection("数据管理", dataPanel));

            return allPanel;
        }

        private FrameworkElement BuildPasswordsPage()
        {
            var stack = new StackPanel();
            var tip = new TextBlock
            {
                Text = "自动捕获并加密保存网页登录密码，重启后自动填充。密码使用 DPAPI 加密，仅本机可解密。",
                FontSize = 12, Foreground = (Brush)FindResource("TextColor"),
                Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(tip);

            var lb = new ItemsControl
            {
                Margin = new Thickness(0, 8, 0, 0),
                ItemTemplate = (DataTemplate)FindResource("PasswordItemTemplate")
            };
            _lbPasswords = lb;
            stack.Children.Add(lb);

            var noPwd = new TextBlock
            {
                Text = "暂无保存的密码",
                FontSize = 12, Foreground = (Brush)FindResource("TextColor"),
                Opacity = 0.4, Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed
            };
            _txtNoPasswords = noPwd;
            stack.Children.Add(noPwd);

            return MakeSection("密码管理", stack);
        }

        private FrameworkElement BuildExtensionsPage()
        {
            var stack = new StackPanel();
            var tip = new TextBlock
            {
                Text = "导入 .crx 文件或解压扩展文件夹加载本地扩展。仅导入可信来源扩展，第三方扩展存在安全风险。",
                FontSize = 12, Foreground = (Brush)FindResource("TextColor"),
                Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(tip);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var btnCrx = new Button
            {
                Content = "📦 导入 CRX",
                Width = 130, Height = 32,
                Margin = new Thickness(0, 0, 8, 0)
            };
            btnCrx.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "CRX 扩展 (*.crx)|*.crx|所有文件 (*.*)|*.*",
                    Title = "选择 CRX 文件"
                };
                if (dlg.ShowDialog() == true && _extensionsManager != null)
                    _extensionsManager.ImportCrxAsync(dlg.FileName);
            };
            btnRow.Children.Add(btnCrx);

            var btnFolder = new Button
            {
                Content = "📁 导入扩展文件夹",
                Width = 150, Height = 32
            };
            btnFolder.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择包含 manifest.json 的文件夹" };
                if (dlg.ShowDialog() == true && _extensionsManager != null)
                    _extensionsManager.ImportFolderAsync(dlg.FolderName);
            };
            btnRow.Children.Add(btnFolder);
            stack.Children.Add(btnRow);

            return MakeSection("扩展", stack);
        }

        private FrameworkElement BuildScriptsPage()
        {
            var stack = new StackPanel();
            var btnAdd = new Button
            {
                Content = "➕ 添加脚本",
                Width = 120, Height = 32,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            btnAdd.Click += (s, e) => BtnAddScript_Click(s, e);
            stack.Children.Add(btnAdd);

            var lb = new ItemsControl
            {
                Margin = new Thickness(0, 8, 0, 0),
                ItemTemplate = (DataTemplate)FindResource("ScriptItemTemplate")
            };
            _lbScripts = lb;
            stack.Children.Add(lb);

            return MakeSection("油猴脚本", stack);
        }

        private FrameworkElement BuildAboutPage()
        {
            var stack = new StackPanel();
            var title = new TextBlock
            {
                Text = "2ⁿ Browser",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextColor"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(title);
            var ver = new TextBlock
            {
                Text = "版本 1.3.0",
                FontSize = 14,
                Foreground = (Brush)FindResource("AccentBlue"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 12)
            };
            stack.Children.Add(ver);

            var desc = new TextBlock
            {
                Text = "一个基于 WebView2 的极简浏览器，支持垂直标签、标签分组/工作区、标签搜索、闲置冻结、PDF原生编辑批注、多级别安全防护、自定义字体主题首页等功能。",
                FontSize = 13,
                Foreground = (Brush)FindResource("TextColor"),
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            };
            stack.Children.Add(desc);

            var tech = new TextBlock
            {
                Text = "技术栈：.NET 8 · WPF · WebView2",
                FontSize = 12,
                Foreground = (Brush)FindResource("TextColor"),
                Opacity = 0.5,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(tech);

            return MakeSection("关于", stack);
        }

        #endregion

        #region 更多按钮（三点菜单）
        private void BtnMore_Click(object sender, RoutedEventArgs e)
        {
            if (btnMore.ContextMenu != null)
            {
                btnMore.ContextMenu.PlacementTarget = btnMore;
                btnMore.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                btnMore.ContextMenu.HorizontalOffset = -90;
                btnMore.ContextMenu.IsOpen = true;
            }
        }
        #endregion

        #region 无痕模式
        private void BtnIncognito_Click(object sender, RoutedEventArgs e) => NewIncognitoTab();

        private void BtnOpenPdf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PDF 文件 (*.pdf)|*.pdf|所有文件 (*.*)|*.*",
                Title = "打开本地 PDF"
            };
            if (dlg.ShowDialog() == true)
            {
                var fullPath = dlg.FileName;
                var url = "file:///" + fullPath.Replace("\\", "/");
                NewTabWithUrl(url);
                var tab = _tabs.LastOrDefault();
                if (tab != null)
                {
                    tab.Title = System.IO.Path.GetFileName(fullPath);
                    tab.IsPdf = true;
                }
            }
        }
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

        #region 标签管理：搜索、分组、冻结、PDF

        // ===== 标签搜索 =====
        private void BtnTabSearch_Click(object sender, RoutedEventArgs e)
        {
            _tabSearchVisible = !_tabSearchVisible;
            txtTabSearch.Visibility = _tabSearchVisible ? Visibility.Visible : Visibility.Collapsed;
            if (_tabSearchVisible) txtTabSearch.Focus();
            else { txtTabSearch.Text = ""; ApplyTabFilter(""); }
        }

        private void TxtTabSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyTabFilter(txtTabSearch.Text?.Trim() ?? "");
        }

        private void ApplyTabFilter(string keyword)
        {
            foreach (TabInfo tab in _tabs)
            {
                var lbi = tabList.ItemContainerGenerator.ContainerFromItem(tab) as ListBoxItem;
                if (lbi == null) continue;
                if (string.IsNullOrEmpty(keyword))
                {
                    lbi.Visibility = Visibility.Visible;
                }
                else
                {
                    bool match = (tab.Title?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (tab.Group?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false);
                    lbi.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        // ===== 标签分组/工作区 =====
        private void MenuItemAssignGroup_Click(object sender, RoutedEventArgs e)
        {
            if (tabList.SelectedItem is not TabInfo tab) return;
            var dlg = new Window
            {
                Title = "分配到分组",
                Width = 360, Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = (Brush)FindResource("WindowBg")
            };
            var panel = new StackPanel { Margin = new Thickness(16) };
            var label = new TextBlock
            {
                Text = "输入分组名称（如：工作、学习、社交）：",
                FontSize = 13, Foreground = (Brush)FindResource("TextColor"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(label);
            var input = new TextBox
            {
                Text = tab.Group, Height = 32, FontSize = 13,
                Style = (Style)FindResource("UrlBox"),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(input);
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var btnOk = new Button { Content = "确定", Width = 72, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
            var btnCancel = new Button { Content = "取消", Width = 72, Height = 30 };
            btnOk.Click += (s2, e2) => { dlg.DialogResult = true; dlg.Close(); };
            btnCancel.Click += (s2, e2) => { dlg.DialogResult = false; dlg.Close(); };
            btnRow.Children.Add(btnOk);
            btnRow.Children.Add(btnCancel);
            panel.Children.Add(btnRow);
            dlg.Content = panel;
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(input.Text))
            {
                tab.Group = input.Text.Trim();
                tab.LastActiveTime = DateTime.Now;
            }
        }

        private void MenuItemRemoveGroup_Click(object sender, RoutedEventArgs e)
        {
            if (tabList.SelectedItem is TabInfo tab)
                tab.Group = "";
        }

        private void MenuItemCloseGroup_Click(object sender, RoutedEventArgs e)
        {
            if (tabList.SelectedItem is not TabInfo tab) return;
            var group = tab.Group;
            if (string.IsNullOrEmpty(group)) return;
            var toClose = _tabs.Where(t => t.Group == group).ToList();
            foreach (var t in toClose) CloseTab(t);
        }

        private void MenuItemCopyTabUrl_Click(object sender, RoutedEventArgs e)
        {
            if (tabList.SelectedItem is TabInfo tab &&
                _webViews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null)
            {
                try { Clipboard.SetText(wv.CoreWebView2.Source); } catch { }
            }
        }

        private void TabList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // 确保右键时选中对应标签
            if (e.OriginalSource is DependencyObject dep)
            {
                var lbi = FindParent<ListBoxItem>(dep);
                if (lbi?.DataContext is TabInfo tab)
                    tabList.SelectedItem = tab;
            }
        }

        // ===== 批量折叠分组 =====
        private void BtnCollapseGroups_Click(object sender, RoutedEventArgs e)
        {
            _groupsCollapsed = !_groupsCollapsed;
            // 切换所有分组的折叠状态
            var groups = _tabs.Where(t => !string.IsNullOrEmpty(t.Group))
                              .Select(t => t.Group).Distinct().ToList();
            if (_groupsCollapsed)
            {
                foreach (var g in groups)
                {
                    _collapsedGroups[g] = true;
                    CollapseGroup(g, true);
                }
            }
            else
            {
                foreach (var g in groups)
                {
                    _collapsedGroups[g] = false;
                    CollapseGroup(g, false);
                }
            }
        }

        private void CollapseGroup(string group, bool collapse)
        {
            foreach (TabInfo tab in _tabs)
            {
                if (tab.Group != group) continue;
                var lbi = tabList.ItemContainerGenerator.ContainerFromItem(tab) as ListBoxItem;
                if (lbi != null) lbi.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        // ===== 冻结闲置标签 =====
        private void BtnFreezeIdle_Click(object sender, RoutedEventArgs e)
        {
            var idleTabs = _tabs.Where(t => !t.IsFrozen &&
                t != tabList.SelectedItem &&
                !t.IsIncognito &&
                (DateTime.Now - t.LastActiveTime).TotalMinutes >= 5).ToList();
            foreach (var tab in idleTabs)
                FreezeTab(tab);
            if (idleTabs.Count > 0)
                MessageBox.Show($"已冻结 {idleTabs.Count} 个闲置标签", "完成");
            else
                MessageBox.Show("没有需要冻结的闲置标签", "提示");
        }

        private void MenuItemFreezeTab_Click(object sender, RoutedEventArgs e)
        {
            if (tabList.SelectedItem is TabInfo tab)
            {
                if (tab.IsFrozen) UnfreezeTab(tab);
                else FreezeTab(tab);
            }
        }

        private void FreezeTab(TabInfo tab)
        {
            if (tab.IsFrozen) return;
            if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 == null) return;
            try
            {
                tab.FrozenUrl = wv.CoreWebView2.Source;
                tab.Title = tab.Title + " [已冻结]";
                tab.IsFrozen = true;
                // 隐藏并导航到空白页释放内存
                wv.Visibility = Visibility.Collapsed;
                wv.CoreWebView2.Navigate("about:blank");
            }
            catch { }
        }

        private void UnfreezeTab(TabInfo tab)
        {
            if (!tab.IsFrozen) return;
            if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 == null) return;
            try
            {
                // 恢复标题（去掉 [已冻结] 后缀）
                if (tab.Title.EndsWith(" [已冻结]"))
                    tab.Title = tab.Title[..^" [已冻结]".Length];
                tab.IsFrozen = false;
                tab.LastActiveTime = DateTime.Now;
                if (!string.IsNullOrEmpty(tab.FrozenUrl))
                    wv.CoreWebView2.Navigate(tab.FrozenUrl);
                tab.FrozenUrl = null;
            }
            catch { }
        }

        private void CheckAndFreezeIdleTabs()
        {
            if (!_config.TabFreezeEnabled) return;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    var threshold = DateTime.Now.AddMinutes(-_config.TabFreezeMinutes);
                    var idleTabs = _tabs.Where(t => !t.IsFrozen &&
                        t != tabList.SelectedItem &&
                        !t.IsIncognito &&
                        !t.IsPdf &&
                        t.LastActiveTime < threshold).ToList();
                    foreach (var tab in idleTabs)
                        FreezeTab(tab);
                });
            }
            catch { }
        }

        // ===== PDF 原生处理 =====
        private void SetupPdfHandling(Microsoft.Web.WebView2.Wpf.WebView2 wv, TabInfo tab)
        {
            wv.CoreWebView2.SourceChanged += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    var url = wv.CoreWebView2?.Source ?? "";
                    tab.IsPdf = url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                                url.Contains(".pdf?", StringComparison.OrdinalIgnoreCase);
                    if (tab.IsPdf)
                    {
                        _ = wv.CoreWebView2.ExecuteScriptAsync(PdfToolbarScript);
                        if (tabList.SelectedItem == tab)
                            ShowPdfSidePanel();
                    }
                    else
                    {
                        if (tabList.SelectedItem == tab)
                            HidePdfSidePanel();
                    }
                });
            };
        }

        private const string PdfToolbarScript = @"
(function(){
    if(window.__pdfAnnoInstalled) return;
    window.__pdfAnnoInstalled = true;

    var state = {
        mode: null,
        color: '#ffeb3b',
        textColor: '#dc2626',
        fontSize: 18,
        lineWidth: 3,
        undoStack: [],
        drawing: false,
        startX: 0, startY: 0,
        shape: null,
        freePoints: null,
        freePath: null
    };

    function wrapSelected(baseStyle, wrapperTag){
        var s = window.getSelection();
        if(!s||s.rangeCount===0||!s.toString().trim()) { return false; }
        var r = s.getRangeAt(0);
        try{
            var span = document.createElement(wrapperTag||'span');
            span.style.cssText = baseStyle;
            span.setAttribute('data-pdf-anno','1');
            r.surroundContents(span);
            pushUndo(function(){ span.remove(); });
        }catch(ex){ return false; }
        s.removeAllRanges();
        return true;
    }

    function pushUndo(fn){ state.undoStack.push(fn); }

    function ensureSvg(){
        var id='__pdfsvg';
        var svg=document.getElementById(id);
        if(!svg){
            svg=document.createElementNS('http://www.w3.org/2000/svg','svg');
            svg.id=id;svg.setAttribute('data-pdf-anno','1');
            svg.style.cssText='position:absolute;left:0;top:0;width:100%;height:100%;pointer-events:none;z-index:999997;';
            document.body.appendChild(svg);
        }
        return svg;
    }

    function exitMode(){
        state.mode = null;
        document.body.style.cursor='default';
        document.onmousedown=null;
        document.onmousemove=null;
        document.onmouseup=null;
    }

    window.__pdfAnno = {
        setColor: function(c){ state.color = c; },
        setTextColor: function(c){ state.textColor = c; },
        setFontSize: function(s){ state.fontSize = parseInt(s,10); },
        setLineWidth: function(w){ state.lineWidth = parseInt(w,10); },

        highlight: function(){
            var ok = wrapSelected('background:'+state.color+';opacity:0.65;');
            return ok;
        },
        underline: function(){
            var ok = wrapSelected('background:transparent;border-bottom:'+state.lineWidth+'px solid '+state.color+';padding-bottom:1px;');
            return ok;
        },
        strikeout: function(){
            var ok = wrapSelected('text-decoration:line-through;text-decoration-color:'+state.color+';text-decoration-thickness:'+state.lineWidth+'px;');
            return ok;
        },
        addTextbox: function(){
            var box=document.createElement('div');
            box.contentEditable='true';
            box.spellcheck=false;
            box.setAttribute('data-pdf-anno','1');
            box.className='pdf-anno';
            box.style.cssText='position:absolute;top:80px;left:80px;min-width:180px;max-width:420px;padding:10px 12px;border-radius:6px;background:'+state.color+';color:'+state.textColor+';font-size:'+state.fontSize+'px;line-height:1.5;box-shadow:0 4px 12px rgba(0,0,0,0.2);cursor:move;z-index:999998;word-break:break-word;';
            box.textContent='输入文字...';
            box.onclick = function(){ if(box.textContent==='输入文字...') box.textContent=''; };
            var dragging=false,dragDX=0,dragDY=0;
            box.onmousedown = function(e){
                if(window.getSelection().toString()) return;
                dragging=true;
                var rect = box.getBoundingClientRect();
                dragDX=e.clientX-rect.left;dragDY=e.clientY-rect.top;
                e.stopPropagation();
            };
            document.addEventListener('mousemove',function(e){
                if(!dragging) return;
                box.style.left=(e.clientX-dragDX)+'px';
                box.style.top=(e.clientY-dragDY)+'px';
            });
            document.addEventListener('mouseup',function(){ dragging=false; });
            document.body.appendChild(box);
            pushUndo(function(){ box.remove(); });
        },

        startFreehand: function(){
            state.mode='freehand';
            document.body.style.cursor='crosshair';
            var getX=function(e){ return e.clientX+window.scrollX; };
            var getY=function(e){ return e.clientY+window.scrollY; };
            document.onmousedown = function(e){
                state.drawing=true;
                state.startX=getX(e);state.startY=getY(e);
                state.freePoints = [{x:state.startX,y:state.startY}];
                state.freePath = document.createElementNS('http://www.w3.org/2000/svg','path');
                state.freePath.setAttribute('fill','none');
                state.freePath.setAttribute('stroke',state.color);
                state.freePath.setAttribute('stroke-width',state.lineWidth);
                state.freePath.setAttribute('stroke-linecap','round');
                state.freePath.setAttribute('stroke-linejoin','round');
                state.shape = ensureSvg();
                state.shape.appendChild(state.freePath);
                e.preventDefault();
            };
            document.onmousemove = function(e){
                if(!state.drawing) return;
                var x=getX(e),y=getY(e);
                state.freePoints.push({x:x,y:y});
                var d='M'+state.freePoints.map(function(p){return p.x+' '+p.y;}).join(' L');
                state.freePath.setAttribute('d',d);
            };
            document.onmouseup = function(){
                if(!state.drawing) return;
                state.drawing=false;
                var svg=state.shape;
                pushUndo(function(){ if(svg && svg.parentNode) svg.parentNode.removeChild(svg); });
                exitMode();
            };
        },

        startRect: function(){
            state.mode='rect';
            document.body.style.cursor='crosshair';
            var getX=function(e){ return e.clientX+window.scrollX; };
            var getY=function(e){ return e.clientY+window.scrollY; };
            document.onmousedown = function(e){
                state.drawing=true;
                state.startX=getX(e);state.startY=getY(e);
                state.shape = document.createElement('div');
                state.shape.className='pdf-anno';
                state.shape.setAttribute('data-pdf-anno','1');
                state.shape.style.cssText='position:absolute;border:'+state.lineWidth+'px solid '+state.color+';border-radius:2px;pointer-events:none;z-index:999997;';
                state.shape.style.left=state.startX+'px';
                state.shape.style.top=state.startY+'px';
                document.body.appendChild(state.shape);
                e.preventDefault();
            };
            document.onmousemove = function(e){
                if(!state.drawing) return;
                var x=getX(e),y=getY(e);
                var left=Math.min(state.startX,x);
                var top=Math.min(state.startY,y);
                var w=Math.abs(x-state.startX);
                var h=Math.abs(y-state.startY);
                state.shape.style.left=left+'px';
                state.shape.style.top=top+'px';
                state.shape.style.width=w+'px';
                state.shape.style.height=h+'px';
            };
            document.onmouseup = function(){
                if(!state.drawing) return;
                state.drawing=false;
                var el=state.shape;
                pushUndo(function(){ el.remove(); });
                exitMode();
            };
        },

        undo: function(){
            if(state.undoStack.length===0) return;
            try{ state.undoStack.pop()(); }catch(_){}
        },

        clear: function(){
            document.querySelectorAll('[data-pdf-anno=1],.pdf-anno').forEach(function(n){n.remove();});
            state.undoStack = [];
        },

        exitMode: exitMode,

        exportPdf: function(){
            var a=document.createElement('a');a.href=window.location.href;a.download=(document.title||'document')+'.pdf';a.click();
        },

        print: function(){ window.print(); }
    };

    document.addEventListener('keydown',function(e){
        if(e.key==='Escape') exitMode();
        if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='z'){ e.preventDefault(); window.__pdfAnno.undo(); }
    });
})();
";

        #endregion

        #region PDF 侧边批注面板
        private string _currentPdfColor = "#ffeb3b";
        private string _currentPdfTextColor = "#dc2626";

        private static readonly string[] HighlightColors = { "#ffeb3b", "#4ade80", "#f87171", "#60a5fa", "#c084fc", "#fb923c" };
        private static readonly string[] TextColors = { "#dc2626", "#ea580c", "#16a34a", "#2563eb", "#7c3aed", "#111827", "#ffffff" };

        private static readonly (string icon, string tooltip, string action)[] PdfTools =
        {
            ("H", "高亮 (选中文字后点此)", "highlight"),
            ("U", "下划线", "underline"),
            ("S", "删除线", "strikeout"),
            ("T", "文字批注 (插入便签)", "textbox"),
            ("✏", "自由画线", "freehand"),
            ("▭", "矩形框", "rect")
        };

        private void InitPdfPanel()
        {
            // 高亮颜色
            for (int i = 0; i < HighlightColors.Length; i++)
            {
                var color = HighlightColors[i];
                var btn = new Button
                {
                    Width = 26, Height = 26,
                    BorderThickness = new Thickness(0),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                    Cursor = Cursors.Hand,
                    Tag = color,
                    ToolTip = color
                };
                btn.Click += (s, e) => { _currentPdfColor = color; PdfExec($"window.__pdfAnno.setColor('{color}');"); UpdatePdfColorSelection(); };
                highlightColorGrid.Children.Add(btn);
            }
            // 文字颜色
            for (int i = 0; i < TextColors.Length; i++)
            {
                var color = TextColors[i];
                var btn = new Button
                {
                    Width = 26, Height = 26,
                    BorderThickness = new Thickness(0),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                    Cursor = Cursors.Hand,
                    Tag = color,
                    ToolTip = color
                };
                btn.Click += (s, e) => { _currentPdfTextColor = color; PdfExec($"window.__pdfAnno.setTextColor('{color}');"); UpdatePdfColorSelection(); };
                textColorGrid.Children.Add(btn);
            }
            // 批注工具
            for (int i = 0; i < PdfTools.Length; i++)
            {
                var (icon, tooltip, action) = PdfTools[i];
                var btn = new Button
                {
                    Height = 30,
                    Content = icon,
                    ToolTip = tooltip,
                    Tag = action,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold
                };
                btn.Style = (Style)FindResource("ToolButton");
                btn.Click += (s, e) => PdfTool_Click(action);
                pdfToolGrid.Children.Add(btn);
            }
            UpdatePdfColorSelection();
        }

        private void UpdatePdfColorSelection()
        {
            foreach (Button btn in highlightColorGrid.Children)
            {
                var c = btn.Tag as string;
                btn.BorderThickness = new Thickness(c == _currentPdfColor ? 3 : 2);
                btn.BorderBrush = c == _currentPdfColor ?
                    Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString(c));
            }
            foreach (Button btn in textColorGrid.Children)
            {
                var c = btn.Tag as string;
                btn.BorderThickness = new Thickness(c == _currentPdfTextColor ? 3 : 2);
                btn.BorderBrush = c == _currentPdfTextColor ?
                    Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString(c));
            }
        }

        private void ShowPdfSidePanel()
        {
            pdfSidePanel.Width = 0;
            pdfSidePanel.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(new Action(() => AnimateWidth(pdfSidePanel, 220)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void HidePdfSidePanel()
        {
            AnimateWidth(pdfSidePanel, 0);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            t.Tick += (s, e) => { pdfSidePanel.Visibility = Visibility.Collapsed; t.Stop(); };
            t.Start();
        }

        private void PdfTool_Click(string action)
        {
            var fs = pdfFontSize.SelectedItem as ComboBoxItem;
            if (int.TryParse(fs?.Content?.ToString(), out int sz))
                PdfExec($"window.__pdfAnno.setFontSize({sz});");
            PdfExec($"window.__pdfAnno.setLineWidth({(int)pdfStrokeWidth.Value});");

            switch (action)
            {
                case "highlight":
                    PdfExec($"window.__pdfAnno.setColor('{_currentPdfColor}');var r=window.__pdfAnno.highlight();if(!r)alert('请先选中文字');");
                    break;
                case "underline":
                    PdfExec($"window.__pdfAnno.setColor('{_currentPdfColor}');var r=window.__pdfAnno.underline();if(!r)alert('请先选中文字');");
                    break;
                case "strikeout":
                    PdfExec($"window.__pdfAnno.setColor('{_currentPdfColor}');var r=window.__pdfAnno.strikeout();if(!r)alert('请先选中文字');");
                    break;
                case "textbox":
                    PdfExec($"window.__pdfAnno.setColor('{_currentPdfColor}');window.__pdfAnno.addTextbox();");
                    break;
                case "freehand":
                    PdfExec($"window.__pdfAnno.setColor('{_currentPdfColor}');window.__pdfAnno.startFreehand();");
                    break;
                case "rect":
                    PdfExec($"window.__pdfAnno.setColor('{_currentPdfColor}');window.__pdfAnno.startRect();");
                    break;
            }
        }

        private void BtnPdfUndo_Click(object sender, RoutedEventArgs e) =>
            PdfExec("window.__pdfAnno.undo();");

        private void BtnPdfClear_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定清除所有批注？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                PdfExec("window.__pdfAnno.clear();");
        }

        private void BtnPdfExport_Click(object sender, RoutedEventArgs e) =>
            PdfExec("window.__pdfAnno.exportPdf();");

        private void BtnPdfPrint_Click(object sender, RoutedEventArgs e) =>
            PdfExec("window.__pdfAnno.print();");

        private void BtnPdfPanelCollapse_Click(object sender, RoutedEventArgs e)
        {
            HidePdfSidePanel();
        }

        private void PdfExec(string script)
        {
            if (tabList.SelectedItem is not TabInfo tab) return;
            if (_webViews.TryGetValue(tab.Id, out var wv) && wv.CoreWebView2 != null)
                _ = wv.CoreWebView2.ExecuteScriptAsync(script);
        }

        #endregion

        #region 标签栏伸缩
        private void TabSidebar_MouseEnter(object sender, MouseEventArgs e)
        {
            IsSidebarExpanded = true;
            txtNewTabLabel.Visibility = Visibility.Visible;
            txtSearchLabel.Visibility = Visibility.Visible;
            txtCollapseLabel.Visibility = Visibility.Visible;
            txtFreezeLabel.Visibility = Visibility.Visible;
            txtTabSearch.Visibility = _tabSearchVisible ? Visibility.Visible : Visibility.Collapsed;
            AnimateWidth(tabSidebar, SidebarExpanded);
            AnimateMargin(webArea, NavMarginExpanded, 6, 6, 6);
            AnimateMargin(settingsPage, NavMarginExpanded, 6, 6, 6);
        }

        private void TabSidebar_MouseLeave(object sender, MouseEventArgs e)
        {
            IsSidebarExpanded = false;
            txtNewTabLabel.Visibility = Visibility.Collapsed;
            txtSearchLabel.Visibility = Visibility.Collapsed;
            txtCollapseLabel.Visibility = Visibility.Collapsed;
            txtFreezeLabel.Visibility = Visibility.Collapsed;
            txtTabSearch.Visibility = Visibility.Collapsed;
            _tabSearchVisible = false;
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
                // 关键字触发检测：输入 "gh wpf webview2" → GitHub 搜索
                var spaceIdx = input.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    var kw = input[..spaceIdx];
                    var query = input[(spaceIdx + 1)..].Trim();
                    if (!string.IsNullOrEmpty(query))
                    {
                        var engine = _config.SearchEngines.FirstOrDefault(x =>
                            string.Equals(x.Keyword, kw, StringComparison.OrdinalIgnoreCase));
                        if (engine != null && !string.IsNullOrEmpty(engine.SearchUrl))
                        {
                            url = engine.SearchUrl.Replace("%s", Uri.EscapeDataString(query));
                            DoNavigate(url);
                            return;
                        }
                    }
                }

                // 默认引擎搜索
                var defaultEngine = _config.SearchEngines.FirstOrDefault(x => x.Name == _config.DefaultEngine)
                    ?? _config.SearchEngines[0];
                var template = string.IsNullOrEmpty(defaultEngine.SearchUrl)
                    ? defaultEngine.UrlTemplate  // 兼容旧 {0} 格式
                    : defaultEngine.SearchUrl;
                url = template.Contains("%s")
                    ? template.Replace("%s", Uri.EscapeDataString(input))
                    : template.Replace("{0}", Uri.EscapeDataString(input));
            }

            DoNavigate(url);
        }

        private void DoNavigate(string url)
        {
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

        // ========== 地址栏联想 (v1.5.0) ==========

        /// <summary>
        /// 地址栏文本变化：
        ///  1. 立刻本地 SQLite 查询（同步，无网络，毫秒级）
        ///  2. 防抖 250ms 后发起云端联想（失败静默，仅保留本地）
        ///  3. 每次输入立即取消上次云端请求，解决竞态覆盖
        /// </summary>
        private async void TxtUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 用户在 Popup 内键盘选择时，TxtUrl 会被 SetText 再次触发；此时不重开 Popup
            if (_suppressTextChanged)
            {
                _suppressTextChanged = false;
                return;
            }

            var text = txtUrl.Text?.Trim() ?? "";
            _suggestItems.Clear();
            SuggestPopup.IsOpen = false;
            if (string.IsNullOrEmpty(text))
            {
                _suggestCts?.Cancel();
                return;
            }

            // 先出本地结果（书签>历史 优先级由 QueryLocalSuggest 内部保证）
            List<AddressSuggestItem> localCandidates = new();
            if (EnableLocalSuggest && _localDb != null)
            {
                try
                {
                    localCandidates = _localDb.QueryLocalSuggest(text, SuggestTakeLocalEach);
                }
                catch { /* 数据库异常：降级为仅云端 */ }
            }
            else if (EnableLocalSuggest)
            {
                // SQLite 不可用，退回到内存中历史/书签
                localCandidates = FallbackQueryLocalFromMemory(text, SuggestTakeLocalEach);
            }
            foreach (var it in localCandidates) _suggestItems.Add(it);

            if (_suggestItems.Count > 0)
            {
                OpenSuggestPopup();
                SuggestListBox.SelectedIndex = -1;
            }

            // ========== 云端联想（防抖 250ms + 取消上一次）==========
            _suggestCts?.Cancel();
            _suggestCts = new CancellationTokenSource();
            var token = _suggestCts.Token;

            // 无痕模式或开关关闭时，不发网络（直接保留本地候选）
            bool isIncognito = tabList.SelectedItem is TabInfo t && t.IsIncognito;
            if (!EnableCloudSuggest || isIncognito) return;

            try
            {
                await Task.Delay(SuggestDebounceMs, token).ConfigureAwait(true);
                if (token.IsCancellationRequested) return;

                // 默认引擎有 SuggestUrl 就用对应引擎，否则百度兜底（覆盖面更广）
                SearchEngine? defaultEngine = _config.SearchEngines.FirstOrDefault(x => x.Name == _config.DefaultEngine)
                    ?? _config.SearchEngines.FirstOrDefault();
                List<AddressSuggestItem> cloud;
                if (defaultEngine != null && !string.IsNullOrWhiteSpace(defaultEngine.SuggestUrl)
                    && defaultEngine.SuggestUrl.Contains("%s"))
                {
                    cloud = await FetchEngineSuggest(defaultEngine, text, token).ConfigureAwait(true);
                }
                else
                {
                    cloud = await FetchBaiduSuggest(text, token).ConfigureAwait(true);
                }

                if (token.IsCancellationRequested) return;

                // 合并云端（去重：URL 唯一；纯搜索词 Url=生成链接，也和本地不重叠）
                var existUrls = new HashSet<string>(
                    _suggestItems.Select(x => x.Url), StringComparer.OrdinalIgnoreCase);
                int added = 0;
                foreach (var s in cloud)
                {
                    if (added >= SuggestCloudTake) break;
                    if (existUrls.Add(s.Url))
                    {
                        _suggestItems.Add(s);
                        added++;
                    }
                }

                if (_suggestItems.Count > 0)
                {
                    OpenSuggestPopup();
                    if (SuggestListBox.SelectedIndex < 0 && localCandidates.Count == 0)
                        SuggestListBox.SelectedIndex = 0; // 仅有云端结果时默认首项
                }
            }
            catch (TaskCanceledException) { /* 用户继续输入 / 超时：取消，正常 */ }
            catch (OperationCanceledException) { /* 用户继续输入，正常 */ }
            catch (HttpRequestException) { /* 网络失败：保持本地 */ }
            catch { /* 其他异常：静默 */ }
        }

        private bool _suppressTextChanged;

        private void OpenSuggestPopup()
        {
            try
            {
                // 让 Popup 宽度跟随地址栏
                if (SuggestPopup.Child is Border b)
                    b.Width = Math.Max(txtUrl.ActualWidth, 480);
                SuggestPopup.IsOpen = true;
            }
            catch { }
        }

        private void TxtUrl_LostFocus(object sender, RoutedEventArgs e)
        {
            // 点击 Popup 中的项时也会触发 LostFocus；延迟一小会儿关闭，
            // 让 SelectionChanged 先处理，之后再关
            if (SuggestPopup.IsKeyboardFocusWithin || SuggestListBox.IsMouseOver) return;
            SuggestPopup.IsOpen = false;
        }

        /// <summary>当 SQLite 不可用时回退到内存 JSON 做联想</summary>
        private List<AddressSuggestItem> FallbackQueryLocalFromMemory(string keyword, int take)
        {
            var list = new List<AddressSuggestItem>();
            var kw = keyword.Trim();
            // 书签优先
            foreach (var bm in _bookmarks
                         .Where(b =>
                             (!string.IsNullOrEmpty(b.Title) && b.Title.Contains(kw, StringComparison.OrdinalIgnoreCase))
                             || (!string.IsNullOrEmpty(b.Url) && b.Url.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                         .Take(take))
            {
                list.Add(new AddressSuggestItem
                {
                    Text = string.IsNullOrWhiteSpace(bm.Title) ? bm.Url : bm.Title,
                    Url = bm.Url,
                    Source = SuggestSource.LocalBookmark
                });
            }
            foreach (var h in _allHistory
                         .Where(h =>
                             (!string.IsNullOrEmpty(h.Title) && h.Title.Contains(kw, StringComparison.OrdinalIgnoreCase))
                             || (!string.IsNullOrEmpty(h.Url) && h.Url.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                         .Take(take))
            {
                list.Add(new AddressSuggestItem
                {
                    Text = string.IsNullOrWhiteSpace(h.Title) ? h.Url : h.Title,
                    Url = h.Url,
                    Source = SuggestSource.LocalHistory
                });
            }
            return list;
        }

        /// <summary>
        /// 按当前默认引擎的 SuggestUrl 调用云端联想。
        /// 百度特殊处理（JSONP），其他走普通 JSON（Bing osjson、搜狗 suggest、360 suggest 等）。
        /// 调用失败时抛异常，由上层统一"保留本地结果静默降级"。
        /// </summary>
        private async Task<List<AddressSuggestItem>> FetchEngineSuggest(SearchEngine engine, string keyword, CancellationToken ct)
        {
            var url = engine.SuggestUrl.Replace("%s", Uri.EscapeDataString(keyword));
            // 百度：特殊 JSONP 格式（window.baidu.sug({...})），兜底函数
            if (!string.IsNullOrWhiteSpace(engine.Name) &&
                (engine.Name == "百度" || url.Contains("suggestion.baidu.com", StringComparison.OrdinalIgnoreCase)))
                return await FetchBaiduSuggest(keyword, ct).ConfigureAwait(false);

            string resp = await _httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resp)) return new List<AddressSuggestItem>();
            var result = new List<AddressSuggestItem>();

            using var doc = JsonDocument.Parse(resp);
            var root = doc.RootElement;
            IEnumerable<JsonElement>? arr = null;
            switch (root.ValueKind)
            {
                case JsonValueKind.Array:
                    // 必应 osjson：["query",["word1","word2",...]]
                    if (root.GetArrayLength() >= 2 && root[1].ValueKind == JsonValueKind.Array)
                        arr = root[1].EnumerateArray();
                    else
                        arr = root.EnumerateArray();
                    break;
                case JsonValueKind.Object:
                    if (root.TryGetProperty("s", out var sArr) && sArr.ValueKind == JsonValueKind.Array)
                        arr = sArr.EnumerateArray();
                    break;
            }
            if (arr == null) return result;
            foreach (var e in arr)
            {
                string? word = e.ValueKind switch
                {
                    JsonValueKind.String => e.GetString(),
                    JsonValueKind.Object => (e.TryGetProperty("q", out var q) ? q.GetString() :
                                             e.TryGetProperty("keyword", out var k) ? k.GetString() :
                                             e.TryGetProperty("word", out var w) ? w.GetString() : null),
                    _ => null
                };
                if (string.IsNullOrWhiteSpace(word)) continue;
                var searchUrl = (engine.SearchUrl?.Contains("%s") ?? false)
                    ? engine.SearchUrl.Replace("%s", Uri.EscapeDataString(word))
                    : $"https://www.baidu.com/s?wd={Uri.EscapeDataString(word)}";
                result.Add(new AddressSuggestItem
                {
                    Text = word,
                    Url = searchUrl,
                    Source = SuggestSource.CloudSearch
                });
            }
            return result;
        }

        /// <summary>百度联想（非官方接口，JSONP）</summary>
        private async Task<List<AddressSuggestItem>> FetchBaiduSuggest(string keyword, CancellationToken ct)
        {
            var url = $"https://suggestion.baidu.com/su?wd={Uri.EscapeDataString(keyword)}&json=1";
            var resp = await _httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resp)) return new List<AddressSuggestItem>();
            // 百度返回：window.baidu.sug({...})
            int start = resp.IndexOf('{');
            int end = resp.LastIndexOf('}');
            if (start < 0 || end < 0 || end <= start) return new List<AddressSuggestItem>();
            var json = resp.Substring(start, end - start + 1);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("s", out var sArr) || sArr.ValueKind != JsonValueKind.Array)
                return new List<AddressSuggestItem>();
            var result = new List<AddressSuggestItem>();
            foreach (var e in sArr.EnumerateArray())
            {
                var word = e.GetString();
                if (string.IsNullOrWhiteSpace(word)) continue;
                result.Add(new AddressSuggestItem
                {
                    Text = word,
                    Url = $"https://www.baidu.com/s?wd={Uri.EscapeDataString(word)}",
                    Source = SuggestSource.CloudSearch
                });
            }
            return result;
        }

        // ===== Popup 键盘导航（与 Edge 对齐）：↑↓ 移动、Enter 跳转、Esc 关闭、Tab 关闭 =====

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (!SuggestPopup.IsOpen) return;

            switch (e.Key)
            {
                case Key.Escape:
                    SuggestPopup.IsOpen = false;
                    e.Handled = true;
                    return;
                case Key.Down:
                    if (_suggestItems.Count == 0) return;
                    if (SuggestListBox.SelectedIndex < _suggestItems.Count - 1)
                        SuggestListBox.SelectedIndex++;
                    else
                        SuggestListBox.SelectedIndex = 0;
                    SuggestListBox.ScrollIntoView(SuggestListBox.SelectedItem);
                    e.Handled = true;
                    return;
                case Key.Up:
                    if (_suggestItems.Count == 0) return;
                    if (SuggestListBox.SelectedIndex <= 0)
                        SuggestListBox.SelectedIndex = _suggestItems.Count - 1;
                    else
                        SuggestListBox.SelectedIndex--;
                    SuggestListBox.ScrollIntoView(SuggestListBox.SelectedItem);
                    e.Handled = true;
                    return;
                case Key.Enter:
                    if (SuggestListBox.SelectedItem is AddressSuggestItem sel)
                    {
                        ApplySuggestItem(sel);
                        e.Handled = true;
                    }
                    return;
                case Key.Tab:
                    SuggestPopup.IsOpen = false;
                    return;
            }
        }

        private void SuggestListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // ListBox 中键盘事件仍交给窗口统一处理；防止 ListBox 吞掉 Enter/ESC
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                OnPreviewKeyDown(e);
                e.Handled = true;
            }
        }

        private void SuggestListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 鼠标单击（不按键盘时）：立即应用候选项
            if (e.AddedItems.Count == 0) return;
            if (SuggestListBox.SelectedItem is not AddressSuggestItem it) return;
            // 只响应鼠标点击触发的 SelectionChanged：如果键盘焦点仍在地址栏里且是键盘选到的就先别跳转
            if (SuggestListBox.IsMouseOver || Mouse.LeftButton == MouseButtonState.Released && Keyboard.FocusedElement is ListBoxItem)
            {
                ApplySuggestItem(it);
            }
        }

        /// <summary>把候选项应用到地址栏并导航</summary>
        private void ApplySuggestItem(AddressSuggestItem it)
        {
            SuggestPopup.IsOpen = false;
            // 云端搜索词：直接跳转到生成的搜索 URL
            // 本地历史/书签：也跳 Url；并且把输入框文字设置为 URL 看起来更顺
            _suppressTextChanged = true;
            txtUrl.Text = it.Url;
            txtUrl.SelectionStart = txtUrl.Text.Length;
            Navigate(it.Url);
        }

        #endregion

        #region 用户管理（profile 切换/新建/删除）
        /// <summary>计算目录占用大小（MB）</summary>
        private static string GetDirectorySizeText(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return "";
                long bytes = 0;
                foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                    bytes += f.Length;
                return bytes switch
                {
                    < 1024 => $"{bytes} B",
                    < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
                    < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
                    _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB"
                };
            }
            catch { return ""; }
        }

        /// <summary>读取所有 profile 名（基于 exe 目录下 Profiles 子目录）</summary>
        private List<string> GetAllProfileNames()
        {
            var result = new List<string>();
            try
            {
                var profilesRoot = Path.Combine(App.DataDir, "Profiles");
                // 优先取 exe 同级 Profiles（与 App.ParseArgs 一致）
                var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? App.DataDir;
                profilesRoot = Path.Combine(exeDir, "Profiles");
                if (Directory.Exists(profilesRoot))
                {
                    foreach (var d in Directory.GetDirectories(profilesRoot))
                        result.Add(Path.GetFileName(d));
                }
            }
            catch { }
            return result.OrderBy(n => n).ToList();
        }

        /// <summary>刷新设置页的用户列表</summary>
        private void LoadProfiles()
        {
            if (lbSettingsNav.SelectedItem is SettingsNavItem item && item.Tag == "user")
            {
                settingsContent.Content = BuildUserPage();
            }
        }

        private void BtnNewProfile_Click(object sender, RoutedEventArgs e)
        {
            // 用原生 InputBox 替代：简单字符串输入对话框
            var dlg = new SimpleInputDialog("新建用户", "请输入新用户名（仅字母/数字/中文，不可含特殊字符）：", "");
            dlg.Owner = this;
            if (dlg.ShowDialog() != true) return;
            var name = dlg.InputText;
            if (string.IsNullOrWhiteSpace(name)) return;

            var sanitized = App.SanitizeProfileName(name);
            if (string.IsNullOrEmpty(sanitized) || sanitized != name)
            {
                MessageBox.Show("用户名包含非法字符，请仅使用字母、数字、中文。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // 重复检查
            if (GetAllProfileNames().Contains(sanitized, StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show($"用户 [{sanitized}] 已存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            RestartWithProfile(sanitized);
        }

        private void BtnSwitchProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string name) return;
            var current = string.IsNullOrEmpty(_profileName) ? "" : _profileName;
            if (name == current)
            {
                MessageBox.Show("已经是当前用户。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            RestartWithProfile(name);
        }

        private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string name) return;
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("默认用户无法删除。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (name == _profileName)
            {
                MessageBox.Show("无法删除当前正在使用的用户，请先切换到其他用户再删除。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var r = MessageBox.Show($"确定删除用户 [{name}]？\n该用户的所有书签、历史、密码、扩展将被永久清除！",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? App.DataDir;
                var dir = Path.Combine(exeDir, "Profiles", name);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("删除失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            LoadProfiles();
            MessageBox.Show($"用户 [{name}] 已删除。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>重启到指定 profile（关闭当前实例，启动新进程）</summary>
        private void RestartWithProfile(string profile)
        {
            try
            {
                var exePath = Environment.ProcessPath!;
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };
                if (!string.IsNullOrEmpty(profile))
                    psi.ArgumentList.Add("--profile");
                if (!string.IsNullOrEmpty(profile))
                    psi.ArgumentList.Add(profile);
                System.Diagnostics.Process.Start(psi);
                // 关闭当前窗口：通过托盘退出而非最小化
                _isClosingFromTray = false;
                _trayIcon?.Dispose();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show("切换失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion
    }

    /// <summary>设置页用户列表项</summary>
    public class ProfileViewModel
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string SizeText { get; set; } = "";
    }

    /// <summary>简单的字符串输入对话框（替代 VB InputBox）</summary>
    public class SimpleInputDialog : Window
    {
        public string InputText { get; private set; } = "";
        private readonly TextBox _txt;

        public SimpleInputDialog(string title, string prompt, string defaultValue)
        {
            Title = title;
            Width = 420; Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = System.Windows.Media.Brushes.White;

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            var lbl = new TextBlock
            {
                Text = prompt,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(lbl, 0);
            grid.Children.Add(lbl);

            _txt = new TextBox
            {
                Height = 30,
                Text = defaultValue,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(_txt, 1);
            grid.Children.Add(_txt);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var ok = new Button { Content = "确定", Width = 80, Height = 30, Margin = new Thickness(4, 0, 0, 0) };
            var cancel = new Button { Content = "取消", Width = 80, Height = 30, Margin = new Thickness(4, 0, 0, 0) };
            ok.Click += (s, e) => { InputText = _txt.Text; DialogResult = true; };
            cancel.Click += (s, e) => { DialogResult = false; };
            btnPanel.Children.Add(ok);
            btnPanel.Children.Add(cancel);
            Grid.SetRow(btnPanel, 2);
            grid.Children.Add(btnPanel);

            Content = grid;
            Loaded += (s, e) => { _txt.Focus(); _txt.SelectAll(); };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { InputText = _txt.Text; DialogResult = true; }
                else if (e.Key == Key.Escape) DialogResult = false;
            };
        }
    }
}
