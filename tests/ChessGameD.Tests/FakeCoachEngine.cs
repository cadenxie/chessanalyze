using ChessDotNetCore;
using ChessGameD.Web.Data;
using ChessGameD.Web.Services.Engine;
using ChessGameD.Web.Services.Play;

namespace ChessGameD.Tests;

/// <summary>
/// 假的陪练引擎:按局面给出可控的评估与候选着法。
///
/// 走法用 ChessDotNetCore 现算(合法且确定),分值由测试脚本决定,
/// 这样「走一步 → 引擎应手 → 判定坏棋」整条链路都能在单元测试里跑。
/// </summary>
public sealed class FakeCoachEngine : ICoachEngine
{
    private readonly Func<string, int?> _scoreFor;
    private readonly List<string> _requestedFens = new();

    /// <summary>默认给 0 分。</summary>
    public FakeCoachEngine()
        : this(_ => 0)
    {
    }

    /// <summary>按局面给分(返回 null 表示没有评估)。</summary>
    public FakeCoachEngine(Func<string, int?> scoreFor)
    {
        _scoreFor = scoreFor;
    }

    /// <summary>引擎被问过哪些局面。</summary>
    public IReadOnlyList<string> RequestedFens => _requestedFens;

    public Task<EngineAnalysis> AnalyzeAsync(string fen, CancellationToken ct = default)
    {
        _requestedFens.Add(fen);

        var score = _scoreFor(fen);
        var moves = LegalMoves(fen);
        var best = moves.FirstOrDefault() ?? string.Empty;
        var lines = new List<MultiPvLine>();

        if (!string.IsNullOrEmpty(best))
        {
            lines.Add(new MultiPvLine(1, score, null, new[] { best }));
            foreach (var extra in moves.Skip(1).Take(2))
            {
                lines.Add(new MultiPvLine(lines.Count + 1, score, null, new[] { extra }));
            }
        }

        return Task.FromResult(new EngineAnalysis(
            score,
            null,
            12,
            1000,
            1000,
            10,
            string.IsNullOrEmpty(best) ? Array.Empty<string>() : new[] { best },
            lines,
            best));
    }

    /// <summary>局面里的合法走法(UCI)。</summary>
    public static List<string> LegalMoves(string fen)
    {
        var result = new List<string>();
        var game = new ChessGame();
        game.Initialize(fen);

        foreach (var move in game.GetValidMoves(game.CurrentPlayer))
        {
            var uci = $"{move.OriginalPosition}{move.NewPosition}";
            if (move.Promotion is not null)
            {
                uci += char.ToLowerInvariant(move.Promotion.Value);
            }

            result.Add(uci.ToLowerInvariant());
        }

        return result;
    }
}
