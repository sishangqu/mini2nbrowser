using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace mini2nbrowser
{
    /// <summary>
    /// CRX 文件解析与解压。
    /// CRX 格式：
    ///   公共头："Cr24" (4字节) + version (uint32)
    ///   v2: pubKeyLen(uint32) + sigLen(uint32) + pubKey + sig + ZIP
    ///   v3: headerLen(uint32) + header(protobuf) + ZIP
    /// 解压后需将 _metadata 目录重命名为 metadata（WebView2 不识别 _metadata）
    /// </summary>
    public static class CrxLoader
    {
        private const string CRX_MAGIC = "Cr24";

        /// <summary>校验并解压 CRX 到目标目录（目录不存在会创建，已存在会被清空）</summary>
        /// <exception cref="InvalidDataException">CRX 格式非法时抛出</exception>
        public static void Extract(string crxPath, string destFolder)
        {
            if (!File.Exists(crxPath))
                throw new FileNotFoundException("CRX 文件不存在", crxPath);

            using var fs = File.OpenRead(crxPath);
            using var br = new BinaryReader(fs);

            // 1. 校验魔数 "Cr24"
            var magicBytes = br.ReadBytes(4);
            if (magicBytes.Length < 4)
                throw new InvalidDataException("文件过小，不是有效的 CRX 文件");
            string magic = Encoding.ASCII.GetString(magicBytes);
            if (magic != CRX_MAGIC)
                throw new InvalidDataException($"CRX 魔数不匹配（期望 {CRX_MAGIC}，实际 {magic}），可能不是有效的扩展文件");

            // 2. 读取版本号
            uint version = br.ReadUInt32();
            long zipStart;
            if (version == 2)
            {
                int pubKeyLen = br.ReadInt32();
                int sigLen = br.ReadInt32();
                zipStart = 16 + pubKeyLen + sigLen;
            }
            else if (version == 3)
            {
                int headerLen = br.ReadInt32();
                zipStart = 12 + headerLen;
            }
            else
            {
                throw new InvalidDataException($"不支持的 CRX 版本: {version}（仅支持 v2/v3）");
            }

            if (zipStart >= fs.Length)
                throw new InvalidDataException("CRX 头部声明超过文件大小，文件可能已损坏");

            // 3. 定位到 ZIP 起点
            fs.Position = zipStart;

            // 4. 准备目标目录（清空）
            if (Directory.Exists(destFolder))
            {
                try { Directory.Delete(destFolder, true); } catch { }
                // 二次保险：若删不干净，等待并重试一次
                System.Threading.Thread.Sleep(50);
                if (Directory.Exists(destFolder))
                    Directory.Delete(destFolder, true);
            }
            Directory.CreateDirectory(destFolder);

            // 5. 解压 ZIP
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in zip.Entries)
            {
                // 安全：防止路径穿越（如 ../foo）
                string relative = entry.FullName.Replace('\\', '/');
                if (relative.Contains(".."))
                    continue;

                string fullPath = Path.Combine(destFolder, relative);
                // 规范化后再校验是否在 destFolder 之内
                string normalizedDest = Path.GetFullPath(destFolder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                string normalizedFull = Path.GetFullPath(fullPath);
                if (!normalizedFull.StartsWith(normalizedDest, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (relative.EndsWith("/") || string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(fullPath);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                entry.ExtractToFile(fullPath, overwrite: true);
            }

            // 6. WebView2 关键坑：_metadata 必须重命名为 metadata
            var metaDir = Path.Combine(destFolder, "_metadata");
            if (Directory.Exists(metaDir))
            {
                var newMetaDir = Path.Combine(destFolder, "metadata");
                if (Directory.Exists(newMetaDir))
                {
                    try { Directory.Delete(newMetaDir, true); } catch { }
                }
                try { Directory.Move(metaDir, newMetaDir); } catch { }
            }
        }
    }
}
