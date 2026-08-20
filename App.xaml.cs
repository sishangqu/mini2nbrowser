using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace mini2nbrowser
{
    public partial class App : Application
    {
        private Mutex? _appMutex;
        private CancellationTokenSource? _pipeCts;
        private bool _ownsMutex;

        /// <summary>当前实例的 profile 名（空表示默认实例）</summary>
        public static string ProfileName { get; private set; } = "";

        /// <summary>当前实例的数据根目录（默认=exe 实际所在目录，多 profile 时=exe 目录下 Profiles\&lt;name&gt;）</summary>
        public static string DataDir { get; private set; } = GetExeDirectory();

        /// <summary>获取 exe 实际所在目录（单文件发布时 AppContext.BaseDirectory 是临时解压目录，必须用 ProcessPath）</summary>
        private static string GetExeDirectory()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                    return Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
            }
            catch { }
            return AppContext.BaseDirectory;
        }

        /// <summary>当前实例的 Mutex 名（含 profile 后缀，多实例互不冲突）</summary>
        private static string MutexName =>
            string.IsNullOrEmpty(ProfileName)
                ? "mini2nbrowser-Browser-v1.1.0-Unique"
                : $"mini2nbrowser-Browser-v1.1.0-Profile-{ProfileName}";

        /// <summary>当前实例的管道名（含 profile 后缀）</summary>
        private static string PipeName =>
            string.IsNullOrEmpty(ProfileName)
                ? "mini2nbrowser-restore-v1.1.0"
                : $"mini2nbrowser-restore-v1.1.0-{ProfileName}";

        protected override void OnStartup(StartupEventArgs e)
        {
            // 解析命令行参数：--profile <name> 启动独立实例（独立数据目录、独立托盘）
            ParseArgs(e.Args);

            // 单实例互斥检测（每个 profile 独立互斥，不同 profile 可并存）
            _appMutex = new Mutex(true, MutexName, out bool createdNew);
            _ownsMutex = createdNew;

            if (!createdNew)
            {
                // 已有同 profile 实例在运行 → 通过命名管道通知已有窗口热启动
                SignalRestore();
                // 关键修复：用 Environment.Exit 立即终止进程，避免 WPF 继续 base.OnStartup
                // 通过 StartupUri 创建第二个主窗口和托盘图标
                Environment.Exit(0);
                return;
            }

            // 启动优化：ProfileOptimization 记录热点方法，下次启动并行编译
            try
            {
                ProfileOptimization.SetProfileRoot(DataDir);
                ProfileOptimization.StartProfile("mini2nbrowser.profile");
            }
            catch { }

            // 启动阶段低延迟 GC，减少 Full GC 阻塞 UI
            try { GCSettings.LatencyMode = GCLatencyMode.LowLatency; } catch { }

            // 启动命名管道服务器，监听后续实例的唤醒请求
            StartPipeServer();

            base.OnStartup(e);

            // 显式创建主窗口（移除了 StartupUri，避免新实例也走窗口创建路径）
            var mw = new MainWindow(DataDir, ProfileName);
            MainWindow = mw;
            mw.Show();
        }

        /// <summary>解析命令行参数，确定 profile 名和数据目录</summary>
        private static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--profile" || args[i] == "-p") && i + 1 < args.Length)
                {
                    ProfileName = SanitizeProfileName(args[++i]);
                    break;
                }
            }
            if (!string.IsNullOrEmpty(ProfileName))
            {
                DataDir = Path.Combine(GetExeDirectory(), "Profiles", ProfileName);
                Directory.CreateDirectory(DataDir);
            }
        }

        /// <summary>清理 profile 名中的非法字符，防止路径穿越</summary>
        private static string SanitizeProfileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder();
            foreach (var c in name.Trim())
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            var result = sb.ToString();
            // 防止穿越：禁止 . 和 ..
            if (result == "." || result == "..") return "";
            return result;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _pipeCts?.Cancel();
            try { GCSettings.LatencyMode = GCLatencyMode.Interactive; } catch { }
            // 仅在真正拥有 Mutex 所有权时才释放（否则 ReleaseMutex 会抛 ApplicationException）
            if (_ownsMutex)
            {
                try { _appMutex?.ReleaseMutex(); } catch { }
            }
            _appMutex?.Dispose();
            base.OnExit(e);
        }

        /// <summary>命名管道服务器：监听新实例发来的唤醒请求</summary>
        private void StartPipeServer()
        {
            _pipeCts = new CancellationTokenSource();
            var token = _pipeCts.Token;
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        await server.WaitForConnectionAsync(token);
                        using var reader = new StreamReader(server, leaveOpen: true);
                        var msg = await reader.ReadLineAsync(token);
                        if (msg == "restore")
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (MainWindow is MainWindow mw)
                                    mw.RestoreFromTray();
                            });
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch { await Task.Delay(500); }
                }
            }, token);
        }

        /// <summary>新实例向已有实例发送唤醒信号</summary>
        private static void SignalRestore()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
                client.Connect(2000);
                using var writer = new StreamWriter(client, leaveOpen: true);
                writer.WriteLine("restore");
                writer.Flush();
            }
            catch { }
        }
    }
}
