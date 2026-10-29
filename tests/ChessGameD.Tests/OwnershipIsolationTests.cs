using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChessGameD.Tests;

/// <summary>
/// 数据隔离:两个用户之间的棋局、分析、报告互不可见。
///
/// 这是整个账号模块最关键的一组用例,任何一条挂了都不许发布。
/// </summary>
public sealed class OwnershipIsolationTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    /// <summary>导入一局棋,返回它的 id。</summary>
    private static async Task<int> ImportAsync(AppFactory factory, WebSession session, string pgn = TestPgn.SimpleGame)
    {
        var response = await session.PostTextFieldAsync("/api/games/import", "text", pgn);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await factory.WithDbAsync(async db =>
            await db.Games.OrderByDescending(g => g.Id).Select(g => g.Id).FirstAsync());
    }

    [Fact]
    public async Task 用户A上传棋局_用户B的列表里看不到()
    {
        using var a = await AccountTestKit.RegisterAsync(_factory, "alice");
        using var b = await AccountTestKit.RegisterAsync(_factory, "bob");

        var gameId = await ImportAsync(_factory, a);

        var listA = await a.GetAsync("/Games/Index");
        var listB = await b.GetAsync("/Games/Index");

        Assert.Contains("Alice", await listA.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Alice", await listB.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 用户A的棋局id_用户B访问moves返回404()
    {
        using var a = await AccountTestKit.RegisterAsync(_factory, "alice");
        using var b = await AccountTestKit.RegisterAsync(_factory, "bob");

        var gameId = await ImportAsync(_factory, a);

        var response = await b.GetAsync($"/api/games/{gameId}/moves");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 用户A的棋局id_用户B访问analysis返回404()
    {
        using var a = await AccountTestKit.RegisterAsync(_factory, "alice");
        using var b = await AccountTestKit.RegisterAsync(_factory, "bob");

        var gameId = await ImportAsync(_factory, a);

        var response = await b.GetAsync($"/api/games/{gameId}/analysis");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 用户A的棋局id_用户B访问report返回404()
    {
        using var a = await AccountTestKit.RegisterAsync(_factory, "alice");
        using var b = await AccountTestKit.RegisterAsync(_factory, "bob");

        var gameId = await ImportAsync(_factory, a);

        var response = await b.GetAsync($"/api/games/{gameId}/report?format=html");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 用户A的棋局id_用户B删除返回404且数据仍在()
    {
        using var a = await AccountTestKit.RegisterAsync(_factory, "alice");
        using var b = await AccountTestKit.RegisterAsync(_factory, "bob");

        var gameId = await ImportAsync(_factory, a);

        var response = await b.DeleteAsync($"/api/games/{gameId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // 棋局还在,而且 A 还能看
        var stillThere = await _factory.WithDbAsync(async db =>
            await db.Games.AnyAsync(g => g.Id == gameId));
        Assert.True(stillThere);

        var owner = await a.GetAsync($"/api/games/{gameId}/moves");
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    [Fact]
    public async Task 用户A的棋局id_用户B启动分析返回404()
    {
        using var a = await AccountTestKit.RegisterAsync(_factory, "alice");
        using var b = await AccountTestKit.RegisterAsync(_factory, "bob");

        var gameId = await ImportAsync(_factory, a);

        var response = await b.PostFormAsync($"/api/games/{gameId}/analyze", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 用户A的棋局id_用户B看复盘页返回404()
    {
        using var a = await AccountTestKit.RegisterAsync(_factory, "alice");
        using var b = await AccountTestKit.RegisterAsync(_factory, "bob");

        var gameId = await ImportAsync(_factory, a);

        var response = await b.GetAsync($"/Games/Review/{gameId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 管理员_可以看到所有用户的棋局()
    {
        using var factory = new AppFactory(
            ("Chess:Admin:Username", "admin"),
            ("Chess:Admin:Password", "admin123"));

        using var a = await AccountTestKit.RegisterAsync(factory, "alice");
        using var b = await AccountTestKit.RegisterAsync(factory, "bob");
        using var admin = await AccountTestKit.LoginAdminAsync(factory);

        // 种子管理员首次登录必须先改密,改完才能访问别的页面
        var changed = await AccountTestKit.ChangePasswordAsync(admin, "admin123", "newpass456");
        Assert.Equal(HttpStatusCode.Found, changed.StatusCode);

        await ImportAsync(factory, a);
        await ImportAsync(factory, b);

        var list = await admin.GetAsync("/Games/Index");
        var html = await list.Content.ReadAsStringAsync();
        Assert.Contains("Alice", html);
        Assert.Contains("Bob", html);

        // 管理员也能直接按 id 取棋局
        var gameId = await factory.WithDbAsync(async db =>
            await db.Games.OrderBy(g => g.Id).Select(g => g.Id).FirstAsync());
        var moves = await admin.GetAsync($"/api/games/{gameId}/moves");
        Assert.Equal(HttpStatusCode.OK, moves.StatusCode);
    }

    [Fact]
    public async Task 用户A的棋局_导入后归属写的是A()
    {
        using var a = await AccountTestKit.RegisterAsync(_factory, "alice");

        var gameId = await ImportAsync(_factory, a);

        var owner = await _factory.WithDbAsync(async db =>
        {
            var game = await db.Games.FirstAsync(g => g.Id == gameId);
            return (game.OwnerUserId, game.OwnerAnonKey);
        });

        Assert.NotNull(owner.OwnerUserId);
        Assert.Null(owner.OwnerAnonKey);
    }
}
