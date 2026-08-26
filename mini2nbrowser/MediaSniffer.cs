using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace mini2nbrowser;

// ===== 数据模型 =====
public class MediaItem : INotifyPropertyChanged
{
    public string Url { get; set; } = "";
    public string Kind { get; set; } = "";        // 视频 / 音频 / 流媒体
    public string Ext { get; set; } = "";          // mp4 m3u8 mp3 ...
    public string ContentType { get; set; } = "";
    public string PageTitle { get; set; } = "";
    public string PageUrl { get; set; } = "";
    public string Referrer { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;

    private long? _size;
    public long? Size
    {
        get => _size;
        set { if (_size != value) { _size = value; RaiseProp(); RaiseProp(nameof(SizeText)); } }
    }

    private long _downloaded;
    public long Downloaded
    {
        get => _downloaded;
        set { if (_downloaded != value) { _downloaded = value; RaiseProp(); RaiseProp(nameof(DownloadedText)); } }
    }

    private long _speed;
    public long Speed
    {
        get => _speed;
        set { if (_speed != value) { _speed = value; RaiseProp(); RaiseProp(nameof(SpeedText)); RaiseProp(nameof(EtaText)); } }
    }

    private long _avgSpeed;
    public long AvgSpeed
    {
        get => _avgSpeed;
        set { if (_avgSpeed != value) { _avgSpeed = value; RaiseProp(); RaiseProp(nameof(AvgSpeedText)); } }
    }

    private long _etaSeconds = -1;
    public long EtaSeconds
    {
        get => _etaSeconds;
        set { if (_etaSeconds != value) { _etaSeconds = value; RaiseProp(); RaiseProp(nameof(EtaText)); } }
    }

    private int _activeThreads;
    public int ActiveThreads
    {
        get => _activeThreads;
        set { if (_activeThreads != value) { _activeThreads = value; RaiseProp(); RaiseProp(nameof(ThreadsText)); } }
    }

    private int _targetThreads;
    public int TargetThreads
    {
        get => _targetThreads;
        set { if (_targetThreads != value) { _targetThreads = value; RaiseProp(); RaiseProp(nameof(ThreadsText)); } }
    }

    public string LimitType { get; set; } = "";

    private string _status = "已嗅探";
    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                RaiseProp();
                IsDownloading = value is "探测中" or "探测大小" or "探测并发" or "下载中" or "合并中...";
            }
        }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set { if (Math.Abs(_progress - value) > 0.01) { _progress = value; RaiseProp(); } }
    }

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        set { if (_isDownloading != value) { _isDownloading = value; RaiseProp(); } }
    }

    public string DisplayUrl => Url.Length > 70 ? Url[..67] + "..." : Url;
    public string DisplayTime => Time.ToString("HH:mm:ss");
    public string KindBadge => Kind switch
    {
        "视频" => "🎬",
        "音频" => "🎵",
        "流媒体" => "📺",
        _ => "📦"
    };
    public string SizeText => FormatBytes(Size ?? 0);
    public string DownloadedText => FormatBytes(Downloaded);
    public string SpeedText => Speed > 0 ? FormatBytes(Speed) + "/s" : "—";
    public string AvgSpeedText => AvgSpeed > 0 ? FormatBytes(AvgSpeed) + "/s" : "—";
    public string EtaText => EtaSeconds < 0 ? "—" : (EtaSeconds < 60 ? $"{EtaSeconds}s" : $"{EtaSeconds / 60:D2}:{EtaSeconds % 60:D2}");
    public string ThreadsText => TargetThreads > 0 ? $"{ActiveThreads}/{TargetThreads} 线程" : "—";

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void RaiseProp([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

// ===== 嗅探核心（逻辑同前，未改动）=====
public class MediaSniffer
{
    private readonly ObservableCollection<MediaItem> _items = new();
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private const int MaxItems = 500;

    private static readonly string[] VideoExts = { ".mp4", ".flv", ".webm", ".mov", ".avi", ".mkv", ".m4v", ".ts", ".wmv" };
    private static readonly string[] AudioExts = { ".mp3", ".m4a", ".ogg", ".wav", ".aac", ".flac", ".opus" };
    private static readonly string[] StreamExts = { ".m3u8", ".mpd" };

    private static readonly string[] VideoCt = { "video/", "application/octet-stream" };
    private static readonly string[] AudioCt = { "audio/", "application/ogg" };
    private static readonly string[] StreamCt = { "application/vnd.apple.mpegurl", "application/x-mpegurl", "application/x-mpegURL", "application/dash+xml" };

    public ObservableCollection<MediaItem> Items => _items;

    public void Attach(CoreWebView2 coreView, Func<(string title, string url)> pageCtxProvider)
    {
        coreView.WebResourceResponseReceived += (s, e) =>
        {
            try
            {
                var url = e.Request?.Uri ?? "";
                if (string.IsNullOrWhiteSpace(url)) return;
                if (url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;

                int sc = 0;
                try { sc = e.Response?.StatusCode ?? 0; } catch { }
                if (sc != 0 && (sc < 200 || sc >= 300)) return;

                string ct = "";
                try { ct = e.Response?.Headers?.GetHeader("Content-Type") ?? ""; } catch { }

                if (!MatchMedia(url, ct, out var kind, out var ext)) return;

                lock (_lock)
                {
                    if (_seen.Contains(url)) return;
                    _seen.Add(url);
                }

                long? size = null;
                try
                {
                    var cl = e.Response?.Headers?.GetHeader("Content-Length");
                    if (!string.IsNullOrWhiteSpace(cl) && long.TryParse(cl, out var sz)) size = sz;
                }
                catch { }

                var ctx = pageCtxProvider();
                var item = new MediaItem
                {
                    Url = url,
                    Kind = kind,
                    Ext = ext,
                    ContentType = ct,
                    PageTitle = ctx.title,
                    PageUrl = ctx.url,
                    Referrer = SafeGetHeader(e.Request?.Headers, "Referer"),
                    Size = size,
                    Time = DateTime.Now,
                    Status = "已嗅探"
                };

                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    _items.Insert(0, item);
                    while (_items.Count > MaxItems) _items.RemoveAt(_items.Count - 1);
                }));
            }
            catch { }
        };
    }

    private static bool MatchMedia(string url, string contentType, out string kind, out string ext)
    {
        kind = ""; ext = "";
        var lower = url.Contains('?') ? url.Substring(0, url.IndexOf('?')) : url;
        var ci = lower.LastIndexOf('.');
        var rawExt = ci >= 0 ? lower[ci..].ToLowerInvariant() : "";

        if (StreamExts.Contains(rawExt)) { kind = "流媒体"; ext = rawExt.TrimStart('.'); return true; }
        if (VideoExts.Contains(rawExt)) { kind = "视频"; ext = rawExt.TrimStart('.'); return true; }
        if (AudioExts.Contains(rawExt)) { kind = "音频"; ext = rawExt.TrimStart('.'); return true; }

        var ct = (contentType ?? "").ToLowerInvariant();
        if (string.IsNullOrEmpty(ct)) return false;
        foreach (var p in StreamCt) if (ct.Contains(p)) { kind = "流媒体"; ext = "m3u8"; return true; }
        foreach (var p in VideoCt) if (ct.StartsWith(p)) { kind = "视频"; ext = GuessExtFromCt(ct); return true; }
        foreach (var p in AudioCt) if (ct.StartsWith(p)) { kind = "音频"; ext = GuessExtFromCt(ct); return true; }

        return false;
    }

    private static string GuessExtFromCt(string ct)
    {
        if (ct.Contains("mp4")) return "mp4";
        if (ct.Contains("webm")) return "webm";
        if (ct.Contains("ogg")) return "ogg";
        if (ct.Contains("wav")) return "wav";
        if (ct.Contains("mpeg")) return "mp3";
        if (ct.Contains("aac")) return "aac";
        if (ct.Contains("flac")) return "flac";
        return "";
    }

    private static string SafeGetHeader(CoreWebView2HttpRequestHeaders? h, string key)
    {
        try { return h?.GetHeader(key) ?? ""; }
        catch { return ""; }
    }

    public void Clear()
    {
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            _items.Clear();
            lock (_lock) _seen.Clear();
        }));
    }

    public void Remove(MediaItem item)
    {
        lock (_lock) _seen.Remove(item.Url);
        // v1.9.1：ObservableCollection 只能在 UI 线程修改
        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    try { _items.Remove(item); } catch { }
                }));
            }
            else
            {
                _items.Remove(item);
            }
        }
        catch { }
    }
}

