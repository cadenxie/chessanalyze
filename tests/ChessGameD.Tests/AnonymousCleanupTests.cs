using ChessGameD.Web.Data;
using ChessGameD.Web.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChessGameD.Tests;

/// <summary>匿名数据的定时清理。</summary>
public sealed class AnonymousCleanupTests
{
    private static AnonymousCleanupService CreateService(TestDatabase db, int retentionHours = 24)
        => new(
            db.Factory,
            Options.Create(new AuthOptions { AnonymousRetentionHours = retentionHours }),
            NullLogger<AnonymousCleanupService>.Instance);

    /// <summary>造一局带走子与分析的匿名棋局。</summary>
    private static async Task<int> SeedAnonymousGameAsync(
        TestDatabase db,
        DateTime importedAt,
        string anonKey = "anon-key-1")
    {
        var batch = new ImportBatch
        {
            FileName = "test.pgn",
            TotalGames = 1,
            ImportedGames = 1,
            SkippedGames = 0,
            ImportedAt = importedAt,
        };
        db.Context.ImportBatches.Add(batch);
        await db.Context.SaveChangesAsync();

        var game = new Game
        {
            White = "Alice",
            Black = "Bob",
            PgnText = TestPgn.SimpleGame,
            ImportedAt = importedAt,
            ImportBatchId = batch.Id,
            OwnerAnonKey = anonKey,
        };
        db.Context.Games.Add(game);
        await db.Context.SaveChangesAsync();

        db.Context.Moves.Add(new GameMove
        {
            GameId = game.Id,
            Ply = 1,
            San = "e4",
            Uci = "e2e4",
            Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1",
        });
        db.Context.Analyses.Add(new Analysis
        {
            GameId = game.Id,
            Ply = 1,
            EngineVersion = "test",
            Depth = 10,
            ScoreCp = 20,
            Pv = "e2e4",
            CreatedAt = importedAt,
        });
        await db.Context.SaveChangesAsync();

        return game.Id;
    }

    [Fact]
    public async Task 匿名数据超期_定时任务删除且带走的moves与analyses()
    {
        await using var db = new TestDatabase();
        var service = CreateService(db);

        var old = await SeedAnonymousGameAsync(db, DateTime.UtcNow.AddHours(-30));

        var removed = await service.CleanupAsync();

        Assert.Equal(1, removed);
        Assert.False(await db.Context.Games.AnyAsync(g => g.Id == old));
        Assert.False(await db.Context.Moves.AnyAsync(m => m.GameId == old));
        Assert.False(await db.Context.Analyses.AnyAsync(a => a.GameId == old));
    }

    [Fact]
    public async Task 匿名数据没过保留期_不会被删()
    {
        await using var db = new TestDatabase();
        var service = CreateService(db);

        var fresh = await SeedAnonymousGameAsync(db, DateTime.UtcNow.AddHours(-2));

        var removed = await service.CleanupAsync();

        Assert.Equal(0, removed);
        Assert.True(await db.Context.Games.AnyAsync(g => g.Id == fresh));
    }

    [Fact]
    public async Task 登录用户的棋局_不会被当成匿名数据删掉()
    {
        await using var db = new TestDatabase();
        var service = CreateService(db);

        db.Context.ImportBatches.Add(new ImportBatch
        {
            FileName = "user.pgn",
            ImportedAt = DateTime.UtcNow.AddHours(-30),
            TotalGames = 1,
            ImportedGames = 1,
        });
        await db.Context.SaveChangesAsync();
        var batchId = await db.Context.ImportBatches.Select(b => b.Id).FirstAsync();

        db.Context.Games.Add(new Game
        {
            White = "Bob",
            Black = "Carol",
            ImportedAt = DateTime.UtcNow.AddHours(-30),
            ImportBatchId = batchId,
            OwnerUserId = TestDatabase.TestUserId,
        });
        await db.Context.SaveChangesAsync();

        var removed = await service.CleanupAsync();

        Assert.Equal(0, removed);
        Assert.Single(await db.Context.Games.ToListAsync());
    }

    [Fact]
    public async Task 清理之后_顺手清掉没有棋局的导入批次()
    {
        await using var db = new TestDatabase();
        var service = CreateService(db);

        await SeedAnonymousGameAsync(db, DateTime.UtcNow.AddHours(-30));

        await service.CleanupAsync();

        Assert.Empty(await db.Context.ImportBatches.ToListAsync());
    }

    [Fact]
    public void 清理时刻_固定在凌晨两点()
    {
        // 01:59 应该等到当天 02:00
        var before = new DateTime(2026, 1, 1, 1, 59, 0);
        var wait = AnonymousCleanupService.TimeUntilNextRun(before);
        Assert.Equal(TimeSpan.FromMinutes(1), wait);

        // 02:00 整点要等到第二天
        var at = new DateTime(2026, 1, 1, 2, 0, 0);
        var wait2 = AnonymousCleanupService.TimeUntilNextRun(at);
        Assert.Equal(TimeSpan.FromHours(24), wait2);

        // 白天也要等到第二天凌晨两点
        var noon = new DateTime(2026, 1, 1, 12, 0, 0);
        var wait3 = AnonymousCleanupService.TimeUntilNextRun(noon);
        Assert.Equal(TimeSpan.FromHours(14), wait3);
    }
}
