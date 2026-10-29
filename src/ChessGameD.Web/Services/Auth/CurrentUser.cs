using System.Security.Claims;

namespace ChessGameD.Web.Services.Auth;

/// <summary>
/// 当前请求的身份。
///
/// 登录用户从认证 Cookie 的 claims 里取;匿名访客靠一个会话级 Cookie
/// (<see cref="AnonCookieName"/>)识别——关掉浏览器就没了,下次进来看不到上次的棋局。
/// </summary>
public sealed class CurrentUser
{
    /// <summary>匿名会话 Cookie 名。</summary>
    public const string AnonCookieName = "chessgame.anon";

    /// <summary>「首次登录必须改密」的 claim 名。</summary>
    public const string MustChangePasswordClaim = "must_change_password";

    private readonly IHttpContextAccessor _accessor;
    private bool _loaded;
    private string? _anonKey;
    private int? _userId;
    private string _userName = "";
    private bool _isAdmin;

    public CurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    /// <summary>当前请求上下文;后台任务里为 null。</summary>
    public HttpContext? HttpContext => _accessor.HttpContext;

    /// <summary>登录用户 id;匿名访客为 null。</summary>
    public int? UserId
    {
        get
        {
            EnsureLoaded();
            return _userId;
        }
    }

    /// <summary>登录名;匿名访客为空串。</summary>
    public string UserName
    {
        get
        {
            EnsureLoaded();
            return _userName;
        }
    }

    /// <summary>是否管理员。</summary>
    public bool IsAdmin
    {
        get
        {
            EnsureLoaded();
            return _isAdmin;
        }
    }

    /// <summary>是否已登录。</summary>
    public bool IsAuthenticated => UserId.HasValue;

    /// <summary>已存在的匿名会话标识(只读,不会写 Cookie)。</summary>
    public string? AnonKey
    {
        get
        {
            EnsureLoaded();
            return _anonKey;
        }
    }

    /// <summary>
    /// 取匿名会话标识;没有就生成一个并写回响应。
    /// 响应已经开始发送时写不进去,此时只返回内存里的值(本次请求内仍然自洽)。
    /// </summary>
    public string EnsureAnonKey()
    {
        EnsureLoaded();
        if (!string.IsNullOrEmpty(_anonKey))
        {
            return _anonKey;
        }

        var key = NewAnonKey();
        var ctx = _accessor.HttpContext;
        if (ctx is not null)
        {
            try
            {
                ctx.Response.Cookies.Append(AnonCookieName, key, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    // 站点目前只有 HTTP(D19:暂不启用 HTTPS),加了 Secure 浏览器就不存这个 Cookie 了
                    Secure = false,
                    IsEssential = true,
                    Path = "/",
                });
            }
            catch (InvalidOperationException)
            {
                // 响应已经开始发送,写不进去就算了
            }
        }

        _anonKey = key;
        return key;
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        var ctx = _accessor.HttpContext;
        if (ctx is null)
        {
            return;
        }

        var principal = ctx.User;
        var rawId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(rawId, out var userId))
        {
            _userId = userId;
        }

        _userName = principal.FindFirst(ClaimTypes.Name)?.Value ?? "";
        _isAdmin = principal.IsInRole("Admin");

        if (ctx.Request.Cookies.TryGetValue(AnonCookieName, out var key) && IsValidAnonKey(key))
        {
            _anonKey = key;
        }
    }

    /// <summary>匿名会话标识:32 位十六进制 GUID。</summary>
    private static string NewAnonKey() => Guid.NewGuid().ToString("N");

    /// <summary>校验 Cookie 里的匿名标识,防止被塞进超长或非法内容。</summary>
    private static bool IsValidAnonKey(string key)
        => key.Length == 32 && key.All(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f'));
}
