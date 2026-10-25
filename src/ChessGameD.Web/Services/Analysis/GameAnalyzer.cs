using System.Collections.Concurrent;
using System.Text.Json;
using ChessDotNetCore;
using ChessGameD.Web.Data;
using ChessGameD.Web.Services.Engine;
using Microsoft.EntityFrameworkCore;
using DataAnalysis = ChessGameD.Web.Data.Analysis;

namespace ChessGameD.Web.Services.Analysis;

public sealed class AnalysisJobStatus
{
    public string State { get; set; } = "idle";
    public int Done { get; set; }
    public int Total { get; set; }
    public int Percent { get; set; }
    public int CurrentPly { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed record AnalysisPoint(int Ply, int? ScoreCp, int? ScoreMate, int Depth);

public sealed record MoveClassification(
    int Ply,
    string San,
    MoveClass Class,
    int LossCp,
    bool IsEngineTopMove,
    bool InTopCandidates,
    string? BestMoveUci = null,
    string? BestMoveSan = null,
    int? EvalBeforeCp = null,
    int? EvalAfterCp = null);

public sealed record AnalysisSummary(
    int GameId,
    string? EngineVersion,
    IReadOnlyList<AnalysisPoint> Points,
    IReadOnlyList<MoveClassification> Classifications);

/// <summary>
/// 整局分析编排:找出缺失的局面 → 逐个搜索 → 立刻落库 → 计算分类。
/// </summary>
public sealed class GameAnalyzer
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly EngineManager _engine;
    private readonly SettingsStore _settings;
    private readonly ILogger<GameAnalyzer> _logger;
    private readonly ConcurrentDictionary<int, Job> _jobs = new();

    public GameAnalyzer(
        IDbContextFactory<AppDbContext> factory,
        EngineManager engine,
        SettingsStore settings,
        ILogger<GameAnalyzer> logger)
    {
        _factory = factory;
        _engine = engine;
        _settings = settings;
        _logger = logger;
    }

    public AnalysisJobStatus GetStatus(int gameId)
        => _jobs.TryGetValue(gameId, out var job) ? job.Status : new AnalysisJobStatus();

    public Task<AnalysisJobStatus> StartAsync(int gameId, bool force, CancellationToken ct = default)
    {
        if (_jobs.TryGetValue(gameId, out var existing))
        {
            if (existing.Status.State == "running")
            {
                return Task.FromResult(existing.Status);
            }

            existing.Cts.Dispose();
        }

        var job = new Job();
        _jobs[gameId] = job;

        job.Status = new AnalysisJobStatus { State = "running", StartedAt = DateTimeOffset.Now };

        _ = Task.Run(async () =>
        {
            try
            {
                await RunAsync(gameId, force, job, job.Cts.Token);
                job.Status.State = "done";
            }
            catch (OperationCanceledException)
            {
                job.Status.State = "cancelled";
            }
            catch (EngineNotInstalledException ex)
            {
                job.Status.State = "error";
                job.Status.Error = ex.Message;
            }
            catch (EngineBusyException ex)
            {
                job.Status.State = "error";
                job.Status.Error = ex.Message;
            }
            catch (Exception ex)
            {
                job.Status.State = "error";
                job.Status.Error = ex.Message;
                _logger.LogError(ex, "整局分析失败 (game {GameId})", gameId);
            }
            finally
            {
                job.Status.FinishedAt = DateTimeOffset.Now;
                job.Status.Percent = job.Status.State == "done" ? 100 : job.Status.Percent;
            }
        }, CancellationToken.None);

        return Task.FromResult(job.Status);
    }

    public void Stop(int gameId)
    {
        if (_jobs.TryGetValue(gameId, out var job) && job.Status.State == "running")
        {
            job.Cts.Cancel();
        }
    }

    private async Task RunAsync(int gameId, bool force, Job job, CancellationToken ct)
    {
        var status = await _engine.GetStatusAsync(ct);
        if (!status.IsInstalled)
        {
            throw new EngineNotInstalledException(
                "尚未安装 Stockfish 引擎,请先到设置页点击「一键安装 Stockfish」。");
        }

        var engineVersion = status.Version ?? "unknown";

        await using var db = await _factory.CreateDbContextAsync(ct);
        var game = await db.Games.Include(g => g.Moves).FirstOrDefaultAsync(g => g.Id == gameId, ct);
        if (game is null)
        {
            throw new InvalidOperationException($"棋局 {gameId} 不存在。");
        }

        var moves = game.Moves.OrderBy(m => m.Ply).ToList();
        var fens = new List<(int Ply, string Fen)>();
        var initialFen = game.InitialFen ?? "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        fens.Add((0, initialFen));
        foreach (var m in moves)
        {
            fens.Add((m.Ply, m.Fen));
        }

        var existing = await db.Analyses
            .Where(a => a.GameId == gameId && a.EngineVersion == engineVersion)
            .Select(a => a.Ply)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        var pending = force
            ? fens
            : fens.Where(f => !existingSet.Contains(f.Ply)).ToList();

        if (force)
        {
            var stale = await db.Analyses.Where(a => a.GameId == gameId).ToListAsync(ct);
            db.Analyses.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
        }

        job.Status.Total = fens.Count;
        job.Status.Done = fens.Count - pending.Count;

        if (pending.Count == 0)
        {
            job.Status.Percent = 100;
            return;
        }

        var depth = await _settings.GetIntAsync(SettingsStore.EngineAnalysisDepth, 16, ct);
        var multiPv = await _settings.GetIntAsync(SettingsStore.EngineMultiPv, 3, ct);

        var options = new UciSearchOptions
        {
            Depth = depth,
            MultiPv = Math.Clamp(multiPv, 1, 8),
            TimeoutMs = 180_000,
        };

        await _engine.AnalyzeBatchAsync(
            pending,
            options,
            async (result) =>
            {
                await SaveAnalysisAsync(gameId, engineVersion, result.Ply, result.Analysis, ct);
                job.Status.Done++;
                job.Status.CurrentPly = result.Ply;
                job.Status.Percent = job.Status.Total == 0
                    ? 100
                    : (int)(100.0 * job.Status.Done / job.Status.Total);
            },
            null,
            ct);
    }

    private async Task SaveAnalysisAsync(
        int gameId,
        string engineVersion,
        int ply,
        EngineAnalysis analysis,
        CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Analyses.FirstOrDefaultAsync(
            a => a.GameId == gameId && a.Ply == ply && a.EngineVersion == engineVersion, ct);

        if (row is null)
        {
            row = new DataAnalysis { GameId = gameId, Ply = ply, EngineVersion = engineVersion };
            db.Analyses.Add(row);
        }

        row.Depth = analysis.Depth;
        row.ScoreCp = analysis.ScoreCp;
        row.ScoreMate = analysis.ScoreMate;
        row.Pv = string.Join(' ', analysis.Pv);
        row.MultiPvJson = JsonSerializer.Serialize(analysis.Lines);
        row.Nodes = analysis.Nodes;
        row.Nps = analysis.Nps;
        row.TimeMs = analysis.TimeMs;
        row.CreatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>读取整局分析结果(评估曲线 + 走子分类)。</summary>
    public async Task<AnalysisSummary?> GetResultAsync(int gameId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var game = await db.Games.Include(g => g.Moves).FirstOrDefaultAsync(g => g.Id == gameId, ct);
        if (game is null)
        {
            return null;
        }

        var analyses = await db.Analyses.Where(a => a.GameId == gameId).ToListAsync(ct);
        if (analyses.Count == 0)
        {
            return null;
        }

        // 优先使用当前引擎版本的结果,否则用条目最多的那一版
        var status = await _engine.GetStatusAsync(ct);
        var preferred = status.Version;
        string version;
        if (preferred is not null && analyses.Any(a => a.EngineVersion == preferred))
        {
            version = preferred;
        }
        else
        {
            version = analyses.GroupBy(a => a.EngineVersion)
                .OrderByDescending(g => g.Count())
                .First().Key;
        }

        var rows = analyses.Where(a => a.EngineVersion == version).ToDictionary(a => a.Ply);
        var moves = game.Moves.OrderBy(m => m.Ply).ToList();

        var points = new List<AnalysisPoint>();
        foreach (var m in moves)
        {
            if (rows.TryGetValue(m.Ply, out var a))
            {
                points.Add(new AnalysisPoint(m.Ply, a.ScoreCp, a.ScoreMate, a.Depth));
            }
        }

        var classifications = new List<MoveClassification>();
        var fenByPly = moves.ToDictionary(m => m.Ply, m => m.Fen);
        for (int i = 0; i < moves.Count; i++)
        {
            var move = moves[i];
            var beforePly = move.Ply - 1;
            if (!rows.TryGetValue(beforePly, out var before) || !rows.TryGetValue(move.Ply, out var after))
            {
                continue;
            }

            var loss = ComputeLoss(before, after, move.Ply);
            var playedUci = move.Uci;

            var topMove = FirstPvMove(before.Pv);
            var isTop = topMove is not null && playedUci is not null
                        && topMove.Equals(playedUci, StringComparison.OrdinalIgnoreCase);

            var inTop = false;
            if (playedUci is not null)
            {
                inTop = DeserializeLines(before.MultiPvJson)
                    .Any(l => FirstMove(l.Pv) is { } first
                             && first.Equals(playedUci, StringComparison.OrdinalIgnoreCase));
            }

            // 引擎在这个局面推荐的着法(走子前的 PV 首着),转成 SAN 方便直接展示
            string? bestSan = null;
            if (topMove is not null && fenByPly.TryGetValue(beforePly, out var beforeFen))
            {
                bestSan = SanConverter.ToSan(beforeFen, topMove);
            }

            // 走子方视角的前后分值(正数对走子方有利),用于「漏着」判定
            var sign = move.Ply % 2 == 1 ? 1 : -1;
            var evalBefore = sign * EvalToCp(before.ScoreCp, before.ScoreMate);
            var evalAfter = sign * EvalToCp(after.ScoreCp, after.ScoreMate);
            var missedWin = MoveClassifier.IsMissedWin(evalBefore, evalAfter);

            var cls = MoveClassifier.Classify(loss, isTop, inTop, missedWin);
            classifications.Add(new MoveClassification(
                move.Ply, move.San, cls, loss, isTop, inTop, topMove, bestSan, evalBefore, evalAfter));
        }

        return new AnalysisSummary(gameId, version, points, classifications);
    }

    /// <summary>
    /// 计算这一步的损失(相对走子方)。
    ///
    /// 库里存的是<b>相对白方</b>的分值。第 m 个半回合(1 起):
    /// m 为奇数时是白方走,分数差不用变号;m 为偶数时是黑方走,需要反号。
    /// </summary>
    public static int ComputeLoss(DataAnalysis before, DataAnalysis after, int movePly)
    {
        var vBefore = EvalToCp(before.ScoreCp, before.ScoreMate);
        var vAfter = EvalToCp(after.ScoreCp, after.ScoreMate);
        var raw = vBefore - vAfter;
        return movePly % 2 == 1 ? raw : -raw;
    }

    /// <summary>杀棋分值折算成厘兵:mate in 1 ≈ 10000,每多一步折 100。</summary>
    public static int EvalToCp(int? cp, int? mate)
    {
        if (cp.HasValue)
        {
            return cp.Value;
        }

        if (mate.HasValue)
        {
            var m = mate.Value;
            var magnitude = Math.Abs(m) <= 0 ? 1 : Math.Abs(m);
            var value = 10000 - (magnitude - 1) * 100;
            return m >= 0 ? value : -value;
        }

        return 0;
    }

    private static string? FirstPvMove(string pv)
    {
        if (string.IsNullOrWhiteSpace(pv))
        {
            return null;
        }

        var parts = pv.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : null;
    }

    private static string? FirstMove(IReadOnlyList<string> pv)
        => pv is { Count: > 0 } ? pv[0] : null;

    private static IReadOnlyList<MultiPvLine> DeserializeLines(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<MultiPvLine>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<MultiPvLine>>(json) ?? new List<MultiPvLine>();
        }
        catch (JsonException)
        {
            return Array.Empty<MultiPvLine>();
        }
    }

    private sealed class Job
    {
        public CancellationTokenSource Cts { get; } = new();
        public AnalysisJobStatus Status { get; set; } = new();
    }
}
