using System.Text.RegularExpressions;
using ChessGameD.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessGameD.Web.Services.Auth;

/// <summary>账号操作的结果。</summary>
public sealed class AccountResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public User? User { get; init; }

    public static AccountResult Success(User user) => new() { Ok = true, User = user };
    public static AccountResult Fail(string error) => new() { Error = error };
}

/// <summary>
/// 账号相关操作:注册、登录校验、改密、管理员管理。
/// </summary>
public sealed class AccountService
{
    /// <summary>口令最小长度。</summary>
    public const int MinPasswordLength = 6;

    /// <summary>口令最大长度。</summary>
    public const int MaxPasswordLength = 128;

    private readonly AppDbContext _db;
    private readonly PasswordHashing _passwords;
    private readonly RegistrationLimiter _limiter;

    public AccountService(AppDbContext db, PasswordHashing passwords, RegistrationLimiter limiter)
    {
        _db = db;
        _passwords = passwords;
        _limiter = limiter;
    }

    // ---- 校验规则 ----

    /// <summary>登录名规范化为小写;非法时返回 null。</summary>
    public static string? NormalizeUserName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var name = raw.Trim().ToLowerInvariant();
        return UserNamePattern.IsMatch(name) ? name : null;
    }

    /// <summary>校验登录名,返回错误文案(合法时为 null)。</summary>
    public static string? ValidateUserName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "请填写账号名。";
        }

        var name = raw.Trim();
        if (name.Length is < 2 or > 32)
        {
            return "账号名长度要在 2–32 个字符之间。";
        }

        if (!UserNamePattern.IsMatch(name.ToLowerInvariant()))
        {
            return "账号名只能包含字母、数字和下划线。";
        }

        return null;
    }

    /// <summary>校验并规范化邮箱;合法时返回小写值,非法时返回 null。</summary>
    public static string? NormalizeEmail(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var email = raw.Trim();
        if (email.Length > 254 || !EmailPattern.IsMatch(email))
        {
            return null;
        }

        return email.ToLowerInvariant();
    }

    /// <summary>校验口令;合法时返回 null。</summary>
    public static string? ValidatePassword(string? password, string userName)
    {
        if (string.IsNullOrEmpty(password))
        {
            return "请填写密码。";
        }

        if (password.Length < MinPasswordLength)
        {
            return $"密码至少 {MinPasswordLength} 位。";
        }

        if (password.Length > MaxPasswordLength)
        {
            return $"密码不能超过 {MaxPasswordLength} 位。";
        }

        if (!string.IsNullOrEmpty(userName) && password.Contains(userName, StringComparison.OrdinalIgnoreCase))
        {
            return "密码不能与账号名相同。";
        }

        return null;
    }

    // ---- 注册 ----

    /// <summary>注册一个新账号(普通用户)。</summary>
    public async Task<AccountResult> RegisterAsync(
        string? userName,
        string? email,
        string? password,
        string? confirmPassword,
        string clientIp,
        int rateLimitPerHour,
        CancellationToken ct = default)
    {
        var userNameError = ValidateUserName(userName);
        if (userNameError is not null)
        {
            return AccountResult.Fail(userNameError);
        }

        var normalizedName = NormalizeUserName(userName)!;

        string? normalizedEmail = null;
        if (!string.IsNullOrWhiteSpace(email))
        {
            normalizedEmail = NormalizeEmail(email);
            if (normalizedEmail is null)
            {
                return AccountResult.Fail("邮箱格式不正确。");
            }
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            return AccountResult.Fail("两次输入的密码不一致。");
        }

        var passwordError = ValidatePassword(password, normalizedName);
        if (passwordError is not null)
        {
            return AccountResult.Fail(passwordError);
        }

        if (!_limiter.TryAcquire(clientIp, rateLimitPerHour, TimeSpan.FromHours(1)))
        {
            return AccountResult.Fail("注册太频繁了,请一小时后再试。");
        }

        // 账号名与邮箱都查一遍,但对外只给一句统一提示,免得被拿去枚举账号
        var taken = await _db.Users
            .AsNoTracking()
            .AnyAsync(u => u.UserName == normalizedName
                           || (normalizedEmail != null && u.Email == normalizedEmail), ct);

        if (taken)
        {
            return AccountResult.Fail("账号或邮箱不可用。");
        }

        var user = new User
        {
            UserName = normalizedName,
            Email = normalizedEmail,
            PasswordHash = _passwords.Hash(password!),
            IsAdmin = false,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return AccountResult.Success(user);
    }

    // ---- 登录 ----

    /// <summary>按账号名找用户。</summary>
    public Task<User?> FindByUserNameAsync(string? userName, CancellationToken ct = default)
    {
        var normalized = NormalizeUserName(userName);
        if (normalized is null)
        {
            return Task.FromResult<User?>(null);
        }

        return _db.Users.FirstOrDefaultAsync(u => u.UserName == normalized, ct);
    }

    /// <summary>校验口令;顺带把强度过时的哈希升级掉。</summary>
    public async Task<bool> VerifyPasswordAsync(User user, string? password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        if (!_passwords.Verify(user.PasswordHash, password, out var rehash))
        {
            return false;
        }

        if (rehash is not null)
        {
            user.PasswordHash = rehash;
            await _db.SaveChangesAsync(ct);
        }

        return true;
    }

    /// <summary>记录登录时间。</summary>
    public async Task TouchLoginAsync(User user, CancellationToken ct = default)
    {
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ---- 改密 ----

    /// <summary>用户自己改密码:必须提供旧密码。</summary>
    public async Task<AccountResult> ChangePasswordAsync(
        User user,
        string? oldPassword,
        string? newPassword,
        string? confirmPassword,
        CancellationToken ct = default)
    {
        if (!await VerifyPasswordAsync(user, oldPassword, ct))
        {
            return AccountResult.Fail("原密码不正确。");
        }

        return await SetPasswordAsync(user, newPassword, confirmPassword, ct);
    }

    /// <summary>设置新密码(管理员重置 / 用户改密共用)。</summary>
    public async Task<AccountResult> SetPasswordAsync(
        User user,
        string? newPassword,
        string? confirmPassword,
        CancellationToken ct = default)
    {
        if (confirmPassword is not null && !string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            return AccountResult.Fail("两次输入的密码不一致。");
        }

        var error = ValidatePassword(newPassword, user.UserName);
        if (error is not null)
        {
            return AccountResult.Fail(error);
        }

        user.PasswordHash = _passwords.Hash(newPassword!);
        user.MustChangePassword = false;
        await _db.SaveChangesAsync(ct);
        return AccountResult.Success(user);
    }

    // ---- 管理员操作 ----

    /// <summary>启用 / 停用账号。</summary>
    public async Task SetEnabledAsync(User user, bool enabled, CancellationToken ct = default)
    {
        user.IsEnabled = enabled;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>设置管理员角色。</summary>
    public async Task SetAdminAsync(User user, bool isAdmin, CancellationToken ct = default)
    {
        user.IsAdmin = isAdmin;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>删除账号,连同它的棋局一起删。</summary>
    public async Task DeleteUserAsync(User user, CancellationToken ct = default)
    {
        // 先删棋局(走 EF 的级联把 moves / analyses 带走),再删账号
        var games = await _db.Games.Where(g => g.OwnerUserId == user.Id).ToListAsync(ct);
        _db.Games.RemoveRange(games);
        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
    }

    private static readonly Regex UserNamePattern = new(
        "^[a-z0-9_]{2,32}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EmailPattern = new(
        "^[^@\\s]+@[^@\\s.]+(\\.[^@\\s.]+)+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
