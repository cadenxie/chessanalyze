using ChessDotNetCore;
using ChessDotNetCore.PgnParsing;
using ChessGameD.Web.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace ChessGameD.Web.Services.Pgn;

public sealed class ImportResult
{
    public int BatchId { get; set; }
    public int TotalGames { get; set; }
    public int ImportedGames { get; set; }
    public int SkippedGames { get; set; }
    public List<string> Errors { get; } = new();
}

/// <summary>
/// PGN 导入:清洗 → 解析 → 落库。
///
/// 注意:<see cref="PgnParser"/> 内部使用了静态可变状态(取消标志、深度上限),
/// 因此所有导入必须串行执行,这里用一把静态锁保证。
/// </summary>
public sealed class PgnImportService
{
    private static readonly SemaphoreSlim ImportLock = new(1, 1);

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<PgnImportService> _logger;

    public PgnImportService(IDbContextFactory<AppDbContext> factory, ILogger<PgnImportService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<ImportResult> ImportAsync(string rawText, string fileName, CancellationToken ct = default)
    {
        var result = new ImportResult();

        var text = PgnSanitizer.StripBom(PgnSanitizer.Sanitize(rawText ?? string.Empty));
        var gameTexts = PgnSanitizer.SplitGames(text);
        result.TotalGames = gameTexts.Count;

        // 先建批次,后续落库直接挂到这个批次上(避免事后回填产生竞态)
        result.BatchId = await CreateBatchAsync(fileName, ct);

        if (gameTexts.Count == 0)
        {
            result.Errors.Add("没有解析到任何棋局(可能不是 PGN 格式,或文件为空)。");
            await UpdateBatchAsync(result, ct);
            return result;
        }

        await ImportLock.WaitAsync(ct);
        try
        {
            for (int i = 0; i < gameTexts.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await ImportSingleGameAsync(gameTexts[i], i + 1, result, ct);
            }
        }
        finally
        {
            ImportLock.Release();
        }

        await UpdateBatchAsync(result, ct);
        return result;
    }

    private async Task ImportSingleGameAsync(string gameText, int gameIndex, ImportResult result, CancellationToken ct)
    {
        PgnGame? pgnGame = null;
        string? errTxt = null;
        try
        {
            var parser = new PgnParser(isDiagnoseOn: false);
            parser.InitFromString(PgnSanitizer.EnsureEventTag(gameText));
            parser.ParseSingle(
                noMoveList: false,
                out int skipCount,
                out int truncatedCount,
                out pgnGame,
                out errTxt);
        }
        catch (Exception ex)
        {
            result.SkippedGames++;
            result.Errors.Add($"第 {gameIndex} 局:解析异常 —— {ex.Message}");
            _logger.LogWarning(ex, "PGN 单局解析失败 (第 {GameIndex} 局)", gameIndex);
            return;
        }

        var parsedSans = pgnGame?.PgnMoves?.Select(m => m.SanMove).ToList() ?? new List<string?>();

        if (pgnGame is null || pgnGame.PgnMoves is null || pgnGame.PgnMoves.Count == 0)
        {
            result.SkippedGames++;
            result.Errors.Add(errTxt is { Length: > 0 }
                ? DescribeParseError(errTxt, gameIndex, parsedSans)
                : $"第 {gameIndex} 局:棋局为空或走法列表无法解析,已跳过。");
            return;
        }

        if (pgnGame.ChessMoves is null || pgnGame.ChessMoves.Count != pgnGame.PgnMoves.Count)
        {
            result.SkippedGames++;
            result.Errors.Add(DescribeParseError(
                errTxt ?? $"走法未能全部验证(可能是非法着法),共解析出 {pgnGame.PgnMoves.Count} 手。",
                gameIndex,
                parsedSans));
            return;
        }

        try
        {
            await PersistGameAsync(pgnGame, result, ct);
        }
        catch (Exception ex)
        {
            result.SkippedGames++;
            result.Errors.Add($"落库失败: {ex.Message}");
            _logger.LogWarning(ex, "PGN 单局落库失败");
        }
    }

    private async Task PersistGameAsync(PgnGame pgnGame, ImportResult result, CancellationToken ct)
    {
        var attrs = pgnGame.Attrs ?? new Dictionary<string, string>();
        var initialFen = GetAttr(attrs, "FEN");
        if (string.IsNullOrWhiteSpace(initialFen))
        {
            initialFen = string.IsNullOrWhiteSpace(pgnGame.Fen) ? null : pgnGame.Fen;
        }

        // 复现走法,得到每一步的 FEN 与 UCI 走法
        var replay = new ChessGame();
        if (string.IsNullOrWhiteSpace(initialFen))
        {
            replay.Initialize();
        }
        else
        {
            replay.Initialize(initialFen);
        }

        var startPlayer = replay.StartPlayer;

        var fens = new List<string> { replay.GetFen() };
        var ucis = new List<string?> { null };

        for (int i = 0; i < pgnGame.ChessMoves!.Count; i++)
        {
            var move = pgnGame.ChessMoves[i];
            replay.MakeMove(move, alreadyValidated: true);
            fens.Add(replay.GetFen());
            ucis.Add(ToUci(move));
        }

        var game = new Game
        {
            Event = GetAttr(attrs, "Event"),
            Site = GetAttr(attrs, "Site"),
            Round = GetAttr(attrs, "Round"),
            Date = GetAttr(attrs, "Date"),
            Termination = GetAttr(attrs, "Termination"),
            TimeControl = GetAttr(attrs, "TimeControl"),
            Eco = GetAttr(attrs, "ECO"),
            Opening = GetAttr(attrs, "Opening"),
            Variation = GetAttr(attrs, "Variation"),
            White = GetAttr(attrs, "White") ?? pgnGame.WhitePlayerName ?? "?",
            Black = GetAttr(attrs, "Black") ?? pgnGame.BlackPlayerName ?? "?",
            WhiteElo = ToElo(pgnGame.WhiteElo) ?? ToElo(GetAttr(attrs, "WhiteElo")),
            BlackElo = ToElo(pgnGame.BlackElo) ?? ToElo(GetAttr(attrs, "BlackElo")),
            Result = GetAttr(attrs, "Result") ?? (pgnGame.GameResult ?? "*"),
            InitialFen = initialFen,
            PlyCount = pgnGame.PgnMoves!.Count,
            ImportedAt = DateTime.UtcNow,
            ImportBatchId = result.BatchId,
        };

        if (string.IsNullOrWhiteSpace(game.Result))
        {
            game.Result = "*";
        }

        // ply 0 = 初始局面
        game.Moves.Add(new GameMove
        {
            Ply = 0,
            San = string.Empty,
            Uci = null,
            Fen = fens[0],
            Comment = null,
            Nag = 0,
        });

        for (int i = 0; i < pgnGame.PgnMoves.Count; i++)
        {
            var pm = pgnGame.PgnMoves[i];
            game.Moves.Add(new GameMove
            {
                Ply = i + 1,
                San = pm.SanMove ?? string.Empty,
                Uci = ucis[i + 1],
                Fen = fens[i + 1],
                Comment = string.IsNullOrWhiteSpace(pm.Comment) ? null : pm.Comment,
                Nag = pm.NAG,
            });
        }

        game.PgnText = BuildPgnText(pgnGame, startPlayer, game.Result);

        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Games.Add(game);
        await db.SaveChangesAsync(ct);

        result.ImportedGames++;
    }

    private static string BuildPgnText(PgnGame pgnGame, Player startPlayer, string result)
    {
        var header = PgnUtil.GetPgnHeader(pgnGame.Attrs ?? new Dictionary<string, string>());
        var moves = PgnUtil.GetPgnMoves(pgnGame.PgnMoves!, startPlayer, incudeMoveAttrs: true);
        return header + "\r\n\r\n" + moves + " " + result + "\r\n";
    }

    private static string? ToUci(ChessDotNetCore.Move move)
    {
        var from = move.OriginalPosition.ToString();
        var to = move.NewPosition.ToString();
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            return null;
        }

        var promotion = move.Promotion.HasValue ? move.Promotion.Value.ToString().ToLowerInvariant() : string.Empty;
        return from + to + promotion;
    }

