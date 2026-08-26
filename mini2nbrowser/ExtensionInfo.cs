using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace mini2nbrowser
{
    /// <summary>扩展信息（持久化到 extensions.json）</summary>
    public class ExtensionInfo : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>扩展名（manifest 中的 name 字段，或从 CRX 提取）</summary>
        public string Name { get; set; } = "";

        /// <summary>扩展版本号（manifest 中的 version 字段）</summary>
        public string Version { get; set; } = "";

        /// <summary>扩展描述</summary>
        public string Description { get; set; } = "";

        /// <summary>解压后的扩展文件夹绝对路径</summary>
        public string FolderPath { get; set; } = "";

        /// <summary>图标相对路径（相对扩展文件夹），用于 UI 显示</summary>
        public string IconRelPath { get; set; } = "";

        /// <summary>导入时间戳</summary>
        public DateTime ImportedAt { get; set; } = DateTime.Now;

        private bool _enabled = true;
        /// <summary>是否启用</summary>
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        /// <summary>导入来源：crx / folder</summary>
        public string Source { get; set; } = "crx";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>manifest.json 轻量解析模型</summary>
    public class ExtensionManifest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("icons")]
        public Dictionary<string, string>? Icons { get; set; }

        /// <summary>解析扩展文件夹下的 manifest.json</summary>
        public static ExtensionManifest? Parse(string folderPath)
        {
            try
            {
                var manifestFile = System.IO.Path.Combine(folderPath, "manifest.json");
                if (!System.IO.File.Exists(manifestFile)) return null;
                var json = System.IO.File.ReadAllText(manifestFile);
                return JsonSerializer.Deserialize<ExtensionManifest>(json);
            }
            catch { return null; }
        }

        /// <summary>从 manifest 的 icons 字段中选最大尺寸的图标路径</summary>
        public string? GetLargestIconPath()
        {
            if (Icons == null || Icons.Count == 0) return null;
            int bestSize = -1;
            string? bestPath = null;
            foreach (var kv in Icons)
            {
                if (int.TryParse(kv.Key, out var size) && size > bestSize)
                {
                    bestSize = size;
                    bestPath = kv.Value;
                }
            }
            // 若 key 不是数字，取第一个
            if (bestPath == null)
            {
                foreach (var kv in Icons) { bestPath = kv.Value; break; }
            }
            return bestPath;
        }
    }
}
