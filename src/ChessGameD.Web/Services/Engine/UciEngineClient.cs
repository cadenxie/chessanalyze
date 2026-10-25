using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace ChessGameD.Web.Services.Engine;

/// <summary>一条 <c>info</c> 行的解析结果。</summary>
public sealed class UciInfo
{
    public int Depth { get; set; }
    public int SelDepth { get; set; }
    public int MultiPv { get; set; } = 1;

    /// <summary>相对<b>该局面行棋方</b>的厘兵分值(UCI 原生口径)。</summary>
    public int? ScoreCp { get; set; }

    /// <summary>相对该局面行棋方的杀棋步数(正 = 该方胜)。</summary>
    public int? ScoreMate { get; set; }

    public long Nodes { get; set; }
    public int Nps { get; set; }
    public int TimeMs { get; set; }
    public IReadOnlyList<string> Pv { get; set; } = Array.Empty<string>();
}

/// <summary>一次搜索的汇总结果。</summary>
public sealed class UciSearchResult
{
    public int Depth { get; set; }
    public int? ScoreCp { get; set; }
    public int? ScoreMate { get; set; }
    public IReadOnlyList<string> Pv { get; set; } = Array.Empty<string>();
    public IReadOnlyList<UciInfo> Lines { get; set; } = Array.Empty<UciInfo>();
    public long Nodes { get; set; }
    public int Nps { get; set; }
    public int TimeMs { get; set; }
    public string BestMove { get; set; } = "";
}

public sealed class UciSearchOptions
{
    /// <summary>固定深度;与 <see cref="MoveTimeMs"/> 二选一,深度优先。</summary>
    public int? Depth { get; init; }

    /// <summary>固定搜索时长(毫秒)。</summary>
    public int? MoveTimeMs { get; init; }

    public int MultiPv { get; init; } = 1;

    /// <summary>硬超时;超时后发 stop 并回收。</summary>
    public int TimeoutMs { get; init; } = 120_000;

    public string BuildGoCommand()
        => Depth is > 0 ? $"go depth {Depth}" : $"go movetime {MoveTimeMs ?? 2000}";
}

/// <summary>引擎进程异常退出或协议错误。</summary>
public sealed class EngineCrashedException : Exception
{
    public EngineCrashedException(string message) : base(message) { }
}

/// <summary>
/// 自研的 UCI 客户端。
///
/// 之所以不用第三方包:<c>UciEngine</c> 的 <c>SendCommand</c> 会自旋阻塞到收到期望响应
/// (<c>go</c> 则阻塞到 <c>bestmove</c>),导致同一线程无法发 <c>stop</c> 中断搜索;
/// 它的 <c>m_command</c>/<c>m_expected</c> 是共享可变字段,非线程安全;
/// 且每次发命令都会重复订阅一次输出事件。这里全部改成异步 + Channel 管道。
/// </summary>
public sealed class UciEngineClient : IAsyncDisposable
{
    private readonly Channel<string> _stdout = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Queue<string> _stderrTail = new();
    private readonly Queue<string> _stdoutTail = new();
    private readonly object _stderrLock = new();
    private readonly object _stdoutLock = new();

    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _stdoutPump;
    private Task? _stderrPump;
    private bool _disposed;

    public string? EngineName { get; private set; }
    public string? Author { get; private set; }

