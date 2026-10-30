using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ChessGameD.Web.Data;

/// <summary>
/// 键值型配置的读写门面。写入立即落库并失效缓存,读取走内存缓存。
/// </summary>
public class SettingsStore
{
    public const string EnginePath = "Engine.Path";
    public const string EngineVersion = "Engine.Version";
    public const string EngineInstalledAt = "Engine.InstalledAt";

    /// <summary>引擎安装时的平台键(windows / linux / macos),用于检测跨平台搬运的引擎。</summary>
    public const string EnginePlatformKey = "Engine.PlatformKey";
    public const string EngineThreads = "Engine.Threads";
    public const string EngineHashMb = "Engine.HashMb";
    public const string EngineMultiPv = "Engine.MultiPv";
    public const string EngineDefaultDepth = "Engine.DefaultDepth";
    public const string EngineAnalysisDepth = "Engine.AnalysisDepth";
    public const string EngineLastError = "Engine.LastError";

    /// <summary>下载引擎时使用的代理地址(留空 = 直连)。</summary>
    public const string ProxyUrl = "Network.ProxyUrl";

    /// <summary>每局指导对弈的提示次数。</summary>
    public const string PlayHintQuota = "Play.HintQuota";

    /// <summary>每局指导对弈的悔棋次数。</summary>
    public const string PlayTakebackQuota = "Play.TakebackQuota";

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SettingsStore> _logger;

    public SettingsStore(
        IDbContextFactory<AppDbContext> factory,
        IMemoryCache cache,
        ILogger<SettingsStore> logger)
    {
        _factory = factory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        if (_cache.TryGetValue($"setting:{key}", out string? cached))
        {
            return cached;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        var value = row?.Value;
        _cache.Set($"setting:{key}", value, TimeSpan.FromMinutes(30));
        return value;
    }

    public async Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            db.Settings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }

        await db.SaveChangesAsync(ct);
        _cache.Remove($"setting:{key}");
    }

    public async Task<int> GetIntAsync(string key, int defaultValue, CancellationToken ct = default)
    {
        var raw = await GetAsync(key, ct);
        return int.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    public async Task SetIntAsync(string key, int value, CancellationToken ct = default)
        => await SetAsync(key, value.ToString(), ct);

    /// <summary>读取引擎路径;为空或文件不存在时返回 null。</summary>
    public async Task<string?> GetValidEnginePathAsync(CancellationToken ct = default)
    {
        var path = await GetAsync(EnginePath, ct);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!File.Exists(path))
        {
            _logger.LogWarning("配置的引擎路径不存在: {Path}", path);
            return null;
        }

        return path;
    }
}
