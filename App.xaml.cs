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
        public const string UniqueMutexName = "mini2nbrowser-Browser-v1.0.0-Unique";
        public const string PipeName = "mini2nbrowser-restore-v1.0.0";

        protected override void OnStartup(StartupEventArgs e)
        {
            // 单实例互斥检测：第二次双击 exe 不新建进程
            bool createdNew;
            _appMutex = new Mutex(true, UniqueMutexName, out createdNew);

            if (!createdNew)
            {
                // 已有实例在后台运行 → 通过命名管道通知已有窗口热启动
                SignalRestore();
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

            // 启动命名管道服务器，监听后续实例的唤醒请求
            StartPipeServer();

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _pipeCts?.Cancel();
            try { GCSettings.LatencyMode = GCLatencyMode.Interactive; } catch { }
            _appMutex?.ReleaseMutex();
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
