using ChessGameD.Web.Services.Auth;
using ChessGameD.Web.Services.Pgn;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChessGameD.Tests;

public class PgnImportTests
{
    /// <summary>测试里统一的归属(用户 1)。</summary>
    private static readonly GameOwner TestOwner = new(TestDatabase.TestUserId, null);

    private static PgnImportService CreateService(TestDatabase db)
        => new(db.Factory, NullLogger<PgnImportService>.Instance);

    [Fact]
    public async Task Import_SingleGame_StoresGameAndMoves()
    {
        await using var db = new TestDatabase();
        var service = CreateService(db);

        var result = await service.ImportAsync(TestPgn.SimpleGame, "simple.pgn", TestOwner);

        Assert.Equal(1, result.TotalGames);
        Assert.Equal(1, result.ImportedGames);
        Assert.Equal(0, result.SkippedGames);

        var game = await db.Context.Games.Include(g => g.Moves).SingleAsync();
        Assert.Equal("Alice", game.White);
        Assert.Equal("Bob", game.Black);
        Assert.Equal("1-0", game.Result);
        Assert.Equal("C42", game.Eco);
        Assert.Equal(1500, game.WhiteElo);
        Assert.Equal(1400, game.BlackElo);

        // ply 0 = 初始局面,共 6 个半回合 → 7 行
        var moves = game.Moves.OrderBy(m => m.Ply).ToList();
        Assert.Equal(7, moves.Count);
        Assert.Equal(0, moves[0].Ply);
        Assert.Equal(string.Empty, moves[0].San);
        Assert.Equal("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", moves[0].Fen);

        Assert.Equal(new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6" },
            moves.Skip(1).Select(m => m.San).ToArray());
        Assert.Equal("e2e4", moves[1].Uci);
        Assert.Equal("e7e5", moves[2].Uci);
    }

    [Fact]
    public async Task Import_ThreeGames_StoresAllThree()
    {
        await using var db = new TestDatabase();
        var service = CreateService(db);

        var result = await service.ImportAsync(TestPgn.ThreeGames, "three.pgn", TestOwner);

        Assert.Equal(3, result.TotalGames);
        Assert.Equal(3, result.ImportedGames);
        Assert.Equal(3, await db.Context.Games.CountAsync());
    }

    [Fact]
    public async Task Import_WithVariation_KeepsMainLineAndDropsVariationText()
    {
        await using var db = new TestDatabase();
        var service = CreateService(db);

        var result = await service.ImportAsync(TestPgn.WithVariation, "rav.pgn", TestOwner);

        Assert.Equal(1, result.ImportedGames);

        var game = await db.Context.Games.Include(g => g.Moves).SingleAsync();
        var sans = game.Moves.OrderBy(m => m.Ply).Skip(1).Select(m => m.San).ToList();

        // 主变完整保留
        Assert.Equal(new[] { "e4", "e5", "Nf3" }, sans);

        // 变着原文不能出现在注释里
        foreach (var move in game.Moves)
        {
            if (move.Comment is not null)
            {
                Assert.DoesNotContain("d4", move.Comment);
                Assert.DoesNotContain("Nf6", move.Comment);
            }
        }
    }

    [Fact]
    public async Task Import_IllegalMove_SkipsThatGameOnly()
    {
        await using var db = new TestDatabase();
        var service = CreateService(db);

        var combined = TestPgn.SimpleGame + "\n" + TestPgn.WithIllegalMove;
        var result = await service.ImportAsync(combined, "mixed.pgn", TestOwner);

        Assert.Equal(2, result.TotalGames);
        Assert.Equal(1, result.ImportedGames);
        Assert.Equal(1, result.SkippedGames);
        Assert.Single(await db.Context.Games.ToListAsync());
    }