    /// <summary>握手时引擎报告的选项:名称 → 默认值。</summary>
    public Dictionary<string, string> Options { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRunning => _process is { HasExited: false };

    public Task StartAsync(string enginePath, CancellationToken ct = default)
        => StartAsync(enginePath, arguments: null, ct);

    /// <summary>启动引擎进程。<paramref name="arguments"/> 为传给引擎的命令行参数(可选)。</summary>
    public Task StartAsync(string enginePath, string? arguments, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(enginePath);

        if (!File.Exists(enginePath))
        {
            throw new FileNotFoundException("引擎文件不存在。", enginePath);
        }

        var psi = new ProcessStartInfo
        {
            FileName = enginePath,          // 直接赋绝对路径,不经 shell,空格/中文路径都安全
            Arguments = arguments ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Start();

        _stdin = _process.StandardInput;
        _stdin.AutoFlush = true;

        _stdoutPump = Task.Run(() => PumpAsync(_process!.StandardOutput, _stdout));
        _stderrPump = Task.Run(() => PumpAsync(_process!.StandardError, null));

        return Task.CompletedTask;
    }

    private async Task PumpAsync(StreamReader reader, Channel<string>? channel)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (channel is not null)
                {
                    RecordStdout(line);
                    await channel.Writer.WriteAsync(line);
                }
                else
                {
                    RecordStderr(line);
                }
            }
        }
        catch
        {
            // 进程结束时读取会抛异常,忽略即可
        }
        finally
        {
            channel?.Writer.TryComplete();
        }
    }

    public async Task HandshakeAsync(CancellationToken ct = default)
    {
        await WriteLineAsync("uci", ct);

        while (true)
        {
            var line = await ReadLineAsync(ct);
            if (line is null)
            {
                throw new EngineCrashedException("引擎在握手阶段退出。");
            }

            if (line.Equals("uciok", StringComparison.Ordinal))
            {
                break;
            }

            if (line.StartsWith("id name ", StringComparison.Ordinal))
            {
                EngineName = line["id name ".Length..].Trim();
            }
            else if (line.StartsWith("id author ", StringComparison.Ordinal))
            {
                Author = line["id author ".Length..].Trim();
            }
            else if (line.StartsWith("option name ", StringComparison.Ordinal))
            {
                ParseOption(line);
            }
        }

        await WriteLineAsync("isready", ct);
        while (true)
        {
            var line = await ReadLineAsync(ct);
            if (line is null)
            {
                throw new EngineCrashedException("引擎在 isready 阶段退出。");
            }

            if (line.Equals("readyok", StringComparison.Ordinal))
            {
                break;
            }
        }
    }

    private void ParseOption(string line)
    {
        // option name <Name> type <t> default <v> ...
        const string prefix = "option name ";
        var rest = line[prefix.Length..];
        var typeIdx = rest.IndexOf(" type ", StringComparison.Ordinal);
        if (typeIdx < 0)
        {
            Options[rest.Trim()] = string.Empty;
            return;
        }

        var name = rest[..typeIdx].Trim();
        var tail = rest[(typeIdx + 6)..];
        var defaultIdx = tail.IndexOf(" default ", StringComparison.Ordinal);
        string value = string.Empty;
        if (defaultIdx >= 0)
        {
            var after = tail[(defaultIdx + 9)..].Trim();
            var space = after.IndexOf(' ');
            value = space > 0 ? after[..space] : after;
        }

        Options[name] = value;
    }

    public async Task SetOptionAsync(string name, string value, CancellationToken ct = default)
        => await WriteLineAsync($"setoption name {name} value {value}", ct);

    public async Task<UciSearchResult> SearchAsync(
        string fen,
        string? moves,
        UciSearchOptions options,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fen);

        var position = string.IsNullOrWhiteSpace(moves)
            ? $"position fen {fen}"
            : $"position fen {fen} moves {moves}";

        await WriteLineAsync(position, ct);
        await WriteLineAsync(options.BuildGoCommand(), ct);

        var lines = new Dictionary<int, UciInfo>();
        string bestMove = string.Empty;

        using var deadline = new CancellationTokenSource(options.TimeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);

        while (true)
        {
            string? line;
            try
            {
                line = await _stdout.Reader.ReadAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                // 被取消或超时:发 stop,尽量把引擎收干净
                await TrySendAsync("stop");
                await DrainAsync(TimeSpan.FromSeconds(3));

                if (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }

                throw new TimeoutException($"引擎搜索超过 {options.TimeoutMs} 毫秒未返回。" + DescribeState());
            }

            if (line is null)
            {
                throw new EngineCrashedException(
                    "引擎进程已退出。" + Environment.NewLine + GetStderrTail());            }

            if (line.StartsWith("info ", StringComparison.Ordinal))
            {
                var info = ParseInfo(line);
                if (info is not null)
                {
                    lines[info.MultiPv] = info;
                }

                continue;
            }

            if (line.StartsWith("bestmove", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    bestMove = parts[1];
                }

                break;
            }

            if (line.Contains("CRITICAL ERROR", StringComparison.OrdinalIgnoreCase))
            {
                throw new EngineCrashedException(line);
            }
        }

        var ordered = lines.Values.OrderBy(l => l.MultiPv).ToList();
        var primary = ordered.FirstOrDefault(l => l.MultiPv == 1) ?? ordered.FirstOrDefault();

        return new UciSearchResult
        {
            Depth = primary?.Depth ?? 0,
            ScoreCp = primary?.ScoreCp,
            ScoreMate = primary?.ScoreMate,
            Pv = primary?.Pv ?? Array.Empty<string>(),
            Lines = ordered,
            Nodes = primary?.Nodes ?? 0,
            Nps = primary?.Nps ?? 0,
            TimeMs = primary?.TimeMs ?? 0,
            BestMove = bestMove,
        };
    }

    /// <summary>解析一行 <c>info</c>。</summary>
    public static UciInfo? ParseInfo(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        var info = new UciInfo();
        var seenScore = false;

        for (int i = 1; i < parts.Length; i++)
        {
            switch (parts[i])
            {
                case "depth":
                    if (++i < parts.Length && int.TryParse(parts[i], out var d)) info.Depth = d;
                    break;
                case "seldepth":
                    if (++i < parts.Length && int.TryParse(parts[i], out var sd)) info.SelDepth = sd;
                    break;
                case "multipv":
                    if (++i < parts.Length && int.TryParse(parts[i], out var mp)) info.MultiPv = mp;
                    break;
                case "score":
                    if (i + 2 < parts.Length)
                    {
                        var kind = parts[i + 1];
                        if (int.TryParse(parts[i + 2], out var val))
                        {
                            if (kind == "cp") info.ScoreCp = val;
                            else if (kind == "mate") info.ScoreMate = val;
                            seenScore = true;
                        }

                        i += 2;
                    }

                    break;
                case "nodes":
                    if (++i < parts.Length && long.TryParse(parts[i], out var n)) info.Nodes = n;
                    break;
                case "nps":
                    if (++i < parts.Length && int.TryParse(parts[i], out var nps)) info.Nps = nps;
                    break;
                case "time":
                    if (++i < parts.Length && int.TryParse(parts[i], out var t)) info.TimeMs = t;
                    break;
                case "hashfull":
                    if (++i < parts.Length && int.TryParse(parts[i], out _)) { }
                    break;
                case "currmove":
                case "currmovenumber":
                    if (++i < parts.Length) { }
                    break;
                case "pv":
                {
                    var pv = new List<string>();
                    for (int j = i + 1; j < parts.Length; j++)
                    {
                        pv.Add(parts[j]);
                    }

                    info.Pv = pv;
                    i = parts.Length;
                    break;
                }
                case "string":
                    i = parts.Length;
                    break;
                default:
                    // 未知键:跳过它和可能跟着的一个值
                    if (i + 1 < parts.Length && parts[i + 1].Length > 0 && !char.IsLetter(parts[i + 1][0]))
                    {
                        i++;
                    }

                    break;
            }
        }

        return seenScore || info.Pv.Count > 0 || info.Depth > 0 ? info : null;
    }

    /// <summary>请求引擎停止当前搜索(会等到 bestmove 回来)。</summary>
    public async Task StopAsync()
    {
        await TrySendAsync("stop");
        await DrainAsync(TimeSpan.FromSeconds(5));
    }

    private async Task DrainAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (await _stdout.Reader.ReadAsync(cts.Token) is { } line)
            {
                if (line.StartsWith("bestmove", StringComparison.Ordinal))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 收尾超时,忽略
        }
    }

    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        try
        {
            return await _stdout.Reader.ReadAsync(ct);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    private async Task WriteLineAsync(string line, CancellationToken ct)
    {
        var writer = _stdin ?? throw new EngineCrashedException("引擎进程未启动。");
        await _writeLock.WaitAsync(ct);
        try
        {
            await writer.WriteLineAsync(line.AsMemory(), ct);
        }
        catch (Exception ex)
        {
            throw new EngineCrashedException("向引擎写入命令失败: " + ex.Message);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task TrySendAsync(string line)
    {
        var writer = _stdin;
        if (writer is null)
        {
            return;
        }

        try
        {
            await _writeLock.WaitAsync();
            try
            {
                await writer.WriteLineAsync(line.AsMemory());
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch
        {
            // 引擎可能已经死了,忽略
        }
    }

    public string GetStderrTail()
    {
        lock (_stderrLock)
        {
            return _stderrTail.Count == 0 ? string.Empty : string.Join(" | ", _stderrTail);
        }
    }

    /// <summary>最近收到的若干行标准输出,用于诊断"引擎没回话"这类问题。</summary>
    public string GetStdoutTail()
    {
        lock (_stdoutLock)
        {
            return _stdoutTail.Count == 0 ? string.Empty : string.Join(" | ", _stdoutTail);
        }
    }

    private void RecordStdout(string line)
    {
        lock (_stdoutLock)
        {
            _stdoutTail.Enqueue(line);
            while (_stdoutTail.Count > 10)
            {
                _stdoutTail.Dequeue();
            }
        }
    }

    /// <summary>描述引擎当前状态,拼进异常信息里便于排查。</summary>
    private string DescribeState()
    {
        var state = _process is null
            ? "引擎进程未启动。"
            : _process.HasExited
                ? $"引擎进程已退出(退出码 {_process.ExitCode})。"
                : "引擎进程仍在运行。";

        var outTail = GetStdoutTail();
        var errTail = GetStderrTail();
        return state
            + (outTail.Length > 0 ? " 最近输出: " + outTail : string.Empty)
            + (errTail.Length > 0 ? " 最近错误: " + errTail : string.Empty);
    }

    private void RecordStderr(string line)
    {
        lock (_stderrLock)
        {
            _stderrTail.Enqueue(line);
            while (_stderrTail.Count > 10)
            {
                _stderrTail.Dequeue();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_process is { HasExited: false })
            {
                await TrySendAsync("quit");
                using var cts = new CancellationTokenSource(5000);
                try
                {
                    await _process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // 超时则强杀,绝不留孤儿进程
                }
            }
        }
        catch
        {
            // 忽略
        }
        finally
        {
            if (_process is { HasExited: false })
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // 忽略
                }
            }

            _process?.Dispose();
            _process = null;
            _stdout.Writer.TryComplete();
        }
    }
}
