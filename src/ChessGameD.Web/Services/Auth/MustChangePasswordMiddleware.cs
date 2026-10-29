namespace ChessGameD.Web.Services.Auth;

/// <summary>
/// 强制改密中间件:种子管理员首次登录后,在改掉密码之前只能访问改密页与登出。
/// </summary>
public sealed class MustChangePasswordMiddleware
{
    private readonly RequestDelegate _next;

    public MustChangePasswordMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true
            && user.HasClaim(CurrentUser.MustChangePasswordClaim, "1")
            && !IsAllowed(context.Request.Path.Value))
        {
            context.Response.Redirect("/Account/Profile?force=1");
            return;
        }

        await _next(context);
    }

    /// <summary>改密期间允许访问的路径。</summary>
    private static bool IsAllowed(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return path.StartsWith("/Account/Profile", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/Account/Logout", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/Account/ChangePassword", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/account/", StringComparison.OrdinalIgnoreCase);
    }
}