    [Fact]
    public async Task Import_FromPosition_StoresInitialFen()
    {
        await using var db = new TestDatabase();
        var service = CreateService(db);

        var result = await service.ImportAsync(TestPgn.FromPosition, "fen.pgn", TestOwner);

        Assert.Equal(1, result.ImportedGames);

        var game = await db.Context.Games.Include(g => g.Moves).SingleAsync();
        Assert.NotNull(game.InitialFen);
        Assert.StartsWith("r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq", game.InitialFen!);

        var moves = game.Moves.OrderBy(m => m.Ply).ToList();
        Assert.Equal("Nf6", moves[1].San);
        Assert.Equal("g8f6", moves[1].Uci);
    }

    [Fact]
    public async Task Import_NonPgnText_ReportsNoImportedGames()
    {
        await using var db = new TestDatabase();
        var service = CreateService(db);

        var result = await service.ImportAsync("这不是 PGN", "junk.pgn", TestOwner);

        Assert.Equal(0, result.ImportedGames);
        Assert.Equal(1, result.SkippedGames);
        Assert.NotEmpty(result.Errors);
        Assert.Empty(await db.Context.Games.ToListAsync());
    }

    [Fact]
    public async Task Import_RecordsBatch()
    {
        await using var db = new TestDatabase();
        var service = CreateService(db);

        var result = await service.ImportAsync(TestPgn.ThreeGames, "three.pgn", TestOwner);

        var batch = await db.Context.ImportBatches.SingleAsync();
        Assert.Equal("three.pgn", batch.FileName);
        Assert.Equal(3, batch.TotalGames);
        Assert.Equal(3, batch.ImportedGames);
        Assert.Equal(result.BatchId, batch.Id);
    }

    [Fact]
    public async Task Import_GeneratesPlayablePgnText()
    {
        await using var db = new TestDatabase();
        var service = CreateService(db);

        await service.ImportAsync(TestPgn.SimpleGame, "simple.pgn", TestOwner);
        var game = await db.Context.Games.SingleAsync();

        Assert.Contains("[White \"Alice\"]", game.PgnText);
        Assert.Contains("e4", game.PgnText);
        Assert.Contains("1-0", game.PgnText);
    }

    [Fact]
    public async Task Import_WithCommaSeparatedMoves_Succeeds()
    {
        await using var db = new TestDatabase();
        var service = CreateService(db);

        var pgn = """
        [Event "Comma"]
        [White "Alice"]
        [Black "Bob"]
        [Result "1-0"]

        1. e4, e5 2. Nf3, Nc6 1-0
        """;

        var result = await service.ImportAsync(pgn, "comma.pgn", TestOwner);

        Assert.Equal(1, result.ImportedGames);
        Assert.Empty(result.Errors);

        var game = await db.Context.Games.Include(g => g.Moves).SingleAsync();
        var sans = game.Moves.OrderBy(m => m.Ply).Skip(1).Select(m => m.San).ToArray();
        Assert.Equal(new[] { "e4", "e5", "Nf3", "Nc6" }, sans);
    }

    [Fact]
    public async Task Import_IllegalMove_ReportsGameIndexAndMoveNumber()
    {
        await using var db = new TestDatabase();
        var service = CreateService(db);

        // 第 2 局里有一个非法着法(Qxh8 白后 h8 无人守护)
        var combined = TestPgn.SimpleGame + "\n" + TestPgn.WithIllegalMove;
        var result = await service.ImportAsync(combined, "mixed.pgn", TestOwner);

        Assert.Equal(1, result.SkippedGames);
        var error = Assert.Single(result.Errors);
        Assert.Contains("第 2 局", error);
        Assert.Contains("回合", error);
        Assert.Contains("Qxh8", error);
    }

    [Fact]
    public async Task Import_NonPgnText_ReportsGameIndex()
    {
        await using var db = new TestDatabase();
        var service = CreateService(db);

        var result = await service.ImportAsync("这不是 PGN", "junk.pgn", TestOwner);

        Assert.Single(result.Errors);
        Assert.Contains("第 1 局", result.Errors[0]);
    }
}
