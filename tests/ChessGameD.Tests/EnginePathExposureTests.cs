using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChessGameD.Tests;

/// <summary>
/// 引擎可执行文件的绝对路径属于服务器内部信息,只允许管理员看到。
///
/// 覆盖三条线:状态接口按角色裁剪、首页按角色渲染、安装类接口只对管理员开放。
/// </summary>
public class EnginePathExposureTests
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>在应用自己的数据库里写一条引擎路径配置(模拟装过引擎)。</summary>
    private static Task SetEnginePathAsync(AppFactory factory, string path)
    {
        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ChessGameD.Web.Data.SettingsStore>();
        return settings.SetAsync(ChessGameD.Web.Data.SettingsStore.EnginePath, path);
    }

    /// <summary>读状态接口里的 path 字段(没返回就是 null)。</summary>
    private static async Task<string?> ReadPathAsync(WebSession session)
    {
        var response = await session.GetAsync("/api/engine/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var el = doc.RootElement.GetProperty("path");
        return el.ValueKind == JsonValueKind.Null ? null : el.GetString();
    }

    [Fact]
    public async Task 匿名访客_状态接口_不返回引擎路径()
    {
        using var factory = new AppFactory();
        await SetEnginePathAsync(factory, "/opt/secret/stockfish");

        using var session = WebSession.Create(factory);
        Assert.Null(await ReadPathAsync(session));
    }

    [Fact]
    public async Task 普通用户_状态接口_不返回引擎路径()
    {
        using var factory = new AppFactory();
        await SetEnginePathAsync(factory, "/opt/secret/stockfish");

        using var session = await AccountTestKit.RegisterAsync(factory, "alice");
        var login = await AccountTestKit.LoginAsync(session, "alice");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);

        Assert.Null(await ReadPathAsync(session));
    }

    [Fact]
    public async Task 管理员_状态接口_返回引擎路径()
    {
        using var factory = new AppFactory(
            ("Chess:Admin:Username", "admin"),
            ("Chess:Admin:Password", "admin123"));
        await SetEnginePathAsync(factory, "/opt/secret/stockfish");

        using var admin = await AccountTestKit.LoginAdminAsync(factory);
        // 种子管理员首次登录必须先改密
        var changed = await AccountTestKit.ChangePasswordAsync(admin, "admin123", "newpass456");
        Assert.Equal(HttpStatusCode.Found, changed.StatusCode);

        Assert.Equal("/opt/secret/stockfish", await ReadPathAsync(admin));
    }

    [Fact]
    public async Task 首页_普通用户_不渲染引擎路径行()
    {
        using var factory = new AppFactory();
        using var session = WebSession.Create(factory);

        var response = await session.GetAsync("/Home/Index");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("id=\"engine-path\"", html);
    }

    [Fact]
    public async Task 首页_管理员_渲染引擎路径行()
    {
        using var factory = new AppFactory(
            ("Chess:Admin:Username", "admin"),
            ("Chess:Admin:Password", "admin123"));

        using var admin = await AccountTestKit.LoginAdminAsync(factory);
        var changed = await AccountTestKit.ChangePasswordAsync(admin, "admin123", "newpass456");
        Assert.Equal(HttpStatusCode.Found, changed.StatusCode);

        var response = await admin.GetAsync("/Home/Index");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("id=\"engine-path\"", html);
    }

    [Fact]
    public async Task 安装引擎接口_普通用户_被拒绝()
    {
        using var factory = new AppFactory();
        using var session = await AccountTestKit.RegisterAsync(factory, "bob");
        var login = await AccountTestKit.LoginAsync(session, "bob");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);

        var response = await session.PostJsonAsync("/api/engine/install", new { });
        // Cookie 认证对未授权请求统一 302 跳登录页
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task 安装状态接口_匿名访客_被拒绝()
    {
        using var factory = new AppFactory();
        using var session = WebSession.Create(factory);

        var response = await session.GetAsync("/api/engine/install/status?jobId=none");
        // Cookie 认证对未授权请求统一 302 跳登录页
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString() ?? "");
    }
}
