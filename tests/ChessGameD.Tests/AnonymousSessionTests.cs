using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChessGameD.Tests;

/// <summary>
/// 匿名访客:只能看到本次会话上传的棋局,换个会话就看不到了。
/// </summary>
public sealed class AnonymousSessionTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task 匿名上传_本次会话可见()
    {
        using var session = WebSession.Create(_factory);

        var import = await session.PostTextFieldAsync("/api/games/import", "text", TestPgn.SimpleGame);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);

        var moves = await session.GetAsync("/api/games/1/moves");
        Assert.Equal(HttpStatusCode.OK, moves.StatusCode);

        var list = await session.GetAsync("/Games/Index");
        Assert.Contains("Alice", await list.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 匿名上传_归属写的是匿名会话键()
    {
        using var session = WebSession.Create(_factory);

        await session.PostTextFieldAsync("/api/games/import", "text", TestPgn.SimpleGame);

        var owner = await _factory.WithDbAsync(async db =>
        {
            var game = await db.Games.SingleAsync();
            return (game.OwnerUserId, game.OwnerAnonKey);
        });

        Assert.Null(owner.OwnerUserId);
        Assert.NotNull(owner.OwnerAnonKey);
        Assert.Equal(32, owner.OwnerAnonKey!.Length);
    }

    [Fact]
    public async Task 清掉匿名Cookie_旧棋局不可见()
    {
        using var session = WebSession.Create(_factory);
        await session.PostTextFieldAsync("/api/games/import", "text", TestPgn.SimpleGame);

        // 换个浏览器:匿名会话没了,之前传的棋局也就看不到了
        session.ClearCookies();

        var moves = await session.GetAsync("/api/games/1/moves");
        Assert.Equal(HttpStatusCode.NotFound, moves.StatusCode);

        var list = await session.GetAsync("/Games/Index");
        Assert.DoesNotContain("Alice", await list.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 匿名会话登录_棋局转移到账号名下()
    {
        // 先把账号建好(在另一个会话里,免得把匿名会话里的棋局也带过去)
        using var setup = await AccountTestKit.RegisterAsync(_factory, "alice");

        using var session = WebSession.Create(_factory);
        await session.PostTextFieldAsync("/api/games/import", "text", TestPgn.SimpleGame);

        var login = await AccountTestKit.LoginAsync(session, "alice");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);

        var owner = await _factory.WithDbAsync(async db =>
        {
            var game = await db.Games.SingleAsync();
            var user = await db.Users.SingleAsync(u => u.UserName == "alice");
            return (game.OwnerUserId, game.OwnerAnonKey, user.Id);
        });

        Assert.Equal(owner.Id, owner.OwnerUserId);
        Assert.Null(owner.OwnerAnonKey);

        // 转移之后仍然能看到这局棋
        var moves = await session.GetAsync("/api/games/1/moves");
        Assert.Equal(HttpStatusCode.OK, moves.StatusCode);
    }

    [Fact]
    public async Task 匿名会话注册_棋局也转移到账号名下()
    {
        using var session = WebSession.Create(_factory);
        await session.PostTextFieldAsync("/api/games/import", "text", TestPgn.SimpleGame);

        var register = await AccountTestKit.RegisterAsync(session, "alice");
        Assert.Equal(HttpStatusCode.Found, register.StatusCode);

        var owner = await _factory.WithDbAsync(async db =>
        {
            var game = await db.Games.SingleAsync();
            return game.OwnerUserId;
        });

        Assert.NotNull(owner);
    }

    [Fact]
    public async Task 匿名访客_看不到别人的棋局()
    {
        using var owner = await AccountTestKit.RegisterAsync(_factory, "alice");
        await owner.PostTextFieldAsync("/api/games/import", "text", TestPgn.SimpleGame);

        using var visitor = WebSession.Create(_factory);
        var moves = await visitor.GetAsync("/api/games/1/moves");
        Assert.Equal(HttpStatusCode.NotFound, moves.StatusCode);
    }
}
