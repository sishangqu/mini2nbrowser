using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace mini2nbrowser
{
    /// <summary>
    /// 扩展管理窗口。
    /// 通过构造函数注入 ExtensionsManager，避免与主窗口共享状态产生耦合。
    /// </summary>
    public partial class ExtensionsWindow : Window
    {
        private readonly ExtensionsManager _manager;

        public ExtensionsWindow(ExtensionsManager manager)
        {
            InitializeComponent();
            _manager = manager;
            lbExtensions.ItemsSource = _manager.Extensions;

            // 不支持时显示提示
            if (!ExtensionsManager.IsSupported)
            {
                unsupportedTip.Visibility = Visibility.Visible;
                btnImportCrx.IsEnabled = false;
                btnImportFolder.IsEnabled = false;
                emptyState.Visibility = Visibility.Collapsed;
            }

            // 主题跟随主程序
            ApplyThemeFromApp();

            UpdateEmptyState();

            // 监听集合变化刷新空状态
            _manager.Extensions.CollectionChanged += (s, e) => UpdateEmptyState();

            // 容器生成完成后加载图标（更可靠地等到模板实例化）
            lbExtensions.ItemContainerGenerator.StatusChanged += (s, e) =>
            {
                if (lbExtensions.ItemContainerGenerator.Status ==
                    System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                {
                    Dispatcher.BeginInvoke(new Action(LoadIconsForAll),
                        System.Windows.Threading.DispatcherPriority.Background);
                }
            };

            // 窗口加载完成后也刷新一次
            Loaded += (s, e) =>
                Dispatcher.BeginInvoke(new Action(LoadIconsForAll),
                    System.Windows.Threading.DispatcherPriority.Background);

            StateChanged += (s, e) =>
            {
                if (WindowState == WindowState.Maximized)
                    rootGrid.Margin = new Thickness(0);
            };
        }

        /// <summary>从 App 当前资源复制主题字典</summary>
        private void ApplyThemeFromApp()
        {
            try
            {
                Application.Current.Resources.MergedDictionaries.ToList().ForEach(d =>
                    Resources.MergedDictionaries.Add(new ResourceDictionary { Source = d.Source }));
            }
            catch { }
        }

        private void UpdateEmptyState()
        {
            if (emptyState == null) return;
            if (!ExtensionsManager.IsSupported) { emptyState.Visibility = Visibility.Collapsed; return; }
            emptyState.Visibility = _manager.Extensions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>为列表中所有项加载图标</summary>
        private void LoadIconsForAll()
        {
            for (int i = 0; i < lbExtensions.Items.Count; i++)
            {
                var container = lbExtensions.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (container == null) continue;
                ApplyIcon(container);
            }
        }

        /// <summary>给单个 ListBoxItem 加载图标 + 设置无图标时的占位字母</summary>
        private void ApplyIcon(ListBoxItem container)
        {
            if (container.DataContext is not ExtensionInfo ext) return;
            // 找到模板里的 imgIcon / txtFallback
            var img = FindVisualChild<Image>(container);
            var fallback = FindVisualChild<TextBlock>(container);
            if (img == null || fallback == null) return;

            if (string.IsNullOrEmpty(ext.IconRelPath))
            {
                img.Visibility = Visibility.Collapsed;
                fallback.Visibility = Visibility.Visible;
                fallback.Text = string.IsNullOrEmpty(ext.Name) ? "?" : ext.Name[..1].ToUpper();
                return;
            }
            string iconFullPath = Path.Combine(ext.FolderPath, ext.IconRelPath);
            if (!File.Exists(iconFullPath))
            {
                img.Visibility = Visibility.Collapsed;
                fallback.Visibility = Visibility.Visible;
                fallback.Text = string.IsNullOrEmpty(ext.Name) ? "?" : ext.Name[..1].ToUpper();
                return;
            }
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(iconFullPath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                img.Source = bmp;
                img.Visibility = Visibility.Visible;
                fallback.Visibility = Visibility.Collapsed;
            }
            catch
            {
                img.Visibility = Visibility.Collapsed;
                fallback.Visibility = Visibility.Visible;
                fallback.Text = string.IsNullOrEmpty(ext.Name) ? "?" : ext.Name[..1].ToUpper();
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        #region 标题栏拖动
        private void TitleBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject src)
            {
                if (FindVisualParent<ButtonBase>(src) != null) return;
            }
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                e.Handled = true;
            }
            else if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
                e.Handled = true;
            }
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T found) return found;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
        #endregion

        #region 导入
        private async void BtnImportCrx_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 CRX 扩展文件",
                Filter = "CRX 扩展 (*.crx)|*.crx|所有文件 (*.*)|*.*",
                Multiselect = false
            };
            if (ofd.ShowDialog() != true) return;
            await _manager.ImportCrxAsync(ofd.FileName);
            lbExtensions.Items.Refresh();
            // 异步刷新图标（容器已存在）
            await Dispatcher.InvokeAsync(LoadIconsForAll, System.Windows.Threading.DispatcherPriority.Background);
        }

        private async void BtnImportFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择已解压的扩展文件夹（含 manifest.json）"
            };
            if (dialog.ShowDialog() != true) return;
            await _manager.ImportFolderAsync(dialog.FolderName);
            lbExtensions.Items.Refresh();
            await Dispatcher.InvokeAsync(LoadIconsForAll, System.Windows.Threading.DispatcherPriority.Background);
        }
        #endregion

        #region 启用/禁用/删除
        private async void ChkEnabled_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is string id)
            {
                var ext = _manager.Extensions.FirstOrDefault(x => x.Id == id);
                if (ext != null) await _manager.SetEnabledAsync(ext, true);
            }
        }

        private async void ChkEnabled_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is string id)
            {
                var ext = _manager.Extensions.FirstOrDefault(x => x.Id == id);
                if (ext != null)
                {
                    await _manager.SetEnabledAsync(ext, false);
                    MessageBox.Show("禁用扩展将在重启浏览器后完全生效。", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var ext = _manager.Extensions.FirstOrDefault(x => x.Id == id);
                if (ext == null) return;
                if (MessageBox.Show($"确定删除扩展 \"{ext.Name}\"？\n将同时删除磁盘文件。",
                    "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _manager.Delete(ext);
                }
            }
        }
        #endregion
    }
}
