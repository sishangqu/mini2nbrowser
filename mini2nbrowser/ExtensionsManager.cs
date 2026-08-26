using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace mini2nbrowser
{
    /// <summary>
    /// 扩展管理器：负责扩展导入、加载、启用/禁用、删除、配置持久化。
    /// 所有扩展解压到程序目录下的 Extensions 子目录，配置保存到 extensions.json。
    /// </summary>
    public class ExtensionsManager
    {
        private readonly string _extensionsDir;
        private readonly string _configPath;
        private readonly ObservableCollection<ExtensionInfo> _extensions = new();
        private readonly HashSet<string> _loadedPaths = new(StringComparer.OrdinalIgnoreCase);
        private CoreWebView2Profile? _profile;

        /// <summary>WebView2 运行时是否支持扩展（≥1.0.2045）</summary>
        public static bool IsSupported { get; private set; }

        public ObservableCollection<ExtensionInfo> Extensions => _extensions;
        public string ExtensionsDir => _extensionsDir;

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        /// <summary>检测当前 WebView2 运行时是否支持扩展（≥1.0.2045）</summary>
        public static bool CheckSupport()
        {
            try
            {
                var v = CoreWebView2Environment.GetAvailableBrowserVersionString();
                if (string.IsNullOrEmpty(v)) { IsSupported = false; return false; }
                // 形如 "120.0.2210.91" 或 "1.0.2210.91"
                var parts = v.Split('.');
                if (parts.Length < 2) { IsSupported = false; return false; }
                // 主版本号大于 1 时（如 120.x），肯定支持；为 1 时检查次版本
                if (int.TryParse(parts[0], out var major))
                {
                    if (major > 1) { IsSupported = true; return true; }
                    if (major == 1 && parts.Length >= 3 && int.TryParse(parts[1], out var minor))
                    {
                        // 1.0.2045 起
                        IsSupported = minor >= 0 && int.TryParse(parts[2], out var build) && build >= 2045;
                        return IsSupported;
                    }
                }
                IsSupported = false;
                return false;
            }
            catch { IsSupported = false; return false; }
        }

        public ExtensionsManager(string baseDir, CoreWebView2Profile? profile = null)
        {
            _extensionsDir = Path.Combine(baseDir, "Extensions");
            _configPath = Path.Combine(baseDir, "extensions.json");
            _profile = profile;
            Directory.CreateDirectory(_extensionsDir);
        }

        /// <summary>绑定 WebView2 profile（首个标签页创建后调用）</summary>
        public void SetProfile(CoreWebView2Profile profile) => _profile = profile;

        #region 配置读写
        public void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var list = JsonSerializer.Deserialize<List<ExtensionInfo>>(File.ReadAllText(_configPath));
                    if (list != null)
                    {
                        _extensions.Clear();
                        foreach (var ext in list)
                        {
                            // 清理失效路径
                            if (Directory.Exists(ext.FolderPath))
                                _extensions.Add(ext);
                        }
                    }
                }
            }
            catch { }
        }

        public void SaveConfig()
        {
            try { File.WriteAllText(_configPath, JsonSerializer.Serialize(_extensions.ToList(), JsonOpts)); }
            catch { }
        }
        #endregion

        #region 导入
        /// <summary>从 CRX 文件导入扩展</summary>
        public async Task<bool> ImportCrxAsync(string crxPath)
        {
            try
            {
                // 用文件名（去扩展名）作为子目录名
                string baseName = Path.GetFileNameWithoutExtension(crxPath);
                string dest = Path.Combine(_extensionsDir, MakeSafeFolderName(baseName));
                // 同名目录加序号
                int n = 1;
                while (Directory.Exists(dest))
                {
                    dest = Path.Combine(_extensionsDir, $"{MakeSafeFolderName(baseName)}_{n++}");
                }

                CrxLoader.Extract(crxPath, dest);

                var info = BuildInfoFromFolder(dest, "crx");
                if (info == null)
                {
                    // manifest 缺失，清理后报错
                    try { Directory.Delete(dest, true); } catch { }
                    throw new InvalidDataException("解压后未找到有效的 manifest.json");
                }
                _extensions.Add(info);
                SaveConfig();

                // 立即加载到当前 profile
                await TryLoadIntoProfileAsync(info);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入 CRX 失败：\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>从已解压文件夹导入扩展（复制到 Extensions 目录）</summary>
        public async Task<bool> ImportFolderAsync(string sourceFolder)
        {
            try
            {
                if (!File.Exists(Path.Combine(sourceFolder, "manifest.json")))
                    throw new InvalidDataException("所选文件夹缺少 manifest.json，不是有效的扩展目录");

                string baseName = MakeSafeFolderName(new DirectoryInfo(sourceFolder).Name);
                string dest = Path.Combine(_extensionsDir, baseName);
                int n = 1;
                while (Directory.Exists(dest))
                {
                    dest = Path.Combine(_extensionsDir, $"{baseName}_{n++}");
                }
                CopyDirectory(sourceFolder, dest);

                // 同样处理 _metadata
                var metaDir = Path.Combine(dest, "_metadata");
                if (Directory.Exists(metaDir))
                {
                    var newMetaDir = Path.Combine(dest, "metadata");
                    if (Directory.Exists(newMetaDir)) try { Directory.Delete(newMetaDir, true); } catch { }
                    try { Directory.Move(metaDir, newMetaDir); } catch { }
                }

                var info = BuildInfoFromFolder(dest, "folder");
                if (info == null)
                {
                    try { Directory.Delete(dest, true); } catch { }
                    throw new InvalidDataException("manifest.json 解析失败");
                }
                _extensions.Add(info);
                SaveConfig();

                await TryLoadIntoProfileAsync(info);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入文件夹失败：\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>从扩展文件夹构建 ExtensionInfo（解析 manifest.json）</summary>
        private static ExtensionInfo? BuildInfoFromFolder(string folder, string source)
        {
            var manifest = ExtensionManifest.Parse(folder);
            if (manifest == null) return null;
            var info = new ExtensionInfo
            {
                Name = string.IsNullOrEmpty(manifest.Name) ? Path.GetFileName(folder) : manifest.Name,
                Version = manifest.Version ?? "",
                Description = manifest.Description ?? "",
                FolderPath = folder,
                IconRelPath = manifest.GetLargestIconPath() ?? "",
                Source = source,
                Enabled = true,
                ImportedAt = DateTime.Now
            };
            return info;
        }
        #endregion

        #region 启用/禁用/删除
        /// <summary>切换启用状态。禁用扩展需要重启 WebView2 实例才会真正生效（API 限制）</summary>
        public async Task SetEnabledAsync(ExtensionInfo ext, bool enabled)
        {
            ext.Enabled = enabled;
            SaveConfig();
            if (enabled)
            {
                await TryLoadIntoProfileAsync(ext);
            }
            else
            {
                // WebView2 当前 API 不支持运行时卸载，仅禁用记录；下次启动不会加载
                // 提示用户重启浏览器
            }
        }

        /// <summary>删除扩展：移除磁盘文件 + 配置（已加载到内存的扩展需重启浏览器才能彻底卸载）</summary>
        public void Delete(ExtensionInfo ext)
        {
            try
            {
                if (Directory.Exists(ext.FolderPath))
                    Directory.Delete(ext.FolderPath, true);
            }
            catch { }
            _loadedPaths.Remove(Path.GetFullPath(ext.FolderPath));
            _extensions.Remove(ext);
            SaveConfig();
        }

        /// <summary>启动时把所有已启用扩展加载到 profile</summary>
        public async Task LoadAllEnabledAsync()
        {
            if (_profile == null) return;
            foreach (var ext in _extensions.Where(e => e.Enabled).ToList())
            {
                await TryLoadIntoProfileAsync(ext);
            }
        }

        private async Task TryLoadIntoProfileAsync(ExtensionInfo ext)
        {
            if (_profile == null) return;
            if (!ext.Enabled) return;
            if (!Directory.Exists(ext.FolderPath)) return;
            // 防止重复加载同一扩展
            string key = Path.GetFullPath(ext.FolderPath);
            if (_loadedPaths.Contains(key)) return;
            try
            {
                var loaded = await _profile.AddBrowserExtensionAsync(ext.FolderPath);
                _loadedPaths.Add(key);
                // 用 WebView2 返回的真实名称回填
                if (!string.IsNullOrEmpty(loaded.Name) && loaded.Name != ext.Name)
                    ext.Name = loaded.Name;
            }
            catch
            {
                // 加载失败（manifest 非法等）不阻塞其他扩展
            }
        }
        #endregion

        #region 工具
        private static string MakeSafeFolderName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder();
            foreach (var c in name)
            {
                if (Array.IndexOf(invalid, c) >= 0) sb.Append('_');
                else sb.Append(c);
            }
            return string.IsNullOrEmpty(sb.ToString()) ? "extension" : sb.ToString();
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.TopDirectoryOnly))
            {
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.TopDirectoryOnly))
            {
                // 跳过 _metadata（已单独处理）
                CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
            }
        }
        #endregion
    }
}