// ===== 块元数据 =====
internal sealed class Block
{
    public int Id;
    public string Url = "";
    public long Start, End;
    public long Downloaded;
    public bool Finished;
    public int HttpStatus;
    public string TempFile = "";
}

internal sealed record SiteCacheEntry(string Host, int Threads, DateTime Ts);

// ===== 增强型多线程下载器（移植自 ddm/main.cpp）=====
public class MediaDownloader
{
    // ===== 配置常量（与 ddm 一致）=====
    private const int MaxThreads = 32;
    private const int ProbeStep = 4;
    private const int ProbeMax = 32;
    private const double ProbeMinSuccess = 0.70;
    private const int MaxRetry = 8;
    private const long MinBlockKB = 256;
    private const int CacheValidDays = 7;
    private const int SpeedIntervalMs = 250;
    private const int SpeedWindow = 8;
    private const int MergeBufferKB = 4096;

    private readonly HttpClient _http;
    private readonly string _cacheFile;
    private readonly Dictionary<string, SiteCacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public MediaDownloader(string? cacheFile = null)
    {
        _http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        });
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _cacheFile = cacheFile ?? Path.Combine(AppContext.BaseDirectory, "media_site_cache.json");
        LoadCache();
    }

    public async Task DownloadAsync(MediaItem item, string savePath, IProgress<MediaDownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var isM3u8 = item.Ext.Equals("m3u8", StringComparison.OrdinalIgnoreCase) ||
                     item.Url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);
        var isMpd = item.Ext.Equals("mpd", StringComparison.OrdinalIgnoreCase) ||
                    item.Url.Contains(".mpd", StringComparison.OrdinalIgnoreCase);

        if (isMpd) throw new NotSupportedException("DASH/MPD 暂不支持自动下载，请使用专业工具。");

        if (isM3u8)
        {
            await DownloadM3u8Async(item, savePath, progress, ct);
            return;
        }

        await DownloadFileAsync(item, savePath, progress, ct);
    }

    // ===== 直接文件下载（多线程分块）=====
    private async Task DownloadFileAsync(MediaItem item, string savePath, IProgress<MediaDownloadProgress>? progress, CancellationToken ct)
    {
        item.Status = "探测大小";
        progress?.Report(new MediaDownloadProgress(0, 0, 0, "探测大小"));

        var (total, rangeOk) = await GetFileSizeAsync(item.Url, item, ct);

        // 不支持 Range 或拿不到大小 → 单线程降级
        if (total <= 0 || !rangeOk)
        {
            await DownloadDirectAsync(item, savePath, progress, ct);
            return;
        }

        item.Size = total;
        item.Downloaded = 0;

        // 探测服务器并发能力
        var host = ExtractHost(item.Url);
        int probeMax = LookupCache(host);
        if (probeMax <= 0)
        {
            item.Status = "探测并发";
            progress?.Report(new MediaDownloadProgress(0, total, 0, "探测服务器并发能力..."));
            probeMax = await DoProbeAsync(item.Url, item, ct);
            if (probeMax < ProbeMax)
            {
                UpdateCache(host, probeMax);
                item.LimitType = "固定";
            }
            else item.LimitType = "未知";
        }
        else item.LimitType = "缓存";

        // 计算线程数与块数
        int n = Math.Min(MaxThreads, Math.Max(1, probeMax));
        int maxBySize = (int)Math.Max(1, total / (MinBlockKB * 1024));
        n = Math.Min(n, maxBySize);
        int numBlocks = Math.Min(256, n * 4);
        if (total / ((long)numBlocks * 256) < 1) numBlocks = Math.Max(1, (int)(total / (256 * 1024)));
        numBlocks = Math.Clamp(numBlocks, 1, 1024);

        item.TargetThreads = n;

        var blocks = InitBlocks(item.Url, total, numBlocks);
        var tmpDir = Path.Combine(Path.GetTempPath(), "mini2n_dl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        foreach (var b in blocks) b.TempFile = Path.Combine(tmpDir, $"part_{b.Id:D6}.bin");

        var queue = new ConcurrentQueue<int>();
        foreach (var b in blocks) queue.Enqueue(b.Id);

        long downloaded = 0;
        long speedBytes = 0;
        long startBytes = 0;
        int activeThreads = 0;
        int statOk = 0, stat429 = 0, statOther = 0;
        var startTime = DateTime.Now;
        var lastTick = DateTime.Now;
        var speedWindow = new long[SpeedWindow];
        int wi = 0;

        using var speedTimer = new Timer(_ =>
        {
            var wb = Interlocked.Exchange(ref speedBytes, 0);
            var now = DateTime.Now;
            var ms = (now - lastTick).TotalMilliseconds;
            lastTick = now;
            var inst = ms > 0 ? wb * 1000.0 / ms : 0;
            speedWindow[wi % SpeedWindow] = (long)inst;
            wi++;
            long sum = 0;
            for (int i = 0; i < SpeedWindow; i++) sum += speedWindow[i];
            var sp = sum / Math.Min(wi, SpeedWindow);
            item.Speed = sp;
            var totalMs = (now - startTime).TotalMilliseconds;
            if (totalMs > 0) item.AvgSpeed = (long)((Interlocked.Read(ref downloaded) - startBytes) * 1000.0 / totalMs);
            if (sp > 0 && total > 0) item.EtaSeconds = (total - Interlocked.Read(ref downloaded)) / sp;
            else item.EtaSeconds = -1;
        }, null, SpeedIntervalMs, SpeedIntervalMs);

        item.Status = "下载中";

        using var sem = new SemaphoreSlim(n, n);
        var workers = new List<Task>();
        for (int i = 0; i < n; i++)
        {
            workers.Add(Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested && queue.TryDequeue(out var id))
                {
                    var b = blocks[id];
                    if (b.Finished) continue;
                    await sem.WaitAsync(ct);
                    Interlocked.Increment(ref activeThreads);
                    item.ActiveThreads = Interlocked.CompareExchange(ref activeThreads, 0, 0);
                    try
                    {
                        int retry = 0;
                        while (retry < MaxRetry && !ct.IsCancellationRequested)
                        {
                            try
                            {
                                bool ok = await DownloadBlockAsync(b, item, ct, bytes =>
                                {
                                    Interlocked.Add(ref downloaded, bytes);
                                    Interlocked.Add(ref speedBytes, bytes);
                                    b.Downloaded += bytes;
                                    var d = Interlocked.Read(ref downloaded);
                                    item.Downloaded = d;
                                    if (total > 0) item.Progress = d * 100d / total;
                                });
                                if (ok) { b.Finished = true; Interlocked.Increment(ref statOk); break; }
                                else
                                {
                                    if (b.HttpStatus == 429) Interlocked.Increment(ref stat429);
                                    else Interlocked.Increment(ref statOther);
                                    retry++;
                                    if (retry >= MaxRetry) break;
                                    int delaySec = Math.Min(30, 1 << Math.Min(retry, 4));
                                    await Task.Delay(delaySec * 1000, ct);
                                    // 重新入队
                                    queue.Enqueue(id);
                                    break;
                                }
                            }
                            catch (OperationCanceledException) { throw; }
                            catch
                            {
                                retry++;
                                if (retry >= MaxRetry) { Interlocked.Increment(ref statOther); break; }
                                int delaySec = Math.Min(30, 1 << Math.Min(retry, 4));
                                await Task.Delay(delaySec * 1000, ct);
                            }
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeThreads);
                        item.ActiveThreads = Interlocked.CompareExchange(ref activeThreads, 0, 0);
                        sem.Release();
                    }
                }
            }, ct));
        }

        await Task.WhenAll(workers);
        await speedTimer.DisposeAsync();

        if (ct.IsCancellationRequested)
        {
            item.Status = "已取消";
            try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); } catch { }
            return;
        }

        if (blocks.Any(x => !x.Finished))
        {
            item.Status = "失败";
            try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); } catch { }
            return;
        }

        // 合并
        item.Status = "合并中";
        item.ActiveThreads = 0;
        progress?.Report(new MediaDownloadProgress(total, total, 100, "合并中..."));
        await using var dst = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.Read, MergeBufferKB * 1024);
        var buf = new byte[MergeBufferKB * 1024];
        foreach (var b in blocks.OrderBy(x => x.Id))
        {
            using var src = new FileStream(b.TempFile, FileMode.Open, FileAccess.Read, FileShare.Read, MergeBufferKB * 1024);
            int rd;
            while ((rd = await src.ReadAsync(buf, ct)) > 0) await dst.WriteAsync(buf.AsMemory(0, rd), ct);
        }

        try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); } catch { }

        item.Status = "已完成";
        item.Progress = 100;
        item.Speed = 0;
        item.AvgSpeed = 0;
        item.EtaSeconds = 0;
    }

    // ===== 文件大小探测（HEAD → GET Range 0-0）=====
    private async Task<(long total, bool rangeOk)> GetFileSizeAsync(string url, MediaItem item, CancellationToken ct)
    {
        // HEAD 优先
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Head, url);
            AddReferrer(req, item);
            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                var total = resp.Content.Headers.ContentLength ?? 0;
                var rangeOk = resp.Headers.AcceptRanges.Contains("bytes");
                if (total > 0) return (total, rangeOk);
            }
        }
        catch { }

        // 降级：GET Range 0-0
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(0, 0);
            req.Headers.Add("Accept", "bytes");
            AddReferrer(req, item);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode == HttpStatusCode.PartialContent)
            {
                var cr = resp.Content.Headers.ContentRange;
                if (cr?.Length > 0) return (cr.Length.Value, true);
            }
            if (resp.IsSuccessStatusCode)
            {
                var total = resp.Content.Headers.ContentLength ?? 0;
                return (total, false);
            }
        }
        catch { }

        return (0, false);
    }

    // ===== 并发探测（4→32 递增，成功率 70% 阈值）=====
    private async Task<int> DoProbeAsync(string url, MediaItem item, CancellationToken ct)
    {
        int safe = 1;
        for (int t = ProbeStep; t <= ProbeMax; t += ProbeStep)
        {
            if (ct.IsCancellationRequested) break;
            int ok = 0, fail = 0;
            var tasks = new List<Task>();
            for (int i = 0; i < t; i++)
            {
                long s = i * 1024L, e = s + 1023;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var req = new HttpRequestMessage(HttpMethod.Head, url);
                        req.Headers.Range = new RangeHeaderValue(s, e);
                        AddReferrer(req, item);
                        using var resp = await _http.SendAsync(req, ct);
                        if (resp.StatusCode == HttpStatusCode.PartialContent) Interlocked.Increment(ref ok);
                        else Interlocked.Increment(ref fail);
                    }
                    catch { Interlocked.Increment(ref fail); }
                }));
            }
            await Task.WhenAll(tasks);
            int total = ok + fail;
            double sr = total > 0 ? (double)ok / total : 0;
            if (sr >= ProbeMinSuccess) safe = t;
            else break;
        }
        return safe;
    }

    private static List<Block> InitBlocks(string url, long total, int numBlocks)
    {
        var blocks = new List<Block>(numBlocks);
        long per = total / numBlocks;
        for (int i = 0; i < numBlocks; i++)
        {
            blocks.Add(new Block
            {
                Id = i,
                Url = url,
                Start = i * per,
                End = (i == numBlocks - 1) ? total - 1 : (i + 1) * per - 1
            });
        }
        return blocks;
    }

    private async Task<bool> DownloadBlockAsync(Block b, MediaItem item, CancellationToken ct, Action<long> onBytes)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, b.Url);
            req.Headers.Range = new RangeHeaderValue(b.Start + b.Downloaded, b.End);
            req.Headers.Add("Accept", "bytes");
            AddReferrer(req, item);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            b.HttpStatus = (int)resp.StatusCode;
            if (resp.StatusCode != HttpStatusCode.PartialContent && !resp.IsSuccessStatusCode)
                return false;
            using var fs = new FileStream(b.TempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var buf = new byte[256 * 1024];
            int n;
            while ((n = await stream.ReadAsync(buf, ct)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, n), ct);
                onBytes(n);
            }
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    // ===== 单线程降级下载 =====
    private async Task DownloadDirectAsync(MediaItem item, string savePath, IProgress<MediaDownloadProgress>? progress, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, item.Url);
        AddReferrer(req, item);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? item.Size ?? 0;
        if (total > 0) item.Size = total;
        item.Status = "下载中";
        await using var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920);
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var buf = new byte[81920];
        long recv = 0;
        int n;
        while ((n = await stream.ReadAsync(buf, ct)) > 0)
        {
            await fs.WriteAsync(buf.AsMemory(0, n), ct);
            recv += n;
            item.Downloaded = recv;
            if (total > 0) { item.Progress = recv * 100d / total; progress?.Report(new MediaDownloadProgress(recv, total, recv * 100d / total)); }
        }
        item.Status = "已完成";
        item.Progress = 100;
    }

    // ===== m3u8 下载：纯 C# 实现，分片下载 + TS 拼接 =====
    // 说明：m3u8 通常由 TS 分片组成，直接二进制拼接即可生成可播放的 MPEG-TS 文件。
    // 现代播放器（VLC、PotPlayer、mpv、MPC-HC）均能直接播放 .ts；保存为 .ts 输出最稳。
    private async Task DownloadM3u8Async(MediaItem item, string savePath, IProgress<MediaDownloadProgress>? progress, CancellationToken ct)
    {
        var baseUri = new Uri(item.Url);
        var m3u8Text = await GetTextAsync(item.Url, item, ct);
        m3u8Text = await ResolveMasterPlaylist(m3u8Text, baseUri, item, ct);

        var segments = ParseSegments(m3u8Text, baseUri);
        if (segments.Count == 0) throw new InvalidDataException("m3u8 未解析到分片（可能是加密流，暂不支持）。");

        item.LimitType = "流媒体";
        var tmpDir = Path.Combine(Path.GetTempPath(), "mini2n_media_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        long totalRecv = 0;
        long speedBytes = 0;
        var startTime = DateTime.Now;
        var lastTick = DateTime.Now;
        var speedWindow = new long[SpeedWindow];
        int wi = 0;

        // 速度采样定时器（与文件下载一致）
        using var speedTimer = new Timer(_ =>
        {
            var wb = Interlocked.Exchange(ref speedBytes, 0);
            var now = DateTime.Now;
            var ms = (now - lastTick).TotalMilliseconds;
            lastTick = now;
            var inst = ms > 0 ? wb * 1000.0 / ms : 0;
            speedWindow[wi % SpeedWindow] = (long)inst;
            wi++;
            long sum = 0;
            for (int i = 0; i < SpeedWindow; i++) sum += speedWindow[i];
            var sp = sum / Math.Min(wi, SpeedWindow);
            item.Speed = sp;
            var totalMs = (now - startTime).TotalMilliseconds;
            if (totalMs > 0) item.AvgSpeed = (long)(Interlocked.Read(ref totalRecv) * 1000.0 / totalMs);
        }, null, SpeedIntervalMs, SpeedIntervalMs);

        try
        {
            var files = new string[segments.Count];
            int done = 0;
            using var sem = new SemaphoreSlim(8);
            item.Status = "下载中";
            item.TargetThreads = 8;

            await Task.WhenAll(segments.Select(async (seg, idx) =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var segUrl = ResolveUri(baseUri, seg.Uri);
                    var segPath = Path.Combine(tmpDir, $"{idx:D6}.ts");
                    files[idx] = segPath;
                    int retry = 3;
                    while (retry-- > 0)
                    {
                        try
                        {
                            var len = await DownloadSegment(segUrl, segPath, item, ct);
                            Interlocked.Add(ref totalRecv, len);
                            Interlocked.Add(ref speedBytes, len);
                            break;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch when (retry >= 0) { await Task.Delay(500, ct); }
                    }
                }
                finally
                {
                    sem.Release();
                    var d = Interlocked.Increment(ref done);
                    item.ActiveThreads = Math.Min(8, segments.Count - d);
                    item.Downloaded = Interlocked.Read(ref totalRecv);
                    item.Progress = d * 100d / segments.Count;
                    progress?.Report(new MediaDownloadProgress(d, segments.Count, d * 100d / segments.Count));
                }
            }));

            item.ActiveThreads = 0;
            item.TargetThreads = 0;
            progress?.Report(new MediaDownloadProgress(segments.Count, segments.Count, 100, "合并中..."));
            item.Status = "合并中";

            // 纯 C# TS 拼接：按分片顺序二进制追加（MPEG-TS 流可直接拼接播放）
            var mergeBuf = new byte[MergeBufferKB * 1024];
            await using var outFs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.Read, MergeBufferKB * 1024);
            foreach (var f in files.Where(x => !string.IsNullOrEmpty(x) && File.Exists(x)).OrderBy(x => x))
            {
                using var segFs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read, MergeBufferKB * 1024);
                int rd;
                while ((rd = await segFs.ReadAsync(mergeBuf, ct)) > 0)
                    await outFs.WriteAsync(mergeBuf.AsMemory(0, rd), ct);
            }
        }
        finally
        {
            try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); } catch { }
        }

        item.Status = "已完成";
        item.Progress = 100;
        item.Speed = 0;
        item.AvgSpeed = 0;
        item.EtaSeconds = 0;
        if (File.Exists(savePath)) { item.Downloaded = new FileInfo(savePath).Length; item.Size = item.Downloaded; }
    }

    private async Task<string> GetTextAsync(string url, MediaItem item, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddReferrer(req, item);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> ResolveMasterPlaylist(string text, Uri baseUri, MediaItem item, CancellationToken ct)
    {
        if (!text.Contains("#EXT-X-STREAM-INF")) return text;
        int bestBw = -1; string bestUri = "";
        foreach (Match m in Regex.Matches(text, @"#EXT-X-STREAM-INF[^:]*:.*?(?:BANDWIDTH=(\d+))?.*?\s+(\S+)"))
        {
            var bw = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
            if (bw >= bestBw) { bestBw = bw; bestUri = m.Groups[2].Value.Trim(); }
        }
        if (string.IsNullOrEmpty(bestUri)) return text;
        var subUri = ResolveUri(baseUri, bestUri);
        return await GetTextAsync(subUri.ToString(), item, ct);
    }

    private record Segment(string Uri, string KeyUrl, string KeyIv);
    private static List<Segment> ParseSegments(string text, Uri baseUri)
    {
        var list = new List<Segment>();
        string curKeyUri = "", curKeyIv = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("#EXT-X-KEY"))
            {
                var method = Regex.Match(line, @"METHOD=([^,]+)").Groups[1].Value;
                if (!string.IsNullOrEmpty(method) && !method.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("m3u8 加密分片暂不支持自动合并，请使用专业工具。");
                var ku = Regex.Match(line, @"URI=""([^""]+)""").Groups[1].Value;
                if (!string.IsNullOrEmpty(ku)) curKeyUri = ResolveUri(baseUri, ku).ToString();
                var iv = Regex.Match(line, @"IV=([^,]+)").Groups[1].Value;
                curKeyIv = iv;
            }
            else if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;
            else list.Add(new Segment(line, curKeyUri, curKeyIv));
        }
        return list;
    }

    private static Uri ResolveUri(Uri baseUri, string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var abs)) return abs;
        return new Uri(baseUri, uri);
    }

    private async Task<long> DownloadSegment(Uri url, string segPath, MediaItem item, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddReferrer(req, item);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = 0;
        await using var fs = new FileStream(segPath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var buf = new byte[81920];
        int n;
        while ((n = await stream.ReadAsync(buf, ct)) > 0)
        {
            await fs.WriteAsync(buf.AsMemory(0, n), ct);
            total += n;
        }
        return total;
    }

    private static void AddReferrer(HttpRequestMessage req, MediaItem item)
    {
        try
        {
            if (!string.IsNullOrEmpty(item.Referrer)) req.Headers.Referrer = new Uri(item.Referrer);
            else if (!string.IsNullOrEmpty(item.PageUrl)) req.Headers.Referrer = new Uri(item.PageUrl);
        }
        catch { }
    }

    private static string ExtractHost(string url)
    {
        try { return new Uri(url).Host; } catch { return ""; }
    }

    // ===== 站点缓存（持久化 7 天）=====
    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_cacheFile)) return;
            var arr = JsonSerializer.Deserialize<List<SiteCacheEntry>>(File.ReadAllText(_cacheFile));
            if (arr == null) return;
            lock (_cacheLock)
            {
                foreach (var e in arr)
                    if ((DateTime.Now - e.Ts).TotalDays < CacheValidDays)
                        _cache[e.Host] = e;
            }
        }
        catch { }
    }

    private void SaveCache()
    {
        try
        {
            List<SiteCacheEntry> list;
            lock (_cacheLock) list = _cache.Values.ToList();
            File.WriteAllText(_cacheFile, JsonSerializer.Serialize(list, JsonOpts));
        }
        catch { }
    }

    private int LookupCache(string host)
    {
        if (string.IsNullOrEmpty(host)) return 0;
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(host, out var v) && (DateTime.Now - v.Ts).TotalDays < CacheValidDays)
                return v.Threads;
            return 0;
        }
    }

    private void UpdateCache(string host, int threads)
    {
        if (string.IsNullOrEmpty(host)) return;
        lock (_cacheLock) _cache[host] = new SiteCacheEntry(host, threads, DateTime.Now);
        SaveCache();
    }
}

public record MediaDownloadProgress(long Received, long Total, double Percent, string Status = "");
