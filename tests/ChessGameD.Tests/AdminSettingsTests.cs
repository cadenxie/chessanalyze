using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChessGameD.Tests;

/// <summary>管理员专属页面、停用即时生效、种子管理员。</summary>
public sealed class AdminSettingsTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task 普通用户访问_Settings_Index_被拒()
    {
        using var user = await AccountTestKit.RegisterAsync(_factory, "alice");

        var response = await user.GetAsync("/Settings/Index");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task 管理员访问_Settings_Index_返回200()
    {
        using var factory = new AppFactory(
            ("Chess:Admin:Username", "root"),
            ("Chess:Admin:Password", "seedpass"));
        using var admin = await AccountTestKit.LoginAdminAsync(factory, "root", "seedpass");
        var changed = await AccountTestKit.ChangePasswordAsync(admin, "seedpass", "newpass456");
        Assert.Equal(HttpStatusCode.Found, changed.StatusCode);

        var response = await admin.GetAsync("/Settings/Index");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task 普通用户访问_Admin_Users_被拒()
    {
        using var user = await AccountTestKit.RegisterAsync(_factory, "alice");

        var response = await user.GetAsync("/Admin/Users");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task 管理员访问_Admin_Users_返回200()
    {
        using var factory = new AppFactory(
            ("Chess:Admin:Username", "root"),
            ("Chess:Admin:Password", "seedpass"));
        using var admin = await AccountTestKit.LoginAdminAsync(factory, "root", "seedpass");
        var changed = await AccountTestKit.ChangePasswordAsync(admin, "seedpass", "newpass456");
        Assert.Equal(HttpStatusCode.Found, changed.StatusCode);

        var response = await admin.GetAsync("/Admin/Users");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"HTTP {(int)response.StatusCode}: {body[..Math.Min(2000, body.Length)]}");
    }

    [Fact]
    public async Task 普通用户_菜单里不出现设置入口()
    {
        using var user = await AccountTestKit.RegisterAsync(_factory, "alice");

        var html = await (await user.GetAsync("/Home/Index")).Content.ReadAsStringAsync();
        var nav = AccountTestKit.Nav(html);
        Assert.DoesNotContain("/Settings/Index", nav);
        Assert.DoesNotContain("/Admin/Users", nav);

        // 引擎没装时普通用户只看到引导文案,不给设置页入口
        Assert.DoesNotContain("/Settings/Index", html);
    }

    [Fact]
    public async Task 管理员_菜单里出现设置与用户管理入口()
    {
        using var factory = new AppFactory(
            ("Chess:Admin:Username", "root"),
            ("Chess:Admin:Password", "seedpass"));
        using var admin = await AccountTestKit.LoginAdminAsync(factory, "root", "seedpass");
        var changed = await AccountTestKit.ChangePasswordAsync(admin, "seedpass", "newpass456");
        Assert.Equal(HttpStatusCode.Found, changed.StatusCode);

        var html = await (await admin.GetAsync("/Home/Index")).Content.ReadAsStringAsync();
        var nav = AccountTestKit.Nav(html);
        Assert.Contains("/Settings/Index", nav);
        Assert.Contains("/Admin/Users", nav);
    }

    [Fact]
    public async Task 禁用账号_已登录会话下次请求即失效()
    {
        using var user = await AccountTestKit.RegisterAsync(_factory, "alice");
        Assert.True((await AccountTestKit.MeAsync(user)).Authenticated);

        await _factory.WithDbAsync(async db =>
        {
            var row = await db.Users.FirstAsync(u => u.UserName == "alice");
            row.IsEnabled = false;
            await db.SaveChangesAsync();
            return 0;
        });

        var response = await user.GetAsync("/Account/Profile");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString() ?? "");

        var (authenticated, _, _) = await AccountTestKit.MeAsync(user);
        Assert.False(authenticated);
    }

    [Fact]
    public async Task 种子管理员_配置为空_不创建任何管理员()
    {
        var count = await _factory.WithDbAsync(async db => await db.Users.CountAsync());
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task 种子管理员_配置有值_创建管理员且IsAdmin为真()
    {
        using var factory = new AppFactory(
            ("Chess:Admin:Username", "root"),
            ("Chess:Admin:Password", "seedpass"));

        var user = await factory.WithDbAsync(async db =>
            await db.Users.AsNoTracking().SingleAsync());

        Assert.Equal("root", user.UserName);
        Assert.True(user.IsAdmin);
        Assert.True(user.IsEnabled);
        Assert.True(user.MustChangePassword);

        // 库里只有哈希,没有明文
        Assert.DoesNotContain("seedpass", user.PasswordHash);
        Assert.NotEmpty(user.PasswordHash);
    }

    [Fact]
    public async Task 种子管理员_库里不存明文口令()
    {
        using var factory = new AppFactory(
            ("Chess:Admin:Username", "root"),
            ("Chess:Admin:Password", "seedpass"));

        var hash = await factory.WithDbAsync(async db =>
            await db.Users.AsNoTracking().Select(u => u.PasswordHash).SingleAsync());

        Assert.DoesNotContain("seedpass", hash);
    }

    [Fact]
    public async Task 普通用户_调管理接口_被拒()
    {
        using var user = await AccountTestKit.RegisterAsync(_factory, "alice");

        var response = await user.PostJsonAsync("/api/admin/users/1/enabled", new { enabled = false });
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact]
    public async Task 管理员_用接口停用账号_对方立刻失效()
    {
        using var factory = new AppFactory(
            ("Chess:Admin:Username", "root"),
            ("Chess:Admin:Password", "seedpass"));
        using var admin = await AccountTestKit.LoginAdminAsync(factory, "root", "seedpass");
        var changed = await AccountTestKit.ChangePasswordAsync(admin, "seedpass", "newpass456");
        Assert.Equal(HttpStatusCode.Found, changed.StatusCode);

        using var victim = await AccountTestKit.RegisterAsync(factory, "alice");
        Assert.True((await AccountTestKit.MeAsync(victim)).Authenticated);

        var victimId = await factory.WithDbAsync(async db =>
            await db.Users.Where(u => u.UserName == "alice").Select(u => u.Id).FirstAsync());

        var disable = await admin.PostJsonAsync($"/api/admin/users/{victimId}/enabled", new { enabled = false });
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        // 被停用的人下一次请求就得重新登录
        var response = await victim.GetAsync("/Account/Profile");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task 管理员_用接口重置密码_返回一次性临时密码()
    {
        using var factory = new AppFactory(
            ("Chess:Admin:Username", "root"),
            ("Chess:Admin:Password", "seedpass"));
        using var admin = await AccountTestKit.LoginAdminAsync(factory, "root", "seedpass");
        var changed = await AccountTestKit.ChangePasswordAsync(admin, "seedpass", "newpass456");
        Assert.Equal(HttpStatusCode.Found, changed.StatusCode);

        using var victim = await AccountTestKit.RegisterAsync(factory, "alice");
        var victimId = await factory.WithDbAsync(async db =>
            await db.Users.Where(u => u.UserName == "alice").Select(u => u.Id).FirstAsync());

        var reset = await admin.PostJsonAsync($"/api/admin/users/{victimId}/reset-password", new { });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var body = await reset.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var temp = doc.RootElement.GetProperty("temporaryPassword").GetString();
        Assert.NotNull(temp);
        Assert.True(temp!.Length >= 6);

        // 临时密码能登录
        var login = await AccountTestKit.LoginAsync(victim, "alice", temp);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
    }

    [Fact]
    public void 仓库配置_不含明文口令()
    {
        // 仓库里的配置文件只留空壳,真实口令由部署者注入
        var path = FindRepoFile(Path.Combine("src", "ChessGameD.Web", "appsettings.json"));
        Assert.NotNull(path);

        var json = File.ReadAllText(path!);
        Assert.Contains("\"Password\": \"\"", json);
    }

    /// <summary>从测试输出目录往上找仓库里的文件。</summary>
    private static string? FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
