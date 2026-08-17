using System;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace mini2nbrowser
{
    public partial class App : Application
    {
        private Mutex? _appMutex;
        public const string UniqueMutexName = "mini2nbrowser-Browser-v1.0.0-Unique";

        protected override void OnStartup(StartupEventArgs e)
        {
            // 单实例互斥检测：第二次双击 exe 不新建进程
            bool createdNew;
            _appMutex = new Mutex(true, UniqueMutexName, out createdNew);

            if (!createdNew)
            {
                // 已有实例在后台运行 → 唤醒已有窗口，毫秒级热启动
                NativeMethods.BringExistingInstanceToFront();
                Shutdown();
                return;
            }

            // 启动优化：ProfileOptimization 记录热点方法，下次启动并行编译
            try
            {
                ProfileOptimization.SetProfileRoot(AppContext.BaseDirectory);
                ProfileOptimization.StartProfile("mini2nbrowser.profile");
            }
            catch { }

            // 启动阶段低延迟 GC，减少 Full GC 阻塞 UI
            try { GCSettings.LatencyMode = GCLatencyMode.LowLatency; } catch { }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { GCSettings.LatencyMode = GCLatencyMode.Interactive; } catch { }
            _appMutex?.ReleaseMutex();
            _appMutex?.Dispose();
            base.OnExit(e);
        }
    }

    /// <summary>Win32 帮助类：唤醒已运行的主窗口</summary>
    internal static class NativeMethods
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        public static void BringExistingInstanceToFront()
        {
            EnumWindows((hwnd, _) =>
            {
                var source = HwndSource.FromHwnd(hwnd);
                if (source?.RootVisual is MainWindow mw)
                {
                    mw.RestoreFromTray();
                    if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
                    else ShowWindow(hwnd, SW_SHOW);
                    SetForegroundWindow(hwnd);
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }
    }
}
