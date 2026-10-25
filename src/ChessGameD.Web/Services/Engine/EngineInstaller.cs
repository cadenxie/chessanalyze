using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ChessGameD.Web.Data;
using Microsoft.Extensions.Options;

namespace ChessGameD.Web.Services.Engine;

/// <summary>
/// 应用内一键安装 Stockfish:
/// 查最新 Release → 下载 → 校验 SHA-256 → 解压 → 定位可执行文件 → 注册路径。
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

    public EngineInstaller(
        IHttpClientFactory httpClientFactory,
        SettingsStore settings,
        EngineManager engineManager,
        IOptions<ChessOptions> options,
        IWebHostEnvironment environment,
        ILogger<EngineInstaller> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _engineManager = engineManager;
        _options = options.Value.Engine;
        _environment = environment;
        _logger = logger;
    }

    public string EnginesRoot => Path.Combine(_environment.ContentRootPath, "App_Data", "engines");

    private readonly ConcurrentDictionary<string, InstallJob> _jobs = new();

    /// <summary>启动一次后台安装,立即返回作业号(下载 80MB 需要进度反馈)。</summary>
    public string StartInstall()
    {
        var jobId = Guid.NewGuid().ToString("N");
        var job = new InstallJob();
        _jobs[jobId] = job;

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
        Directory.CreateDirectory(EnginesRoot);
        var tmpDir = Path.Combine(EnginesRoot, ".tmp");
        Directory.CreateDirectory(tmpDir);

        // 1. 解析下载地址
        progress?.Report(new EngineInstallProgress { Stage = "查询最新版本", Percent = 2 });
        DownloadSource source;
        try
        {
            source = await ResolveFromGitHubAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查询 GitHub Release 失败,尝试兜底地址");
            if (string.IsNullOrWhiteSpace(_options.FallbackUrl))
            {
                return Fail($"无法获取 Stockfish 下载地址: {ex.Message}");
            }

            source = new DownloadSource(
                _options.FallbackUrl,
                string.IsNullOrWhiteSpace(_options.FallbackSha256) ? null : _options.FallbackSha256,
                string.IsNullOrWhiteSpace(_options.FallbackVersion) ? "fallback" : _options.FallbackVersion,
                "fallback");
        }

        // 2. 下载
        var zipPath = Path.Combine(tmpDir, $"stockfish-{source.Version}.zip");
        progress?.Report(new EngineInstallProgress
        {
            Stage = "下载中",
            Percent = 5,
            Detail = source.Url,
        });

        try
        {
            await DownloadAsync(source.Url, zipPath, progress, ct);
        }
        catch (Exception ex)
        {
            SafeDelete(zipPath);
            return Fail($"下载失败: {ex.Message}");
        }

        // 3. 校验 SHA-256
        progress?.Report(new EngineInstallProgress { Stage = "校验完整性", Percent = 80 });
        string actualHash;
        try
        {
            actualHash = await ComputeSha256Async(zipPath, ct);
        }
        catch (Exception ex)
        {
            SafeDelete(zipPath);
            return Fail($"计算校验值失败: {ex.Message}");
        }

        if (source.Sha256 is not null
            && !actualHash.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            SafeDelete(zipPath);
            return Fail($"SHA-256 校验不通过。期望 {source.Sha256},实际 {actualHash}。已删除下载文件,未做任何安装。");
        }

        // 4. 解压
        progress?.Report(new EngineInstallProgress { Stage = "解压中", Percent = 85 });
        var destDir = Path.Combine(EnginesRoot, $"stockfish-{source.Version}");
        try
        {
            if (Directory.Exists(destDir))
            {
                Directory.Delete(destDir, recursive: true);
            }

            ZipFile.ExtractToDirectory(zipPath, destDir, overwriteFiles: true);
        }
        catch (Exception ex)
        {
            SafeDelete(zipPath);
            return Fail($"解压失败: {ex.Message}");
        }
        finally
        {
            SafeDelete(zipPath);
        }

        // 5. 定位可执行文件
        progress?.Report(new EngineInstallProgress { Stage = "定位引擎", Percent = 92 });
        var exe = FindEngineExecutable(destDir);
        if (exe is null)
        {
            return Fail("解压完成,但没有在目录中找到 stockfish 可执行文件。");
        }

        // 6. 注册路径
        await _settings.SetAsync(SettingsStore.EnginePath, exe, ct);
        await _settings.SetAsync(SettingsStore.EngineInstalledAt, DateTimeOffset.Now.ToString("O"), ct);
        await _settings.SetAsync(SettingsStore.EngineLastError, null, ct);

        // 7. 重启引擎,确认能连上
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
        };
    }

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

        string? exactUrl = null;
        string? exactDigest = null;
        string? fallbackUrl = null;
        string? fallbackDigest = null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString();
            if (url is null)
            {
                continue;
            }

            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            digest = NormalizeDigest(digest);

            if (name.Equals(_options.AssetName, StringComparison.OrdinalIgnoreCase))
            {
                exactUrl = url;
                exactDigest = digest;
                break;
            }

            if (fallbackUrl is null
                && name.StartsWith("stockfish-windows-", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                && name.Contains("x86-64", StringComparison.OrdinalIgnoreCase))
            {
                fallbackUrl = url;
                fallbackDigest = digest;
            }
        }

        var chosenUrl = exactUrl ?? fallbackUrl;
        if (chosenUrl is null)
        {
            throw new InvalidOperationException($"Release {tag} 中没有找到 Windows x86-64 的 zip 资产。");
        }

        var chosenDigest = exactUrl is not null ? exactDigest : fallbackDigest;
        return new DownloadSource(chosenUrl, chosenDigest, tag, "github-api");
    }

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

    /// <summary>在解压目录中查找引擎可执行文件(排除 .nnue 之类的伴随文件)。</summary>
    public static string? FindEngineExecutable(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var candidates = Directory.EnumerateFiles(directory, "*.exe", SearchOption.AllDirectories)
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return name.StartsWith("stockfish", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("nnue", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        // 优先精确匹配 stockfish.exe,否则取最大的那个(内嵌网络的通用版体积最大)
        return candidates.FirstOrDefault(f =>
                   Path.GetFileName(f).Equals("stockfish.exe", StringComparison.OrdinalIgnoreCase))
               ?? candidates.OrderByDescending(f => new FileInfo(f).Length).First();
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

    private static EngineInstallResult Fail(string error) => new() { Success = false, Error = error };

    private sealed record DownloadSource(string Url, string? Sha256, string Version, string Source);

    private sealed class InstallJob
    {
        public CancellationTokenSource Cts { get; } = new();
        public EngineInstallProgress Progress { get; } = new();
        public EngineInstallResult? Result { get; set; }
    }
}
