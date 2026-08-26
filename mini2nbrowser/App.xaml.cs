using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace mini2nbrowser
{
    public partial class App : Application
    {
        private Mutex? _appMutex;
        private CancellationTokenSource? _pipeCts;
        private bool _ownsMutex;

        private static string CrashLogPath =>
            Path.Combine(DataDir, "crash.log");

        public static string ProfileName { get; private set; } = "";

        public static string DataDir { get; private set; } = GetExeDirectory();

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

        private static string MutexName =>
            string.IsNullOrEmpty(ProfileName)
                ? "mini2nbrowser-Browser-v1.3.0-Unique"
                : $"mini2nbrowser-Browser-v1.3.0-Profile-{ProfileName}";

        private static string PipeName =>
            string.IsNullOrEmpty(ProfileName)
                ? "mini2nbrowser-restore-v1.3.0"
                : $"mini2nbrowser-restore-v1.3.0-{ProfileName}";

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            try
            {
                StartupCore(e);
            }
            catch (Exception ex)
            {
                LogCrash("OnStartup", ex);
                MessageBox.Show(
                    $"mini2n Browser 启动失败：\n{ex.Message}\n\n详情已写入：{CrashLogPath}",
                    "启动错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(-1);
            }
        }

        private void StartupCore(StartupEventArgs e)
        {
            ParseArgs(e.Args);

            try { Directory.CreateDirectory(DataDir); } catch { }

            _appMutex = new Mutex(true, MutexName, out bool createdNew);
            _ownsMutex = createdNew;

            if (!createdNew)
            {
                SignalRestore();
                Environment.Exit(0);
                return;
            }

            try
            {
                ProfileOptimization.SetProfileRoot(DataDir);
                ProfileOptimization.StartProfile("mini2nbrowser.profile");
            }
            catch { }

            try { GCSettings.LatencyMode = GCLatencyMode.LowLatency; } catch { }

            StartPipeServer();

            base.OnStartup(e);

            var mw = new MainWindow(DataDir, ProfileName);
            MainWindow = mw;
            mw.Show();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogCrash("UI线程", e.Exception);
            if (IsRecoverable(e.Exception))
            {
                e.Handled = true;
            }
        }

        private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                LogCrash("非UI线程(致命)", ex);
            else
                LogCrash("非UI线程(致命)", new Exception(e.ExceptionObject?.ToString() ?? "unknown"));
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogCrash("Task未观察", e.Exception);
            e.SetObserved();
        }

        private static bool IsRecoverable(Exception ex)
        {
            if (ex is System.Runtime.InteropServices.COMException) return true;
            if (ex is System.Runtime.InteropServices.InvalidComObjectException) return true;
            if (ex is ObjectDisposedException) return true;
            if (ex is IOException) return true;
            if (ex is InvalidOperationException ioex && ioex.Message.Contains("modified", StringComparison.OrdinalIgnoreCase)) return true;
            if (ex is NullReferenceException) return true;
            if (ex.InnerException != null) return IsRecoverable(ex.InnerException);
            return false;
        }

        private static void LogCrash(string context, Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"=== [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {context} ===");
                sb.AppendLine($"Type: {ex.GetType().FullName}");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine($"StackTrace:\n{ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    sb.AppendLine("--- InnerException ---");
                    sb.AppendLine($"Type: {ex.InnerException.GetType().FullName}");
                    sb.AppendLine($"Message: {ex.InnerException.Message}");
                    sb.AppendLine($"StackTrace:\n{ex.InnerException.StackTrace}");
                }
                sb.AppendLine();

                var dir = Path.GetDirectoryName(CrashLogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var logPath = CrashLogPath;
                try
                {
                    if (File.Exists(logPath) && new FileInfo(logPath).Length > 2 * 1024 * 1024)
                    {
                        var oldPath = logPath + ".old";
                        try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { }
                        try { File.Move(logPath, oldPath); } catch { }
                    }
                }
                catch { }

                File.AppendAllText(logPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

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

        public static string SanitizeProfileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var c in name.Trim())
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            var result = sb.ToString();
            if (result == "." || result == "..") return "";
            return result;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _pipeCts?.Cancel();
            try { GCSettings.LatencyMode = GCLatencyMode.Interactive; } catch { }
            if (_ownsMutex)
            {
                try { _appMutex?.ReleaseMutex(); } catch { }
            }
            _appMutex?.Dispose();
            base.OnExit(e);
        }

        private void StartPipeServer()
        {
            _pipeCts = new CancellationTokenSource();
            var token = _pipeCts.Token;
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    NamedPipeServerStream? server = null;
                    try
                    {
                        server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        await server.WaitForConnectionAsync(token);
                        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                        var msg = await reader.ReadLineAsync(token);
                        if (msg == "restore")
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                try
                                {
                                    if (MainWindow is MainWindow mw && mw != null)
                                        mw.RestoreFromTray();
                                }
                                catch { }
                            });
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (IOException) { await Task.Delay(200, token); }
                    catch { await Task.Delay(500, token); }
                    finally
                    {
                        try { server?.Dispose(); } catch { }
                    }
                }
            }, token);
        }

        private static void SignalRestore()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
                client.Connect(2000);
                using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true);
                writer.WriteLine("restore");
                writer.Flush();
            }
            catch { }
        }
    }
}
