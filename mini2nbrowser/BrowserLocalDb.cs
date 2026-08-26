using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace mini2nbrowser
{
    /// <summary>下拉候选项来源：本地历史 / 本地书签 / 云端搜索联想</summary>
    public enum SuggestSource
    {
        LocalHistory,
        LocalBookmark,
        CloudSearch
    }

    /// <summary>地址栏下拉候选项（绑定到 ListBox ItemSource）</summary>
    public class AddressSuggestItem
    {
        public string Text { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public SuggestSource Source { get; set; }

        /// <summary>用于UI显示：来源标签 + 颜色</summary>
        public string SourceLabel => Source switch
        {
            SuggestSource.LocalHistory => "历史",
            SuggestSource.LocalBookmark => "书签",
            SuggestSource.CloudSearch => "搜索",
            _ => ""
        };

        public string SourceColor => Source switch
        {
            SuggestSource.LocalHistory => "#0078D4",   // 蓝
            SuggestSource.LocalBookmark => "#107C10",   // 绿
            SuggestSource.CloudSearch => "#5C2D91",     // 紫
            _ => "#888888"
        };
    }

    /// <summary>
    /// SQLite 本地数据库：浏览历史 + 书签
    /// WebView2 不自动维护历史，必须在 NavigationCompleted 手动入库。
    /// 所有公开方法都是线程安全（打开即用）；建议历史量大时把查询放到 Task.Run 执行。
    /// </summary>
    public class BrowserLocalDb : IDisposable
    {
        private readonly string _dbPath;
        private readonly object _lock = new();

        public BrowserLocalDb(string dbPath)
        {
            _dbPath = dbPath;
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS History(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Url TEXT NOT NULL,
    Title TEXT,
    Timestamp INTEGER
);
CREATE TABLE IF NOT EXISTS Bookmark(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Url TEXT NOT NULL UNIQUE,
    Title TEXT
);
CREATE INDEX IF NOT EXISTS idx_history_url ON History(Url);
CREATE INDEX IF NOT EXISTS idx_history_title ON History(Title);
CREATE INDEX IF NOT EXISTS idx_history_ts ON History(Timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_bookmark_url ON Bookmark(Url);
CREATE INDEX IF NOT EXISTS idx_bookmark_title ON Bookmark(Title);
";
            cmd.ExecuteNonQuery();
        }

        private SqliteConnection Open()
        {
            // SQLitePCLRaw 由 Microsoft.Data.Sqlite 8.0 自动加载，无需手动 provider
            var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            // 性能调优：WAL 模式适合多并发读 + 偶尔写（浏览器场景最合适）
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-4096;";
            pragma.ExecuteNonQuery();
            return conn;
        }

        // ============= 历史 =============

        /// <summary>在 WebView2 NavigationCompleted 时调用；无痕标签不要调用。</summary>
        public void AddHistory(string url, string title)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
            if (url.Contains("HomePage.html", StringComparison.OrdinalIgnoreCase)) return;
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var cleanTitle = string.IsNullOrWhiteSpace(title) ? url : title;
            lock (_lock)
            {
                using var conn = Open();
                using var tx = conn.BeginTransaction();
                // 先去重：相同 URL 删掉最旧一条，再插入新的（相当于 bump 到最前）
                using (var del = conn.CreateCommand())
                {
                    del.CommandText = "DELETE FROM History WHERE Url=@u";
                    del.Parameters.AddWithValue("@u", url);
                    del.Transaction = tx;
                    del.ExecuteNonQuery();
                }
                using (var ins = conn.CreateCommand())
                {
                    ins.CommandText = "INSERT INTO History(Url,Title,Timestamp) VALUES(@u,@t,@ts)";
                    ins.Parameters.AddWithValue("@u", url);
                    ins.Parameters.AddWithValue("@t", cleanTitle);
                    ins.Parameters.AddWithValue("@ts", ts);
                    ins.Transaction = tx;
                    ins.ExecuteNonQuery();
                }
                // 保留最近 2000 条（v1.5 扩容：从 500 提到 2000，便于联想）
                using (var trim = conn.CreateCommand())
                {
                    trim.CommandText = @"
DELETE FROM History WHERE Id IN (
    SELECT Id FROM History ORDER BY Timestamp DESC LIMIT -1 OFFSET 2000
)";
                    trim.Transaction = tx;
                    trim.ExecuteNonQuery();
                }
                tx.Commit();
            }
        }

        public void ClearHistory()
        {
            lock (_lock)
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM History;";
                cmd.ExecuteNonQuery();
            }
        }

        public int HistoryCount
        {
            get
            {
                lock (_lock)
                {
                    using var conn = Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM History;";
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        // ============= 书签 =============

        public void AddBookmark(string url, string title)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var cleanTitle = string.IsNullOrWhiteSpace(title) ? url : title;
            lock (_lock)
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                // INSERT OR REPLACE：Url UNIQUE，存在则覆盖标题
                cmd.CommandText =
                    "INSERT OR REPLACE INTO Bookmark(Url,Title) VALUES(@u,@t);";
                cmd.Parameters.AddWithValue("@u", url);
                cmd.Parameters.AddWithValue("@t", cleanTitle);
                cmd.ExecuteNonQuery();
            }
        }

        public void RemoveBookmark(string url)
        {
            lock (_lock)
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Bookmark WHERE Url=@u;";
                cmd.Parameters.AddWithValue("@u", url);
                cmd.ExecuteNonQuery();
            }
        }

        public bool HasBookmark(string url)
        {
            lock (_lock)
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM Bookmark WHERE Url=@u LIMIT 1;";
                cmd.Parameters.AddWithValue("@u", url);
                using var r = cmd.ExecuteReader();
                return r.Read();
            }
        }

        public List<(string Url, string Title)> GetAllBookmarks()
        {
            var list = new List<(string, string)>();
            lock (_lock)
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Url, Title FROM Bookmark ORDER BY Id DESC;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add((r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1)));
            }
            return list;
        }

        public List<(string Url, string Title, DateTime VisitedAt)> GetAllHistory(int limit = 2000)
        {
            var list = new List<(string, string, DateTime)>();
            lock (_lock)
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Url, Title, Timestamp FROM History ORDER BY Timestamp DESC LIMIT @l;";
                cmd.Parameters.AddWithValue("@l", limit);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var ts = r.IsDBNull(2) ? 0 : r.GetInt64(2);
                    var dt = ts == 0 ? DateTime.Now :
                        DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime;
                    list.Add((r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1), dt));
                }
            }
            return list;
        }

        // ============= 联想查询 =============

        /// <summary>
        /// 同步查询本地历史+书签联想。
        /// 优先级：书签(匹配到排在前) > 历史（按时间倒序）。
        /// 如果数据量 > 2000，建议调用 QueryLocalSuggestAsync 避免 UI 卡顿。
        /// </summary>
        public List<AddressSuggestItem> QueryLocalSuggest(string keyword, int takePerSource = 6)
        {
            var list = new List<AddressSuggestItem>();
            if (string.IsNullOrWhiteSpace(keyword)) return list;
            var kw = $"%{keyword.Trim()}%";
            lock (_lock)
            {
                using var conn = Open();
                // 书签：优先级最高
                using (var c1 = conn.CreateCommand())
                {
                    c1.CommandText = @"
SELECT Url, Title FROM Bookmark
WHERE Title LIKE @kw OR Url LIKE @kw
ORDER BY Id DESC LIMIT @take";
                    c1.Parameters.AddWithValue("@kw", kw);
                    c1.Parameters.AddWithValue("@take", takePerSource);
                    using var r = c1.ExecuteReader();
                    while (r.Read())
                    {
                        var u = r.GetString(0);
                        var t = r.IsDBNull(1) ? u : r.GetString(1);
                        list.Add(new AddressSuggestItem
                        {
                            Url = u,
                            Text = string.IsNullOrWhiteSpace(t) ? u : t,
                            Source = SuggestSource.LocalBookmark
                        });
                    }
                }
                // 历史：按时间倒序
                using (var c2 = conn.CreateCommand())
                {
                    c2.CommandText = @"
SELECT Url, Title FROM History
WHERE Title LIKE @kw OR Url LIKE @kw
ORDER BY Timestamp DESC LIMIT @take";
                    c2.Parameters.AddWithValue("@kw", kw);
                    c2.Parameters.AddWithValue("@take", takePerSource);
                    using var r = c2.ExecuteReader();
                    while (r.Read())
                    {
                        var u = r.GetString(0);
                        var t = r.IsDBNull(1) ? u : r.GetString(1);
                        list.Add(new AddressSuggestItem
                        {
                            Url = u,
                            Text = string.IsNullOrWhiteSpace(t) ? u : t,
                            Source = SuggestSource.LocalHistory
                        });
                    }
                }
            }
            return list;
        }

        // ============= 一次性迁移：JSON 历史/书签 -> SQLite（v1.4 -> v1.5）=============

        /// <summary>
        /// 从旧版 JSON 文件导入到 SQLite。
        /// 如果已经导入过（数据库内已有书签或标记）则跳过。
        /// </summary>
        public void ImportFromJsonIfNeeded(string historyJsonPath, string bookmarksJsonPath)
        {
            bool hasBookmarkInDb;
            bool hasHistoryInDb;
            lock (_lock)
            {
                using var c = Open();
                using var check1 = c.CreateCommand();
                check1.CommandText = "SELECT COUNT(*) FROM Bookmark;";
                hasBookmarkInDb = Convert.ToInt32(check1.ExecuteScalar()) > 0;
                using var check2 = c.CreateCommand();
                check2.CommandText = "SELECT COUNT(*) FROM History;";
                hasHistoryInDb = Convert.ToInt32(check2.ExecuteScalar()) > 0;
            }

            if (!hasBookmarkInDb && File.Exists(bookmarksJsonPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(bookmarksJsonPath));
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in doc.RootElement.EnumerateArray())
                        {
                            var url = e.GetPropertyOrDefault("Url")?.GetString();
                            var title = e.GetPropertyOrDefault("Title")?.GetString();
                            if (!string.IsNullOrEmpty(url)) AddBookmark(url, title ?? "");
                        }
                    }
                }
                catch { /* 迁移失败不影响启动 */ }
            }

            if (!hasHistoryInDb && File.Exists(historyJsonPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(historyJsonPath));
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        // 老版最多 500 条，直接反向遍历插入（使时间顺序正确）
                        var items = new List<(string u, string t, DateTime dt)>();
                        foreach (var e in doc.RootElement.EnumerateArray())
                        {
                            var u = e.GetPropertyOrDefault("Url")?.GetString();
                            var t = e.GetPropertyOrDefault("Title")?.GetString();
                            if (string.IsNullOrEmpty(u)) continue;
                            DateTime dt = DateTime.Now;
                            var va = e.GetPropertyOrDefault("VisitedAt");
                            if (va.HasValue && va.Value.ValueKind == JsonValueKind.String)
                            {
                                if (DateTime.TryParse(va.Value.GetString(), out var parsed))
                                    dt = parsed;
                            }
                            items.Add((u, t ?? "", dt));
                        }
                        // VisitedAt 越新越靠后 -> 正向 AddHistory（内部 bump 到最前），顺序对
                        foreach (var (u, t, _) in items)
                            AddHistory(u, t);
                    }
                }
                catch { /* 迁移失败不影响启动 */ }
            }
        }

        public void Dispose()
        {
            // SQLite 连接使用 using 模式；此 Dispose 为未来热插拔预留
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>System.Text.Json 小工具：避免找不到属性抛异常（用于迁移读取老 JSON 结构）</summary>
    internal static class JsonElementExtensions
    {
        public static JsonElement? GetPropertyOrDefault(this JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object) return null;
            if (e.TryGetProperty(name, out var v)) return v;
            // 兼容大小写
            foreach (var p in e.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    return p.Value;
            }
            return null;
        }
    }
}
