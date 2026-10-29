using System.Net;
using Xunit;

namespace ChessGameD.Tests;

/// <summary>账号相关的测试公用方法。</summary>
public static class AccountTestKit
{
    /// <summary>
    /// 解码 HTML 实体。
    ///
    /// Razor 默认的编码器会把非 ASCII 字符转成数字实体
    /// (比如「账号」会变成 &#x8D26;&#x53F7;),断言中文前要先解码。
    /// </summary>
    public static string Text(string html) => WebUtility.HtmlDecode(html);

    /// <summary>取出页面顶部菜单那一段。</summary>
    public static string Nav(string html)
    {
        var start = html.IndexOf("<nav>", StringComparison.Ordinal);
        var end = html.IndexOf("</nav>", StringComparison.Ordinal);
        return start >= 0 && end > start ? html[start..end] : "";
    }

    /// <summary>默认测试口令(够长、也不和账号名重合)。</summary>
    public const string DefaultPassword = "pass123";

    /// <summary>提交注册表单。</summary>
    public static Task<HttpResponseMessage> RegisterAsync(
        WebSession session,
        string userName,
        string? email = null,
        string? password = null,
        string? confirmPassword = null)
        => session.PostFormAsync("/Account/Register", new Dictionary<string, string>
        {
            ["UserName"] = userName,
            ["Email"] = email ?? string.Empty,
            ["Password"] = password ?? DefaultPassword,
            ["ConfirmPassword"] = confirmPassword ?? password ?? DefaultPassword,
        });

    /// <summary>提交登录表单。</summary>
    public static Task<HttpResponseMessage> LoginAsync(
        WebSession session,
        string userName,
        string? password = null)
        => session.PostFormAsync("/Account/Login", new Dictionary<string, string>
        {
            ["UserName"] = userName,
            ["Password"] = password ?? DefaultPassword,
        });

    /// <summary>注册一个新账号(不自动登录)。</summary>
    public static async Task<WebSession> RegisterAsync(
        AppFactory factory,
        string userName,
        string? email = null,
        string? password = null)
    {
        var session = WebSession.Create(factory);
        var response = await RegisterAsync(session, userName, email, password);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        return session;
    }

    /// <summary>用注入的种子账号登录管理员。</summary>
    public static async Task<WebSession> LoginAdminAsync(
        AppFactory factory,
        string userName = "admin",
        string password = "admin123")
    {
        var session = WebSession.Create(factory);
        var response = await LoginAsync(session, userName, password);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        return session;
    }

    /// <summary>修改自己的密码。</summary>
    public static Task<HttpResponseMessage> ChangePasswordAsync(
        WebSession session,
        string? oldPassword,
        string newPassword)
        => session.PostFormAsync("/Account/Profile", new Dictionary<string, string>
        {
            ["OldPassword"] = oldPassword ?? "",
            ["NewPassword"] = newPassword,
            ["ConfirmPassword"] = newPassword,
        });

    /// <summary>读当前身份接口的返回。</summary>
    public static async Task<(bool Authenticated, string UserName, bool IsAdmin)> MeAsync(WebSession session)
    {
        var response = await session.GetAsync("/api/account/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        return (
            root.GetProperty("isAuthenticated").GetBoolean(),
            root.GetProperty("userName").GetString() ?? "",
            root.GetProperty("isAdmin").GetBoolean());
    }
}
