using ChessGameD.Web.Data;

namespace ChessGameD.Web.Services.Engine;

/// <summary>
/// 陪练引擎的抽象。
///
/// 对弈服务只依赖这个接口,测试里可以换成假的实现(不用真拉起 Stockfish)。
/// </summary>
public interface ICoachEngine
{
    /// <summary>分析一个局面,返回<b>相对白方</b>的评估与候选着法。</summary>
    Task<EngineAnalysis> AnalyzeAsync(string fen, CancellationToken ct = default);
}

/// <summary>
/// 陪练引擎:与分析引擎分开的<b>第二个</b> Stockfish 进程。
///
/// 为什么单独起一个:
/// - 分析引擎全进程只有一份,整局分析会占住它好几分钟;对弈要的是「落子后一两秒有回应」,
///   两者挤在同一条队列里会互相拖死;
/// - <see cref="UciEngineClient"/> 是「独占进程 + 单写锁」模型,插队就得打断正在跑的搜索,
///   风险和调试成本都高得多。
///
/// 这个进程只服务指导对弈,参数调小(1 线程 / 64MB),内存占用可以接受。
/// 对局本身是串行的:同一时刻只跑一盘,后面的排着。
/// </summary>
public sealed class CoachEngine : IHostedService, ICoachEngine, IAsyncDisposable
{
    /// <summary>陪练对局的搜索深度。</summary>
    public const int SearchDepth = 14;

    /// <summary>陪练对局的候选数(要靠候选来「放水」)。</summary>
    public const int MultiPv = 3;

    /// <summary>陪练引擎的 Hash 内存(MB),比分析引擎小得多。</summary>
    private const int HashMb = 64;

    /// <summary>搜索硬超时(毫秒)。</summary>
    private const int TimeoutMs = 30_000;

    private readonly SettingsStore _settings;
    private readonly ILogger<CoachEngine> _logger;
    private readonly EnginePlatform _platform;
    private readonly FifoGate _gate = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private UciEngineClient? _client;

    public CoachEngine(SettingsStore settings, ILogger<CoachEngine> logger, EnginePlatform platform)
    {
        _settings = settings;
        _logger = logger;
        _platform = platform;
    }

    /// <summary>陪练队列(先进先出)。</summary>
    public FifoGate Queue => _gate;

    /// <summary>陪练引擎是否已经就绪。</summary>
    public bool IsRunning => _client is { IsRunning: true };

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 不阻塞应用启动:等第一次真正下棋时再拉起引擎
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await ResetAsync();
    }

    /// <summary>
    /// 分析一个局面,返回<b>相对白方</b>的评估与候选着法。
    ///
    /// 陪练对局里的局面评估都走这里,所以它跟整局分析用的是同一种口径。
    /// </summary>
    public async Task<EngineAnalysis> AnalyzeAsync(string fen, CancellationToken ct = default)
    {
        var ticket = _gate.Enqueue();
        var granted = false;

        try
        {
            await _gate.WaitAsync(ticket, ct);
            granted = true;

            var options = new UciSearchOptions
            {
                Depth = SearchDepth,
                MultiPv = MultiPv,
                TimeoutMs = TimeoutMs,
            };

            var raw = await RunSearchWithRetryAsync(fen, options, ct);
            return EngineManager.ToAnalysis(raw, fen);
        }
        finally
        {
            if (granted)
            {
                _gate.Release();
            }
        }
    }

    private async Task<UciSearchResult> RunSearchWithRetryAsync(
        string fen,
        UciSearchOptions options,
        CancellationToken ct)
    {
        var client = await EnsureClientAsync(ct);
        try
        {
            return await client.SearchAsync(fen, null, options, ct);
        }
        catch (EngineCrashedException ex)
        {
            _logger.LogWarning(ex, "陪练引擎崩溃,重启后重试一次");
            await ResetAsync();
            client = await EnsureClientAsync(ct);
            return await client.SearchAsync(fen, null, options, ct);
        }
    }

    private async Task<UciEngineClient> EnsureClientAsync(CancellationToken ct)
    {
        if (_client is { IsRunning: true })
        {
            return _client;
        }

        await _lifecycleLock.WaitAsync(ct);
        try
        {
            if (_client is { IsRunning: true })
            {
                return _client;
            }

            var path = await _settings.GetValidEnginePathAsync(ct);
            if (path is null)
            {
                throw new EngineNotInstalledException(
                    "尚未安装 Stockfish 引擎,请先到设置页点击「一键安装 Stockfish」。");
            }

            // Linux / macOS 上引擎文件必须带执行位(zip 解压会把 x 位丢掉)
            if (!EngineFile.HasExecutablePermission(path))
            {
                EngineFile.EnsureExecutable(path);
            }

            if (_client is not null)
            {
                await _client.DisposeAsync();
            }

            var client = new UciEngineClient();
            try
            {
                await client.StartAsync(path, ct);
                await client.HandshakeAsync(ct);
                await ApplyOptionsAsync(client, ct);
                _client = client;
                _logger.LogInformation("陪练引擎已就绪: {Name}", client.EngineName);
            }
            catch (Exception)
            {
                await client.DisposeAsync();
                throw;
            }

            return client;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>陪练引擎的参数:线程少、Hash 小,省内存。</summary>
    private async Task ApplyOptionsAsync(UciEngineClient client, CancellationToken ct)
    {
        if (client.Options.ContainsKey("Threads"))
        {
            await client.SetOptionAsync("Threads", "1", ct);
        }

        if (client.Options.ContainsKey("Hash"))
        {
            await client.SetOptionAsync("Hash", HashMb.ToString(), ct);
        }

        // MultiPV 是引擎选项,不是 go 参数:不设的话每次只回一条候选,
        // 陪练就没法在候选里抽签「放水」了
        if (client.Options.ContainsKey("MultiPV"))
        {
            await client.SetOptionAsync("MultiPV", MultiPv.ToString(), ct);
        }
    }

    private async Task ResetAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_client is not null)
            {
                await _client.DisposeAsync();
                _client = null;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
    }
}
