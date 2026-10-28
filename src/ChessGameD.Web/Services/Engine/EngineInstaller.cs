using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ChessGameD.Web.Data;
using Microsoft.Extensions.Options;

namespace ChessGameD.Web.Services.Engine;

/// <summary>
/// 应用内一键安装 Stockfish:
/// 查最新 Release → 按当前运行平台挑资产 → 下载 → 校验 SHA-256 → 解压 → 定位可执行文件 → 注册路径。
///
/// Windows 官方包是 <c>.zip</c>,Linux / macOS 官方包是 <c>.tar.gz</c>,两种都要支持。
/// </summary>
public sealed class EngineInstaller
{
    private const string ReleasesApi = "https://api.github.com/repos/official-stockfish/Stockfish/releases/latest";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SettingsStore _settings;
    private readonly EngineManager _engineManager;
    private readonly EngineDownloadOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<EngineInstaller> _logger;
    private readonly EnginePlatform _platform;

    public EngineInstaller(
        IHttpClientFactory httpClientFactory,
        SettingsStore settings,
        EngineManager engineManager,
        IOptions<ChessOptions> options,
        IWebHostEnvironment environment,
        ILogger<EngineInstaller> logger,
        EnginePlatform platform)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _engineManager = engineManager;
        _options = options.Value.Engine;
        _environment = environment;
        _logger = logger;
        _platform = platform;
    }

    /// <summary>当前运行平台(决定下载哪个发行包)。</summary>
    public EnginePlatform Platform => _platform;

    public string EnginesRoot => Path.Combine(_environment.ContentRootPath, "App_Data", "engines");

    private readonly ConcurrentDictionary<string, InstallJob> _jobs = new();
    private string? _activeJobId;

    /// <summary>
    /// 启动一次后台安装,立即返回作业号(下载 80MB 需要进度反馈)。
    /// 已经有安装任务在跑时直接复用那个作业号,避免两个任务抢同一个临时文件。
    /// </summary>
    public string StartInstall()
    {
        var running = _activeJobId;
        if (running is not null
            && _jobs.TryGetValue(running, out var active)
            && !active.Finished)
        {
            return running;
        }

        var jobId = Guid.NewGuid().ToString("N");
        var job = new InstallJob();
        _jobs[jobId] = job;
        _activeJobId = jobId;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await InstallAsync(
                    new Progress<EngineInstallProgress>(p =>
                    {
                        job.Progress.Stage = p.Stage;
                        job.Progress.Percent = p.Percent;
                        job.Progress.Detail = p.Detail;
                    }),
                    job.Cts.Token);

                job.Result = result;
                job.Progress.Percent = 100;
                job.Progress.Stage = result.Success ? "完成" : "失败";
            }
            catch (OperationCanceledException)
            {
                job.Result = new EngineInstallResult { Success = false, Error = "安装已取消。" };
                job.Progress.Stage = "已取消";
            }
            catch (Exception ex)
            {
                job.Result = new EngineInstallResult { Success = false, Error = ex.Message };
                job.Progress.Stage = "失败";
            }
            finally
            {
                job.Progress.Detail = job.Result?.Error;
                job.Finished = true;
            }
        });

        return jobId;
    }

    public (EngineInstallProgress Progress, EngineInstallResult? Result) GetInstallStatus(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            return (new EngineInstallProgress { Stage = "未知作业", Percent = 0 }, null);
        }

        return (job.Progress, job.Result);
    }

    public async Task<EngineInstallResult> InstallAsync(
        IProgress<EngineInstallProgress>? progress,
        CancellationToken ct = default)
    {
        if (!_platform.IsSupported)
        {
            return Fail(
                $"当前环境({_platform.DisplayName})没有官方 Stockfish 发行包,"
                + "请自行下载引擎后在设置页手动指定可执行文件路径。");
        }

        Directory.CreateDirectory(EnginesRoot);
        var tmpDir = Path.Combine(EnginesRoot, ".tmp");
        Directory.CreateDirectory(tmpDir);

        // 1. 解析下载地址
        progress?.Report(new EngineInstallProgress
        {
            Stage = "查询最新版本",
            Percent = 2,
            Detail = _platform.DisplayName,
        });

        DownloadSource source;
        try
        {
            source = await ResolveFromGitHubAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查询 GitHub Release 失败,尝试兜底地址");
            var fallback = ResolveFallback();
            if (fallback is null)
            {
                return Fail($"无法获取 Stockfish 下载地址: {ex.Message}");
            }

            source = fallback;
        }

        // 2. 下载
        var archiveName = GuessArchiveFileName(source.Url);
        var archivePath = Path.Combine(tmpDir, archiveName);
        progress?.Report(new EngineInstallProgress
        {
            Stage = "下载中",
            Percent = 5,
            Detail = source.Url,
        });

        try
        {
            await DownloadAsync(source.Url, archivePath, progress, ct);
        }
        catch (Exception ex)
        {
            SafeDelete(archivePath);
            return Fail($"下载失败: {ex.Message}");
        }

        // 3. 校验 SHA-256
        progress?.Report(new EngineInstallProgress { Stage = "校验完整性", Percent = 80 });
        string actualHash;
        try
        {
            actualHash = await ComputeSha256Async(archivePath, ct);
        }
        catch (Exception ex)
        {
            SafeDelete(archivePath);
            return Fail($"计算校验值失败: {ex.Message}");
        }

        if (source.Sha256 is not null
            && !actualHash.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            SafeDelete(archivePath);
            return Fail($"SHA-256 校验不通过。期望 {source.Sha256},实际 {actualHash}。已删除下载文件,未做任何安装。");
        }

        if (source.Sha256 is null)
        {
            _logger.LogWarning("下载源 {Source} 没有提供 SHA-256,只能依赖 HTTPS 传输完整性。", source.Url);
        }

        // 4. 解压(按内容判断是 zip 还是 tar.gz,不信任扩展名)
        progress?.Report(new EngineInstallProgress { Stage = "解压中", Percent = 85 });
        var destDir = Path.Combine(EnginesRoot, $"stockfish-{source.Version}");
        var archiveKind = DetectArchiveKindByContent(archivePath);
        try
        {
            if (archiveKind == EngineArchiveKind.Unknown)
            {
                SafeDelete(archivePath);
                return Fail("下载到的文件既不是 zip 也不是 tar.gz,无法解压。");
            }

            if (source.Archive != EngineArchiveKind.Unknown && source.Archive != archiveKind)
            {
                _logger.LogWarning(
                    "下载文件的实际格式({Actual})与预期({Expected})不一致,按实际格式解压。",
                    archiveKind,
                    source.Archive);
            }

            if (Directory.Exists(destDir))
            {
                Directory.Delete(destDir, recursive: true);
            }

            Directory.CreateDirectory(destDir);
            ExtractArchive(archivePath, archiveKind, destDir);
        }
        catch (Exception ex)
        {
            SafeDelete(archivePath);
            return Fail($"解压失败: {ex.Message}");
        }
        finally
        {
            SafeDelete(archivePath);
        }

        // 5. 定位可执行文件
        progress?.Report(new EngineInstallProgress { Stage = "定位引擎", Percent = 92 });
        var exe = FindEngineExecutable(destDir, _platform);
        if (exe is null)
        {
            return Fail("解压完成,但没有在目录中找到 stockfish 可执行文件。");
        }

        // 6. Linux / macOS 下补上执行权限(zip 解压会丢掉 x 位)
        if (!EngineFile.EnsureExecutable(exe))
        {
            _logger.LogWarning("无法给引擎文件 {Path} 添加执行权限,启动可能失败。", exe);
        }

        // 7. 注册路径
        await _settings.SetAsync(SettingsStore.EnginePath, exe, ct);
        await _settings.SetAsync(SettingsStore.EngineVersion, source.Version, ct);
        await _settings.SetAsync(SettingsStore.EnginePlatformKey, _platform.Key, ct);
        await _settings.SetAsync(SettingsStore.EngineInstalledAt, DateTimeOffset.Now.ToString("O"), ct);
        await _settings.SetAsync(SettingsStore.EngineLastError, null, ct);

        // 8. 重启引擎,确认能连上
        progress?.Report(new EngineInstallProgress { Stage = "启动引擎", Percent = 97 });
        try
        {
            await _engineManager.RestartAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "安装后启动引擎失败");
            return new EngineInstallResult
            {
                Success = true,
                Error = $"引擎已安装到 {exe},但启动验证失败: {ex.Message}。可稍后在设置页重试。",
                Version = source.Version,
                EnginePath = exe,
                Sha256 = actualHash,
                Source = source.Source,
                Platform = _platform.DisplayName,
            };
        }

        progress?.Report(new EngineInstallProgress { Stage = "完成", Percent = 100 });
        return new EngineInstallResult
        {
            Success = true,
            Version = source.Version,
            EnginePath = exe,
            Sha256 = actualHash,
            SizeBytes = new FileInfo(exe).Length,
            Source = source.Source,
            Platform = _platform.DisplayName,
        };
    }

    /// <summary>
    /// 从 GitHub Release 里挑一个适配当前平台的资产。
    /// 优先用配置里指定的资产名(前提是它确实属于当前平台),否则按「系统 + 架构 + 通用版」打分。
    /// </summary>
    private async Task<DownloadSource> ResolveFromGitHubAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("stockfish");
        var json = await client.GetStringAsync(ReleasesApi, ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "unknown";

        if (!root.TryGetProperty("assets", out var assets))
        {
            throw new InvalidOperationException("Release 响应中没有 assets。");
        }

        var configured = _options.AssetName?.Trim();
        if (!string.IsNullOrEmpty(configured) && _platform.ScoreAssetName(configured) <= 0)
        {
            _logger.LogWarning(
                "配置的资产名 {Asset} 不适用于当前平台 {Platform},改为自动匹配。",
                configured,
                _platform.DisplayName);
        }

        string? bestUrl = null;
        string? bestDigest = null;
        string? bestName = null;
        var bestScore = 0;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString();
            if (url is null)
            {
                continue;
            }

            var digest = NormalizeDigest(asset.TryGetProperty("digest", out var d) ? d.GetString() : null);

            var score = _platform.ScoreAssetName(name);
            if (score <= 0)
            {
                continue;
            }

            // 配置里点名要的资产优先级最高
            if (!string.IsNullOrEmpty(configured)
                && configured.Equals(name, StringComparison.OrdinalIgnoreCase)
                && score > 0)
            {
                score += 1000;
            }

            if (score > bestScore || (score == bestScore && bestName is not null && string.CompareOrdinal(name, bestName) < 0))
            {
                bestScore = score;
                bestUrl = url;
                bestDigest = digest;
                bestName = name;
            }
        }

        if (bestUrl is null)
        {
            throw new InvalidOperationException(
                $"Release {tag} 中没有找到适配 {_platform.DisplayName} 的资产"
                + $"(期望类似 {_platform.AssetName})。");
        }

        return new DownloadSource(bestUrl, bestDigest, tag, "github-api", EnginePlatform.DetectArchiveKind(bestName ?? bestUrl));
    }

    /// <summary>
    /// 兜底地址:优先取当前平台(含架构)的配置,其次取只写系统的,最后取 <c>default</c>;
    /// 旧版的扁平配置只有在资产名与当前平台匹配时才用。
    /// </summary>
    private DownloadSource? ResolveFallback()
    {
        foreach (var key in new[] { _platform.FullKey, _platform.Key, "default" })
        {
            if (_options.Fallbacks.TryGetValue(key, out var fallback)
                && !string.IsNullOrWhiteSpace(fallback.Url))
            {
                return ToFallback(fallback);
            }
        }

        if (!string.IsNullOrWhiteSpace(_options.FallbackUrl)
            && _platform.ScoreAssetName(AssetNameFromUrl(_options.FallbackUrl)) > 0)
        {
            return new DownloadSource(
                _options.FallbackUrl,
                string.IsNullOrWhiteSpace(_options.FallbackSha256) ? null : _options.FallbackSha256,
                string.IsNullOrWhiteSpace(_options.FallbackVersion) ? "fallback" : _options.FallbackVersion,
                "fallback",
                EnginePlatform.DetectArchiveKind(_options.FallbackUrl));
        }

        return null;
    }

    private DownloadSource ToFallback(EngineFallbackOptions fallback) => new(
        fallback.Url,
        string.IsNullOrWhiteSpace(fallback.Sha256) ? null : fallback.Sha256,
        string.IsNullOrWhiteSpace(fallback.Version) ? "fallback" : fallback.Version,
        "fallback",
        EnginePlatform.DetectArchiveKind(fallback.Url));

    private static string? NormalizeDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        var value = digest.Trim();
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            value = value["sha256:".Length..];
        }

        return value.Length == 64 ? value.ToLowerInvariant() : null;
    }

    private async Task DownloadAsync(
        string url,
        string destination,
        IProgress<EngineInstallProgress>? progress,
        CancellationToken ct)
    {
        var proxyUrl = await ResolveProxyUrlAsync(ct);

        // 没配代理就用注入的命名客户端(测试可以替换它的 handler);
        // 配了代理才自建带代理的客户端——代理属于传输层,不该影响可测试性。
        HttpClient? ownedClient = null;
        try
        {
            var client = string.IsNullOrWhiteSpace(proxyUrl)
                ? _httpClientFactory.CreateClient("stockfish")
                : (ownedClient = CreateProxiedClient(proxyUrl));

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(destination);

            var buffer = new byte[128 * 1024];
            var stallSeconds = Math.Clamp(_options.StallTimeoutSeconds, 5, 600);
            long written = 0;

            while (true)
            {
                int read;

                // 每次读取单独计时:某些网络下连接会"连上但不再传数据",
                // 不做停滞检测的话会一直挂到 HttpClient 的整体超时。
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                readCts.CancelAfter(TimeSpan.FromSeconds(stallSeconds));

                try
                {
                    read = await source.ReadAsync(buffer.AsMemory(), readCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"下载停滞:连续 {stallSeconds} 秒没有收到数据。"
                        + $"已下载 {written / 1024 / 1024} MB。可在设置页填写「下载代理」后重试。");
                }

                if (read <= 0)
                {
                    break;
                }

                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                written += read;

                if (total > 0)
                {
                    var percent = 5 + (int)(75.0 * written / total);
                    progress?.Report(new EngineInstallProgress
                    {
                        Stage = "下载中",
                        Percent = Math.Clamp(percent, 5, 80),
                        Detail = $"{written / 1024 / 1024} / {total / 1024 / 1024} MB"
                            + (string.IsNullOrWhiteSpace(proxyUrl) ? string.Empty : " · 走代理"),
                    });
                }
            }
        }
        finally
        {
            ownedClient?.Dispose();
        }
    }

    /// <summary>代理地址:优先取设置页里的值,其次取 appsettings 的配置,都没有则直连。</summary>
    private async Task<string?> ResolveProxyUrlAsync(CancellationToken ct)
    {
        var fromDb = await _settings.GetAsync(SettingsStore.ProxyUrl, ct);
        if (!string.IsNullOrWhiteSpace(fromDb))
        {
            return fromDb.Trim();
        }

        return string.IsNullOrWhiteSpace(_options.ProxyUrl) ? null : _options.ProxyUrl.Trim();
    }

    /// <summary>建一个带代理的下载客户端。</summary>
    private static HttpClient CreateProxiedClient(string proxyUrl)
    {
        var handler = new HttpClientHandler
        {
            Proxy = new WebProxy(proxyUrl),
            UseProxy = true,
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(15),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ChessGameD/1.0");
        return client;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>从 URL 里取出资产文件名(用于挑选资产与命名临时文件)。</summary>
    public static string AssetNameFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "";
        }

        var path = url.Split('?')[0].Split('#')[0].TrimEnd('/');
        var slash = path.LastIndexOf('/');
        var segment = slash >= 0 ? path[(slash + 1)..] : path;
        return Uri.UnescapeDataString(segment);
    }

    /// <summary>把 URL 里的资产名变成一个安全的临时文件名。</summary>
    private static string GuessArchiveFileName(string url)
    {
        var segment = AssetNameFromUrl(url);
        var invalid = Path.GetInvalidFileNameChars();
        segment = new string(segment.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();

        if (segment.Length == 0)
        {
            segment = "stockfish-download";
        }

        if (EnginePlatform.DetectArchiveKind(segment) == EngineArchiveKind.Unknown)
        {
            segment += ".bin";
        }

        return segment;
    }

    /// <summary>按文件头判断压缩格式(zip 的 <c>PK</c> 与 gzip 的 <c>1F 8B</c>)。</summary>
    public static EngineArchiveKind DetectArchiveKindByContent(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> header = stackalloc byte[2];
            var read = fs.Read(header);
            if (read >= 2 && header[0] == 0x1f && header[1] == 0x8b)
            {
                return EngineArchiveKind.TarGz;
            }

            if (read >= 2 && header[0] == (byte)'P' && header[1] == (byte)'K')
            {
                return EngineArchiveKind.Zip;
            }
        }
        catch
        {
            // 读不出来就当无法识别
        }

        return EngineArchiveKind.Unknown;
    }

    /// <summary>解压:zip 用 <see cref="ZipFile"/>,tar(.gz) 走 <see cref="TarReader"/>。</summary>
    private static void ExtractArchive(string archivePath, EngineArchiveKind kind, string destDir)
    {
        if (kind == EngineArchiveKind.Zip)
        {
            ZipFile.ExtractToDirectory(archivePath, destDir, overwriteFiles: true);
            return;
        }

        ExtractTar(archivePath, destDir);
    }

    /// <summary>
    /// 解开 tar / tar.gz。官方 Linux 包是 <c>.tar.gz</c>,里面还带了一份源码目录,
    /// 只取需要的文件即可。解压时保留 Unix 权限位,并挡住 <c>../</c> 目录穿越。
    /// </summary>
    private static void ExtractTar(string archivePath, string destDir)
    {
        var root = Path.GetFullPath(destDir);
        Directory.CreateDirectory(root);

        using var fs = File.OpenRead(archivePath);
        var header = new byte[2];
        var isGzip = fs.Read(header, 0, 2) == 2 && header[0] == 0x1f && header[1] == 0x8b;
        fs.Position = 0;

        using var gzip = isGzip ? new GZipStream(fs, CompressionMode.Decompress) : null;
        using var reader = new TarReader(gzip ?? (Stream)fs);

        while (reader.GetNextEntry() is { } entry)
        {
            var name = entry.Name.Replace('\\', '/');
            if (name.Length == 0)
            {
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(root, name));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                // 越界路径直接丢掉
                continue;
            }

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(target);
                    break;

                case TarEntryType.RegularFile:
                {
                    var dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    using (var output = File.Create(target))
                    {
                        var data = entry.DataStream;
                        if (data is not null)
                        {
                            data.CopyTo(output);
                        }
                    }

                    ApplyUnixMode(target, entry);
                    break;
                }

                default:
                    // 符号链接、特殊设备等一律跳过
                    break;
            }
        }
    }

    /// <summary>把 tar 条目的权限位写到文件上(Windows 上忽略)。</summary>
    private static void ApplyUnixMode(string path, TarEntry entry)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var mode = entry.Mode;
            if (mode == UnixFileMode.None)
            {
                return;
            }

            mode |= UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(path, mode);
        }
        catch
        {
            // 权限写不进去不影响后续补 chmod
        }
    }

    /// <summary>在解压目录中查找引擎可执行文件(排除 .nnue 之类的伴随文件)。</summary>
    public static string? FindEngineExecutable(string directory)
        => FindEngineExecutable(directory, null);

    /// <summary>
    /// 在解压目录中查找引擎可执行文件(排除 .nnue 之类的伴随文件)。
    /// 传入平台时会优先挑该平台形态的可执行文件(Windows 选 .exe,Unix 选无扩展名的)。
    /// </summary>
    public static string? FindEngineExecutable(string directory, EnginePlatform? platform)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var candidates = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(IsEngineCandidate)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        // 名字就叫 stockfish / stockfish.exe 的最优先
        var exact = candidates.FirstOrDefault(f =>
            Path.GetFileNameWithoutExtension(f).Equals("stockfish", StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // 其次按平台偏好挑
        var preferExe = platform?.Os == EngineOsKind.Windows;
        var preferred = preferExe
            ? candidates.FirstOrDefault(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            : candidates.FirstOrDefault(f => Path.GetExtension(f).Length == 0);

        // 最后取体积最大的那个(内嵌网络的通用版体积最大)
        return preferred
            ?? candidates.OrderByDescending(f => new FileInfo(f).Length).First();
    }

    /// <summary>文件名像不像引擎可执行文件。</summary>
    private static bool IsEngineCandidate(string file)
    {
        var name = Path.GetFileName(file);
        if (!name.StartsWith("stockfish", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (name.Contains("nnue", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var ext = Path.GetExtension(name);
        return ext.Length == 0 || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 忽略
        }
    }

    private EngineInstallResult Fail(string error) => new()
    {
        Success = false,
        Error = error,
        Platform = _platform.DisplayName,
    };

    private sealed record DownloadSource(
        string Url,
        string? Sha256,
        string Version,
        string Source,
        EngineArchiveKind Archive);

    private sealed class InstallJob
    {
        public CancellationTokenSource Cts { get; } = new();
        public EngineInstallProgress Progress { get; } = new();
        public EngineInstallResult? Result { get; set; }

        /// <summary>任务是否已经结束。</summary>
        public bool Finished { get; set; }
    }
}
