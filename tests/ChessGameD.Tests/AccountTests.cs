using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChessGameD.Tests;

/// <summary>注册、登录、改密的行为。</summary>
public sealed class AccountTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task 注册_新账号_成功且为普通角色()
    {
        using var session = WebSession.Create(_factory);

        var response = await AccountTestKit.RegisterAsync(session, "Alice");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var (authenticated, userName, isAdmin) = await AccountTestKit.MeAsync(session);
        Assert.True(authenticated);
        Assert.Equal("alice", userName);
        Assert.False(isAdmin);
    }

    [Fact]
    public async Task 注册_账号名重复_提示统一为账号或邮箱不可用()
    {
        using var first = await AccountTestKit.RegisterAsync(_factory, "alice");
        using var second = WebSession.Create(_factory);

        var response = await AccountTestKit.RegisterAsync(second, "alice");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = AccountTestKit.Text(await response.Content.ReadAsStringAsync());
        Assert.Contains("账号或邮箱不可用", html);

        // 重复注册不应该把第一个账号顶掉
        var (authenticated, userName, _) = await AccountTestKit.MeAsync(first);
        Assert.True(authenticated);
        Assert.Equal("alice", userName);
    }

    [Fact]
    public async Task 注册_邮箱重复_同样统一提示()
    {
        using var first = await AccountTestKit.RegisterAsync(_factory, "alice", "same@example.com");
        using var second = WebSession.Create(_factory);

        var response = await AccountTestKit.RegisterAsync(second, "bob", "same@example.com");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("账号或邮箱不可用", AccountTestKit.Text(await response.Content.ReadAsStringAsync()));

        // 换个账号名、邮箱还是重复,提示也要一样(不告诉用户到底是哪一项撞了)
        var another = WebSession.Create(_factory);
        var response2 = await AccountTestKit.RegisterAsync(another, "carol", "same@example.com");
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Contains("账号或邮箱不可用", AccountTestKit.Text(await response2.Content.ReadAsStringAsync()));

        Assert.NotNull(first);
    }

    [Fact]
    public async Task 注册_两次密码不一致_拒绝()
    {
        using var session = WebSession.Create(_factory);

        var response = await AccountTestKit.RegisterAsync(session, "alice", password: "pass123", confirmPassword: "pass456");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("两次输入的密码不一致", AccountTestKit.Text(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task 注册_密码与账号名相同_拒绝()
    {
        using var session = WebSession.Create(_factory);

        var response = await AccountTestKit.RegisterAsync(session, "alice", password: "alice123", confirmPassword: "alice123");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("密码不能与账号名相同", AccountTestKit.Text(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task 注册_密码少于6位_拒绝()
    {
        using var session = WebSession.Create(_factory);

        var response = await AccountTestKit.RegisterAsync(session, "alice", password: "12345", confirmPassword: "12345");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("密码至少 6 位", AccountTestKit.Text(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task 登录_正确密码_成功并写认证Cookie()
    {
        using var session = await AccountTestKit.RegisterAsync(_factory, "alice");
        session.ClearCookies();

        var response = await AccountTestKit.LoginAsync(session, "alice");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.NotNull(session.GetCookie("chessgame.auth"));

        var (authenticated, userName, _) = await AccountTestKit.MeAsync(session);
        Assert.True(authenticated);
        Assert.Equal("alice", userName);
    }

    [Fact]
    public async Task 登录_错误密码_失败()
    {
        using var session = await AccountTestKit.RegisterAsync(_factory, "alice");
        session.ClearCookies();

        var response = await AccountTestKit.LoginAsync(session, "alice", "wrongpass");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("账号或密码不正确", AccountTestKit.Text(await response.Content.ReadAsStringAsync()));

        var (authenticated, _, _) = await AccountTestKit.MeAsync(session);
        Assert.False(authenticated);
    }

    [Fact]
    public async Task 登录_不存在的账号_提示与密码错误一致()
    {
        using var session = WebSession.Create(_factory);

        var response = await AccountTestKit.LoginAsync(session, "ghost");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("账号或密码不正确", AccountTestKit.Text(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task 登录_已禁用账号_失败()
    {
        using var session = await AccountTestKit.RegisterAsync(_factory, "alice");

        await _factory.WithDbAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.UserName == "alice");
            user.IsEnabled = false;
            await db.SaveChangesAsync();
            return 0;
        });

        session.ClearCookies();
        var response = await AccountTestKit.LoginAsync(session, "alice");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("账号或密码不正确", AccountTestKit.Text(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task 登录_种子管理员_被强制跳转改密页()
    {
        using var factory = new AppFactory(
            ("Chess:Admin:Username", "admin"),
            ("Chess:Admin:Password", "admin123"));

        using var session = WebSession.Create(factory);
        var response = await AccountTestKit.LoginAsync(session, "admin", "admin123");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/Account/Profile", response.Headers.Location?.ToString() ?? "");

        // 没改密之前,别的页面一律被拦到改密页
        var home = await session.GetAsync("/Home/Index");
        Assert.Equal(HttpStatusCode.Found, home.StatusCode);
        Assert.Contains("/Account/Profile", home.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task 改密_成功后旧密码失效()
    {
        using var session = await AccountTestKit.RegisterAsync(_factory, "alice");

        var change = await session.PostFormAsync("/Account/Profile", new Dictionary<string, string>
        {
            ["OldPassword"] = AccountTestKit.DefaultPassword,
            ["NewPassword"] = "newpass456",
            ["ConfirmPassword"] = "newpass456",
        });
        Assert.Equal(HttpStatusCode.Found, change.StatusCode);

        // 旧密码不能再登录
        session.ClearCookies();
        var oldLogin = await AccountTestKit.LoginAsync(session, "alice", AccountTestKit.DefaultPassword);
        Assert.Equal(HttpStatusCode.OK, oldLogin.StatusCode);
        Assert.Contains("账号或密码不正确", AccountTestKit.Text(await oldLogin.Content.ReadAsStringAsync()));

        // 新密码能登录
        var newLogin = await AccountTestKit.LoginAsync(session, "alice", "newpass456");
        Assert.Equal(HttpStatusCode.Found, newLogin.StatusCode);
    }

    [Fact]
    public async Task 改密_未提供旧密码_拒绝()
    {
        using var session = await AccountTestKit.RegisterAsync(_factory, "alice");

        var response = await session.PostFormAsync("/Account/Profile", new Dictionary<string, string>
        {
            ["OldPassword"] = "",
            ["NewPassword"] = "newpass456",
            ["ConfirmPassword"] = "newpass456",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("原密码不正确", AccountTestKit.Text(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task 登出_会话立即失效()
    {
        using var session = await AccountTestKit.RegisterAsync(_factory, "alice");

        var logout = await session.PostFormAsync("/Account/Logout", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Found, logout.StatusCode);

        var (authenticated, _, _) = await AccountTestKit.MeAsync(session);
        Assert.False(authenticated);
    }
}
