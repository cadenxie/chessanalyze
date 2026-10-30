using ChessGameD.Web.Data;
using ChessGameD.Web.Services.Analysis;
using ChessGameD.Web.Services.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChessGameD.Tests;

/// <summary>
/// 「最佳续着」:引擎在这个局面上的主线,用来在坏棋下面直接展示"应该怎么走"。
/// </summary>
public class BestLineTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    [Fact]
    public void BuildBestLine_把Uci主变转成San()
    {
        var (uci, san) = GameAnalyzer.BuildBestLine(StartFen, "e2e4 e7e5 g1f3");

        Assert.Equal(new[] { "e2e4", "e7e5", "g1f3" }, uci);
        Assert.Equal(new[] { "e4", "e5", "Nf3" }, san);
    }

    [Fact]
    public void BuildBestLine_最多取BestLineLength手()
    {
        var pv = "e2e4 e7e5 g1f3 b8c6 f1c4 f8c5";

        var (uci, san) = GameAnalyzer.BuildBestLine(StartFen, pv);

        Assert.Equal(GameAnalyzer.BestLineLength, uci.Count);
        Assert.Equal(GameAnalyzer.BestLineLength, san.Count);
        Assert.Equal("e4", san[0]);
    }

    [Fact]
    public void BuildBestLine_遇到非法着法就停下且两边对齐()
    {
        // e2e5 白兵走不到,后面几步也不再转换
        var (uci, san) = GameAnalyzer.BuildBestLine(StartFen, "e2e4 e2e5 g1f3");

        Assert.Equal(new[] { "e4" }, san);
        Assert.Equal(uci.Count, san.Count);
    }

    [Fact]
    public void BuildBestLine_空输入返回空()
    {
        Assert.Empty(GameAnalyzer.BuildBestLine(StartFen, null).San);
        Assert.Empty(GameAnalyzer.BuildBestLine(StartFen, "").San);
        Assert.Empty(GameAnalyzer.BuildBestLine(null, "e2e4").San);
        Assert.Empty(GameAnalyzer.BuildBestLine("这不是 FEN", "e2e4").San);
    }

    /// <summary>整局分析结果里,每个着法都要带上引擎在这个局面上的主线。</summary>
    [Fact]
    public async Task GetResultAsync_分类里带上最佳续着()
    {
        await using var db = new TestDatabase();

        db.Context.ImportBatches.Add(new ImportBatch { FileName = "test.pgn", TotalGames = 1 });
        db.Context.SaveChanges();
        var batchId = db.Context.ImportBatches.Select(b => b.Id).First();

        var game = new Game
        {
            White = "白棋手",
            Black = "黑棋手",
            Result = "1-0",
            PlyCount = 2,
            OwnerUserId = TestDatabase.TestUserId,
            ImportedAt = DateTime.UtcNow,
            ImportBatchId = batchId,
            Moves = new List<GameMove>
            {
                new() { Ply = 0, San = "", Fen = StartFen },
                new() { Ply = 1, San = "e4", Uci = "e2e4", Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1" },
                new() { Ply = 2, San = "e5", Uci = "e7e5", Fen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2" },
            },
        };
        db.Context.Games.Add(game);
        db.Context.SaveChanges();

        // 引擎在开局局面推荐的是 d4,黑方推荐的是 c5
        db.Context.Analyses.AddRange(
            new Analysis { GameId = game.Id, Ply = 0, EngineVersion = "test", ScoreCp = 20, Depth = 12, Pv = "d2d4 d7d5" },
            new Analysis { GameId = game.Id, Ply = 1, EngineVersion = "test", ScoreCp = 30, Depth = 12, Pv = "e7e5 e1e2" },
            new Analysis { GameId = game.Id, Ply = 2, EngineVersion = "test", ScoreCp = 25, Depth = 12, Pv = "c7c5 g1f3" });
        db.Context.SaveChanges();

        var analyzer = new GameAnalyzer(
            db.Factory,
            new EngineManager(db.Settings, NullLogger<EngineManager>.Instance, EnginePlatform.Detect()),
            db.Settings,
            NullLogger<GameAnalyzer>.Instance);

        var summary = await analyzer.GetResultAsync(game.Id);
        Assert.NotNull(summary);

        var first = summary!.Classifications.Single(c => c.Ply == 1);
        Assert.Equal("d4", first.BestMoveSan);
        Assert.Equal(new[] { "d4", "d5" }, first.BestLineSan);
        Assert.Equal(new[] { "d2d4", "d7d5" }, first.BestLineUci);

        // 第一手也要有推荐(ply 0 的局面)
        Assert.NotNull(first.BestMoveUci);
    }
}
