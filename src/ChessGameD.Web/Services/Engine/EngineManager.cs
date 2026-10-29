using System.Diagnostics;
using ChessGameD.Web.Data;

namespace ChessGameD.Web.Services.Engine;

public enum EngineState
{
    NotInstalled,
    Starting,
    Ready,
    Searching,
    Faulted,
}

public sealed class EngineStatus
{
    public EngineState State { get; set; } = EngineState.NotInstalled;
    public string? Path { get; set; }
    public string? Version { get; set; }
    public string? LastError { get; set; }
    public bool IsReady => State == EngineState.Ready;
    public bool IsInstalled => !string.IsNullOrEmpty(Path);

    /// <summary>引擎安装时的平台键(windows / linux / macos)。</summary>
    public string? PlatformKey { get; set; }

    /// <summary>当前运行平台,例如「Linux x64」。</summary>
    public string CurrentPlatform { get; set; } = "";

    /// <summary>已安装的引擎与当前运行平台不一致(例如把 Windows 的引擎搬到 Linux 上)。</summary>
    public bool PlatformMismatch { get; set; }
}

public sealed class EngineNotInstalledException : Exception
{
    public EngineNotInstalledException(string message) : base(message) { }
}

public sealed class EngineBusyException : Exception
{
    public EngineBusyException(string message) : base(message) { }
}

/// <summary>分析队列的状态。</summary>
public sealed class EngineQueueStatus
{
    /// <summary>正在排队的任务数。</summary>
    public int Waiting { get; set; }

    /// <summary>本次任务的位次(1 起)。</summary>
    public int Position { get; set; }
}

/// <summary>一条 MultiPV 候选。</summary>
public sealed record MultiPvLine(int MultiPv, int? ScoreCp, int? ScoreMate, IReadOnlyList<string> Pv);

/// <summary>一次分析的对外结果,分值一律换算成<b>相对白方</b>。</summary>
public sealed record EngineAnalysis(
    int? ScoreCp,
    int? ScoreMate,
    int Depth,
    long Nodes,
    int Nps,
    int TimeMs,
    IReadOnlyList<string> Pv,
    IReadOnlyList<MultiPvLine> Lines,
    string BestMove);

/// <summary>
/// 引擎进程的唯一所有者(进程内只有这一个 Stockfish)。
///
/// 并发语义:
/// - 所有分析请求(即时分析、整局分析)共用一条先进先出的队列,同一时刻只有一次搜索;
/// - 整局分析排在队列里等待,等待期间能看到自己是第几位,不会报「引擎忙」;
/// - 引擎崩溃后自动重启,并对当前这次搜索重试一次。
/// </summary>
public sealed class EngineManager : IHostedService, IAsyncDisposable
{
    private readonly SettingsStore _settings;
    private readonly ILogger<EngineManager> _logger;
    private readonly EnginePlatform _platform;
    private readonly FifoGate _gate = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    /// <summary>即时分析最多排队多久(超过就告诉调用方队列情况)。</summary>
    private static readonly TimeSpan InstantAnalysisQueueWait = TimeSpan.FromSeconds(120);

    private UciEngineClient? _client;
    private EngineState _state = EngineState.NotInstalled;
    private string? _lastError;

    public EngineManager(SettingsStore settings, ILogger<EngineManager> logger, EnginePlatform platform)
    {
        _settings = settings;
        _logger = logger;
        _platform = platform;
    }

    /// <summary>当前运行平台。</summary>
    public EnginePlatform Platform => _platform;

    /// <summary>分析队列(先进先出)。</summary>
    public FifoGate Queue => _gate;

    /// <summary>当前队列里排着几个任务。</summary>
    public EngineQueueStatus GetQueueStatus() => new() { Waiting = _gate.WaitingCount };

    public async Task<EngineStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var configured = await _settings.GetAsync(SettingsStore.EnginePath, ct);
        var valid = await _settings.GetValidEnginePathAsync(ct);
        var installedPlatform = await _settings.GetAsync(SettingsStore.EnginePlatformKey, ct);

        var mismatch = !string.IsNullOrEmpty(installedPlatform)
            && !installedPlatform.Equals(_platform.Key, StringComparison.OrdinalIgnoreCase);

        if (valid is null)
        {
            return new EngineStatus
            {
                State = configured is null ? EngineState.NotInstalled : EngineState.Faulted,
                Path = configured,
                Version = await _settings.GetAsync(SettingsStore.EngineVersion, ct),
                LastError = await _settings.GetAsync(SettingsStore.EngineLastError, ct),
                PlatformKey = installedPlatform,
                CurrentPlatform = _platform.DisplayName,
                PlatformMismatch = mismatch,
            };
        }

