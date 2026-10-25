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
}

public sealed class EngineNotInstalledException : Exception
{
    public EngineNotInstalledException(string message) : base(message) { }
}

public sealed class EngineBusyException : Exception
{
    public EngineBusyException(string message) : base(message) { }
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
/// - 全局搜索锁保证同一时刻只有一次搜索;
/// - 整局分析(批量)持锁直到结束,期间的即时分析请求排队,超过 60 秒返回"引擎忙";
/// - 引擎崩溃后自动重启,并对当前这次搜索重试一次。
/// </summary>
public sealed class EngineManager : IHostedService, IAsyncDisposable
{
    private readonly SettingsStore _settings;
    private readonly ILogger<EngineManager> _logger;
    private readonly SemaphoreSlim _searchLock = new(1, 1);
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private UciEngineClient? _client;
    private EngineState _state = EngineState.NotInstalled;
    private string? _lastError;

    public EngineManager(SettingsStore settings, ILogger<EngineManager> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<EngineStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var configured = await _settings.GetAsync(SettingsStore.EnginePath, ct);
        var valid = await _settings.GetValidEnginePathAsync(ct);

        if (valid is null)
        {
            return new EngineStatus
            {
                State = configured is null ? EngineState.NotInstalled : EngineState.Faulted,
                Path = configured,
                Version = await _settings.GetAsync(SettingsStore.EngineVersion, ct),
                LastError = await _settings.GetAsync(SettingsStore.EngineLastError, ct),
            };
        }

        return new EngineStatus
        {
            State = _state,
            Path = valid,
            Version = _client?.EngineName ?? await _settings.GetAsync(SettingsStore.EngineVersion, ct),
            LastError = _lastError ?? await _settings.GetAsync(SettingsStore.EngineLastError, ct),
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
        if (!await _searchLock.WaitAsync(TimeSpan.FromSeconds(60), ct))
        {
            throw new EngineBusyException("整局分析正在占用引擎,请稍后再试,或先停止整局分析。");
        }

        try
        {
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
        finally
        {
            _searchLock.Release();
        }
    }

    /// <summary>批量分析(整局)。持锁直到全部完成,期间可被取消令牌打断。</summary>
    public async Task AnalyzeBatchAsync(
        IReadOnlyList<(int Ply, string Fen)> positions,
        UciSearchOptions options,
        Func<(int Ply, EngineAnalysis Analysis), Task>? onResult,
        IProgress<int>? progress,
        CancellationToken ct = default)
    {
        if (positions.Count == 0)
        {
            return;
        }

        await _searchLock.WaitAsync(ct);
        try
        {
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
            _searchLock.Release();
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

            foreach (var proc in Process.GetProcessesByName("stockfish"))
            {
                try
                {
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
