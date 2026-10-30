using ChessGameD.Web.Data;
using ChessGameD.Web.Services.Analysis;
using ChessGameD.Web.Services.Auth;
using ChessGameD.Web.Services.Play;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChessGameD.Tests;

/// <summary>指导对弈:开局、走子、提示、悔棋、终局。</summary>
public sealed class PlayServiceTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _db.DisposeAsync();

    /* ---------------- 测试脚手架 ---------------- */

    private PlayService NewService(FakeCoachEngine? engine = null)
        => new(_db.Context, engine ?? new FakeCoachEngine(), _db.Settings, NullLogger<PlayService>.Instance);

    private static CurrentUser NewUser()
        => new(new TestHttpContextAccessor());

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static List<PlayMove> MovesOf(PlaySession session)
        => System.Text.Json.JsonSerializer.Deserialize<List<PlayMove>>(
            session.MovesJson,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? new();

    /* ---------------- 开局 ---------------- */

    [Fact]
    public async Task 开局_建立会话_额度按设置初始化()
    {
        await _db.Settings.SetIntAsync(SettingsStore.PlayHintQuota, 5);
        await _db.Settings.SetIntAsync(SettingsStore.PlayTakebackQuota, 2);

        var service = NewService();
        var session = await service.CreateAsync(NewUser(), CoachLevels.Default, default);

        Assert.Equal(PlaySessionStates.Active, session.Status);
        Assert.Equal(0, session.PlyCount);
        Assert.Equal(5, session.HintsLeft);
        Assert.Equal(2, session.TakebacksLeft);
        Assert.Equal(CoachLevels.Default, session.Level);
        Assert.Empty(MovesOf(session));
        Assert.Equal(PlayService.StandardFen, session.CurrentFen);
    }

    [Fact]
    public async Task 开局_越界的档位_落到默认档()
    {
        var service = NewService();
        var session = await service.CreateAsync(NewUser(), 99, default);

        Assert.Equal(CoachLevels.Default, session.Level);
    }

    /* ---------------- 走子 ---------------- */

    [Fact]
    public async Task 走一步_引擎应手_棋谱两个半回合()
    {
        var service = NewService();
        var session = await service.CreateAsync(NewUser(), CoachLevels.Default, default);

        var outcome = await service.SubmitMoveAsync(session, "e2e4", default);
        var moves = MovesOf(session);

        Assert.False(outcome.GameOver);
        Assert.Equal(2, moves.Count);
        Assert.Equal(1, moves[0].Ply);
        Assert.Equal("e4", moves[0].San);
        Assert.False(moves[0].IsEngineMove);
        Assert.Equal(2, moves[1].Ply);
        Assert.True(moves[1].IsEngineMove);
        Assert.Equal(2, session.PlyCount);
    }

    [Fact]
    public async Task 走非法着法_抛状态异常()
    {
        var service = NewService();
        var session = await service.CreateAsync(NewUser(), CoachLevels.Default, default);

        await Assert.ThrowsAsync<PlayStateException>(
            () => service.SubmitMoveAsync(session, "e2e5", default));
    }

    [Fact]
    public async Task 下出严重失误_标记为坏棋并写批注()
    {
        // 开局 0 分,玩家走完之后掉到 -500:损失 500 厘兵 = 严重失误
        var engine = new FakeCoachEngine(fen => fen == PlayService.StandardFen ? 0 : -500);
        var service = NewService(engine);
        var session = await service.CreateAsync(NewUser(), CoachLevels.Default, default);

        var outcome = await service.SubmitMoveAsync(session, "e2e4", default);
        var moves = MovesOf(session);

        Assert.True(outcome.IsBadMove);
        Assert.Equal("严重失误", outcome.AlertLabel);
        Assert.NotNull(moves[0].Comment);
        Assert.Contains("严重失误", moves[0].Comment);
        Assert.Equal(1, session.BlunderCount);
        Assert.Equal(1, session.MistakeCount);
    }

    [Fact]
    public async Task 下出漏着_单独计数()
    {
        // 本来赢(+200),走完之后优势没了(0):漏着
        var engine = new FakeCoachEngine(fen => fen == PlayService.StandardFen ? 200 : 0);
        var service = NewService(engine);
        var session = await service.CreateAsync(NewUser(), CoachLevels.Default, default);

        var outcome = await service.SubmitMoveAsync(session, "e2e4", default);
        var moves = MovesOf(session);

        Assert.True(outcome.IsBadMove);
        Assert.True(moves[0].IsMissedWin);
        Assert.Equal(1, session.MissedWinCount);
    }

    [Fact]
    public async Task 引擎放水_记在棋谱里()
    {
        // 用一个「次优走法明显」的引擎:引擎回复不是最佳走法
        var engine = new FakeCoachEngine();
        var service = NewService(engine);
        var session = await service.CreateAsync(NewUser(), 1, default);

        await service.SubmitMoveAsync(session, "e2e4", default);
        var moves = MovesOf(session);

        // 入门档位下引擎有 35% 概率走次优;这里只断言引擎那步一定有标记字段
        Assert.True(moves[1].IsEngineMove);
        Assert.False(string.IsNullOrEmpty(moves[1].Uci));
    }

    /* ---------------- 提示 ---------------- */

    [Fact]
    public async Task 提示_看评估_不消耗次数()
    {
        var service = NewService();
        var session = await service.CreateAsync(NewUser(), CoachLevels.Default, default);

        var hint = await service.HintAsync(session, HintStrength.Eval, default);

        Assert.False(hint.Consumed);
        Assert.Equal(3, session.HintsLeft);
        Assert.Equal(0, session.HintsUsed);
    }

    [Fact]
    public async Task 提示_看候选_消耗一次()
    {
        var service = NewService();
        var session = await service.CreateAsync(NewUser(), CoachLevels.Default, default);

        var hint = await service.HintAsync(session, HintStrength.Candidates, default);

        Assert.True(hint.Consumed);
        Assert.Equal(2, session.HintsLeft);
        Assert.Equal(1, session.HintsUsed);
        Assert.NotEmpty(hint.Candidates);
    }

    [Fact]
    public async Task 提示_次数用完_再要提示报错()
    {
        await _db.Settings.SetIntAsync(SettingsStore.PlayHintQuota, 1);
        var service = NewService();
        var session = await service.CreateAsync(NewUser(), CoachLevels.Default, default);

        await service.HintAsync(session, HintStrength.Best, default);

        await Assert.ThrowsAsync<PlayStateException>(
            () => service.HintAsync(session, HintStrength.Best, default));
    }

    /* ---------------- 悔棋 ---------------- */

    [Fact]
    public async Task 悔棋_退回玩家的决策点()
    {
        var service = NewService();
        var session = await service.CreateAsync(NewUser(), CoachLevels.Default, default);

        await service.SubmitMoveAsync(session, "e2e4", default);
        Assert.Equal(2, session.PlyCount);

        var outcome = await service.TakebackAsync(session, default);

        Assert.Equal(2, outcome.RemovedPlies);
        Assert.Equal(0, session.PlyCount);
        Assert.Empty(MovesOf(session));
        Assert.Equal(2, session.TakebacksLeft);
        Assert.Equal(1, session.TakebacksUsed);
        Assert.Equal(PlayService.StandardFen, session.CurrentFen);
    }

    [Fact]
    public async Task 悔棋_次数用完_报错()
    {
        await _db.Settings.SetIntAsync(SettingsStore.PlayTakebackQuota, 1);
        var service = NewService();
        var session = await service.CreateAsync(NewUser(), CoachLevels.Default, default);

        await service.SubmitMoveAsync(session, "e2e4", default);
        await service.TakebackAsync(session, default);

        await Assert.ThrowsAsync<PlayStateException>(
            () => service.TakebackAsync(session, default));
    }

    [Fact]
    public async Task 悔棋_撤掉的着法不进最终棋谱()
    {
        var service = NewService();
        var session = await service.CreateAsync(NewUser(), CoachLevels.Default, default);

        await service.SubmitMoveAsync(session, "e2e4", default);
        await service.TakebackAsync(session, default);
        await service.SubmitMoveAsync(session, "d2d4", default);

        var finish = await service.FinishAsync(session, "1-0", "认输", default);
        var game = await _db.Context.Games.Include(g => g.Moves).FirstAsync(g => g.Id == finish.GameId);

        Assert.Equal(2, game.Moves.Count);
        Assert.Equal("d4", game.Moves.First(m => m.Ply == 1).San);
        Assert.DoesNotContain(game.Moves, m => m.San == "e4");
    }

    /* ---------------- 终局 ---------------- */

    [Fact]
    public async Task 终局_写入棋局_带教练批注()
    {
        var engine = new FakeCoachEngine(fen => fen == PlayService.StandardFen ? 0 : -500);
        var service = NewService(engine);
        var session = await service.CreateAsync(NewUser(), CoachLevels.Default, default);

        await service.SubmitMoveAsync(session, "e2e4", default);
        var finish = await service.FinishAsync(session, "0-1", "认输", default);

        var game = await _db.Context.Games.Include(g => g.Moves).FirstAsync(g => g.Id == finish.GameId);

        Assert.Equal("0-1", game.Result);
        Assert.Equal("认输", game.Termination);
        Assert.Equal(2, game.Moves.Count);
        Assert.NotNull(game.Moves.First(m => m.Ply == 1).Comment);
        Assert.Contains("指导对弈", game.PgnText);
        Assert.Equal(PlaySessionStates.Finished, session.Status);
        Assert.Equal(finish.GameId, session.GameId);
    }

    [Fact]
    public async Task 终局_之后的走子_报错()
    {
        var service = NewService();
        var session = await service.CreateAsync(NewUser(), CoachLevels.Default, default);

        await service.FinishAsync(session, "1-0", "认输", default);

        await Assert.ThrowsAsync<PlayStateException>(
            () => service.SubmitMoveAsync(session, "e2e4", default));
    }

    /* ---------------- 归属 ---------------- */

    [Fact]
    public async Task 归属_匿名会话_互相看不到()
    {
        var service = NewService();
        var first = await service.CreateAsync(NewUser(), CoachLevels.Default, default);
        var second = await service.CreateAsync(NewUser(), CoachLevels.Default, default);

        Assert.NotEqual(first.OwnerAnonKey, second.OwnerAnonKey);
        Assert.Null(await service.GetOwnedAsync(first.Id, NewUser(), default));
        Assert.NotNull(await service.GetOwnedAsync(first.Id, await OwnerOf(first), default));
    }

    private static async Task<CurrentUser> OwnerOf(PlaySession session)
    {
        // 用同一个匿名 key 造一个"同一个人"的当前用户
        var accessor = new TestHttpContextAccessor();
        var user = new CurrentUser(accessor);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = $"{CurrentUser.AnonCookieName}={session.OwnerAnonKey}";
        accessor.HttpContext = ctx;
        await Task.CompletedTask;
        return user;
    }
}