        return new EngineStatus
        {
            State = _state,
            Path = valid,
            Version = _client?.EngineName ?? await _settings.GetAsync(SettingsStore.EngineVersion, ct),
            LastError = _lastError ?? await _settings.GetAsync(SettingsStore.EngineLastError, ct),
            PlatformKey = installedPlatform,
            CurrentPlatform = _platform.DisplayName,
            PlatformMismatch = mismatch,
        };
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 不阻塞应用启动:引擎可能还没安装,等第一次用到时再拉起
        CleanupOrphanProcesses();

        // 已经装好了就后台预热,免得第一次分析还要等引擎握手
        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureEngineAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "预启动引擎失败(不影响应用使用)");
            }
        });

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await ResetEngineAsync();
    }

    /// <summary>
    /// 安装/改路径后重启引擎:关掉旧进程并真正把新引擎拉起来。
    /// 启动失败会抛异常,由调用方决定怎么处理。
    /// </summary>
    public async Task<EngineStatus> RestartAsync(CancellationToken ct = default)
    {
        await ResetEngineAsync();
        await EnsureEngineAsync(ct);
        return await GetStatusAsync(ct);
    }

    public async Task<EngineAnalysis> AnalyzeAsync(
        string fen,
        string? moves,
        int? depth,
        int? moveTimeMs,
        int multiPv,
        CancellationToken ct = default)
    {
        // 即时分析也要排队(跟整局分析共用一条队列),前面有任务就等着
        var ticket = _gate.Enqueue();
        var granted = false;

        // 等太久就放弃:告诉调用方前面排了多少个,而不是干等着
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waitCts.CancelAfter(InstantAnalysisQueueWait);

        try
        {
            await _gate.WaitAsync(ticket, waitCts.Token);
            granted = true;

            var options = new UciSearchOptions
            {
                Depth = depth,
                MoveTimeMs = moveTimeMs,
                MultiPv = Math.Clamp(multiPv, 1, 8),
                TimeoutMs = 180_000,
            };

            var result = await RunSearchWithRetryAsync(fen, moves, options, ct);
            return ToAnalysis(result, fen);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!granted)
        {
            // 排队时被取消:把自己从队列里摘掉就行,不用释放闸门
            throw new EngineBusyException(
                $"引擎正在处理前面的任务(队列里还有 {ticket.Ahead} 个),请稍后再试。");
        }
        finally
        {
            if (granted)
            {
                _gate.Release();
            }
        }
    }

    /// <summary>
    /// 批量分析(整局)。排在全局队列里,等待期间通过 <paramref name="onEnqueued"/>
    /// 把排队凭条交出去,调用方可以显示出「排队中(第 N 位)」。
    /// </summary>
    public async Task AnalyzeBatchAsync(
        IReadOnlyList<(int Ply, string Fen)> positions,
        UciSearchOptions options,
        Func<(int Ply, EngineAnalysis Analysis), Task>? onResult,
        IProgress<int>? progress,
        Action<FifoGate.Ticket>? onEnqueued = null,
        Action? onStarted = null,
        CancellationToken ct = default)
    {
        if (positions.Count == 0)
        {
            return;
        }

        var ticket = _gate.Enqueue();
        var granted = false;

        try
        {
            onEnqueued?.Invoke(ticket);
            await _gate.WaitAsync(ticket, ct);
            granted = true;
            onStarted?.Invoke();

            var done = 0;
            foreach (var (ply, fen) in positions)
            {
                ct.ThrowIfCancellationRequested();
                var raw = await RunSearchWithRetryAsync(fen, null, options, ct);
                var analysis = ToAnalysis(raw, fen);
                if (onResult is not null)
                {
                    await onResult((ply, analysis));
                }

                done++;
                progress?.Report(done);
            }
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
        string? moves,
        UciSearchOptions options,
        CancellationToken ct)
    {
        var client = await EnsureEngineAsync(ct);
        _state = EngineState.Searching;
        try
        {
            return await client.SearchAsync(fen, moves, options, ct);
        }
        catch (EngineCrashedException ex)
        {
            _logger.LogWarning(ex, "引擎崩溃,重启后重试一次");
            await ResetEngineAsync();
            client = await EnsureEngineAsync(ct);
            return await client.SearchAsync(fen, moves, options, ct);
        }
        catch (TimeoutException)
        {
            await ResetEngineAsync();
            throw;
        }
        finally
        {
            _state = _client is { IsRunning: true } ? EngineState.Ready : _state;
        }
    }

    private async Task<UciEngineClient> EnsureEngineAsync(CancellationToken ct)
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
                _state = EngineState.NotInstalled;
                throw new EngineNotInstalledException(
                    "尚未安装 Stockfish 引擎,请先到设置页点击「一键安装 Stockfish」。");
            }

            // Linux / macOS 上引擎文件必须带执行位,zip 解压会把 x 位丢掉,这里补一下
            if (!EngineFile.HasExecutablePermission(path))
            {
                EngineFile.EnsureExecutable(path);
            }

            if (_client is not null)
            {
                await _client.DisposeAsync();
            }

            var client = new UciEngineClient();
            _state = EngineState.Starting;
            try
            {
                await client.StartAsync(path, ct);
                await client.HandshakeAsync(ct);
                await ApplyEngineOptionsAsync(client, ct);
                _client = client;
                _state = EngineState.Ready;
                _lastError = null;
                await _settings.SetAsync(SettingsStore.EngineVersion, client.EngineName, ct);
                await _settings.SetAsync(SettingsStore.EngineLastError, null, ct);
                _logger.LogInformation("引擎已就绪: {Name} ({Path})", client.EngineName, path);
            }
            catch (Exception ex)
            {
                _state = EngineState.Faulted;
                _lastError = ex.Message;
                await _settings.SetAsync(SettingsStore.EngineLastError, ex.Message, ct);
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

    private async Task ApplyEngineOptionsAsync(UciEngineClient client, CancellationToken ct)
    {
        var threads = await _settings.GetIntAsync(SettingsStore.EngineThreads, 0, ct);
        if (threads <= 0)
        {
            threads = Math.Max(1, Environment.ProcessorCount - 1);
        }

        var hashMb = await _settings.GetIntAsync(SettingsStore.EngineHashMb, 256, ct);

        if (client.Options.ContainsKey("Threads"))
        {
            await client.SetOptionAsync("Threads", threads.ToString(), ct);
        }

        if (client.Options.ContainsKey("Hash"))
        {
            await client.SetOptionAsync("Hash", hashMb.ToString(), ct);
        }
    }

    private async Task ResetEngineAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_client is not null)
            {
                await _client.DisposeAsync();
                _client = null;
            }

            _state = EngineState.NotInstalled;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>清理上次异常退出残留的引擎进程(只针对本项目目录下的引擎)。</summary>
    private void CleanupOrphanProcesses()
    {
        try
        {
            var enginesRoot = Path.Combine(AppContext.BaseDirectory, "App_Data", "engines");
            var candidates = new[] { enginesRoot, Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "engines") };

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    // Linux 上进程名会被截断到 15 个字符(stockfish-linux-x86-64-universal
                    // → "stockfish-linux"),所以按前缀匹配而不是全名匹配。
                    if (!proc.ProcessName.StartsWith("stockfish", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var file = proc.MainModule?.FileName;
                    if (file is null)
                    {
                        continue;
                    }

                    if (candidates.Any(root => file.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogWarning("清理残留引擎进程: {File}", file);
                        proc.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // 拿不到主模块路径(权限不足等),跳过
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "清理残留引擎进程时出错");
        }
    }

    internal static bool IsWhiteToMove(string fen)
    {
        var parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 || !parts[1].Equals("b", StringComparison.OrdinalIgnoreCase);
    }

    private static EngineAnalysis ToAnalysis(UciSearchResult result, string fen)
    {
        var whiteToMove = IsWhiteToMove(fen);
        var lines = result.Lines
            .Select(l => new MultiPvLine(
                l.MultiPv,
                l.ScoreCp is null ? null : (whiteToMove ? l.ScoreCp.Value : -l.ScoreCp.Value),
                l.ScoreMate is null ? null : (whiteToMove ? l.ScoreMate.Value : -l.ScoreMate.Value),
                l.Pv))
            .ToList();

        return new EngineAnalysis(
            result.ScoreCp is null ? null : (whiteToMove ? result.ScoreCp.Value : -result.ScoreCp.Value),
            result.ScoreMate is null ? null : (whiteToMove ? result.ScoreMate.Value : -result.ScoreMate.Value),
            result.Depth,
            result.Nodes,
            result.Nps,
            result.TimeMs,
            result.Pv,
            lines,
            result.BestMove);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }
}