    private static int? ToElo(int elo) => elo > 0 ? elo : null;

    private static int? ToElo(string? raw)
        => int.TryParse(raw, out var value) && value > 0 ? value : null;

    private static string? GetAttr(Dictionary<string, string> attrs, string key)
    {
        foreach (var kv in attrs)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value;
            }
        }

        return null;
    }

    private async Task<int> CreateBatchAsync(string fileName, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var batch = new ImportBatch
        {
            FileName = fileName ?? "",
            ImportedAt = DateTime.UtcNow,
        };

        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        return batch.Id;
    }

    private async Task UpdateBatchAsync(ImportResult result, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var batch = await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == result.BatchId, ct);
        if (batch is null)
        {
            return;
        }

        batch.TotalGames = result.TotalGames;
        batch.ImportedGames = result.ImportedGames;
        batch.SkippedGames = result.SkippedGames;
        batch.ErrorSummary = result.Errors.Count == 0
            ? null
            : string.Join("\n", result.Errors.Take(20));
        await db.SaveChangesAsync(ct);
    }

    private static string Trim(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";

    // 上游 ChessDotNetCore 的错误格式:
    //   "PGN contains a bad move -  - <SAN> before Move <上一手>"
    //   "PGN contains a bad move - Invalid Move <token>"
    // 原样透出对用户没有意义,这里翻译成「第几局第几手出了什么问题」。
    private static readonly Regex BadMovePattern = new(
        @"^PGN contains a bad move\s*-\s*(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InvalidMovePattern = new(
        @"^Invalid Move\s+(?<token>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MoveDetailPattern = new(
        @"^-\s*(?<san>\S+)(?:\s+before\s+Move\s+(?<prev>.+?))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string DescribeParseError(string rawError, int gameIndex, IReadOnlyList<string?> parsedSans)
    {
        var line = rawError
            .Split('\n', '\r')
            .FirstOrDefault(l => l.Trim().Length > 0)?
            .Trim() ?? rawError.Trim();

        if (line.Length == 0)
        {
            return $"第 {gameIndex} 局:解析失败。";
        }

        var bad = BadMovePattern.Match(line);
        if (!bad.Success)
        {
            return $"第 {gameIndex} 局:{Trim(line, 200)}";
        }

        var rest = bad.Groups["rest"].Value.Trim();

        var invalid = InvalidMovePattern.Match(rest);
        if (invalid.Success)
        {
            var token = invalid.Groups["token"].Value.Trim();
            return token.Length == 0
                ? $"第 {gameIndex} 局:走法文本里有无法识别的符号,该局已跳过。"
                : $"第 {gameIndex} 局:走法文本里有无法识别的符号「{token}」,该局已跳过。";
        }

        var detail = MoveDetailPattern.Match(rest);
        if (detail.Success)
        {
            var san = detail.Groups["san"].Value;
            var prev = detail.Groups["prev"].Success ? detail.Groups["prev"].Value.Trim() : null;
            var (no, side) = LocateMove(san, parsedSans);
            var where = no > 0 ? $"第 {no} 回合{side}" : "该局";
            var prevText = string.IsNullOrEmpty(prev) ? "" : $"(上一手「{prev}」)";
            return $"第 {gameIndex} 局:{where}的「{san}」不符合规则{prevText},该局已跳过。";
        }

        return $"第 {gameIndex} 局:{Trim(rest, 200)}";
    }

    /// <summary>在已解析出的着法序列里定位某个着法,返回回合数与走子方。</summary>
    private static (int No, string Side) LocateMove(string san, IReadOnlyList<string?> parsedSans)
    {
        for (int i = 0; i < parsedSans.Count; i++)
        {
            if (!string.Equals(parsedSans[i], san, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ply = i + 1;
            return ((ply + 1) / 2, ply % 2 == 1 ? "白方" : "黑方");
        }

        return (0, "");
    }
}
