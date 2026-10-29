using System.Security.Claims;
using ChessGameD.Web.Data;
using ChessGameD.Web.Models;
using ChessGameD.Web.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChessGameD.Web.Controllers;

/// <summary>注册、登录、改密码。</summary>
[AllowAnonymous]
public class AccountController : Controller
{
    private readonly AppDbContext _db;
    private readonly AccountService _accounts;
    private readonly CurrentUser _current;
    private readonly AuthOptions _options;

    public AccountController(
        AppDbContext db,
        AccountService accounts,
        CurrentUser current,
        IOptions<AuthOptions> options)
    {
        _db = db;
        _accounts = accounts;
        _current = current;
        _options = options.Value;
    }

    [HttpGet]
    public IActionResult Register(string? returnUrl = null)
        => View(new RegisterViewModel { ReturnUrl = SafeReturnUrl(returnUrl) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel form, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _accounts.RegisterAsync(
            form.UserName,
            form.Email,
            form.Password,
            form.ConfirmPassword,
            ip,
            _options.RegistrationRateLimitPerHour,
            ct);

        if (!result.Ok || result.User is null)
        {
            form.Error = result.Error;
            return View(form);
        }

        await SignInAsync(result.User, ct);
        return Redirect(SafeReturnUrl(form.ReturnUrl) ?? "/Home/Index");
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
        => View(new LoginViewModel { ReturnUrl = SafeReturnUrl(returnUrl) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel form, CancellationToken ct)
    {
        var user = await _accounts.FindByUserNameAsync(form.UserName, ct);

        // 账号不存在、已停用、口令错误,对外都只说「账号或密码不正确」
        if (user is null || !user.IsEnabled || !await _accounts.VerifyPasswordAsync(user, form.Password, ct))
        {
            form.Error = "账号或密码不正确。";
            return View(form);
        }

        await _accounts.TouchLoginAsync(user, ct);
        await SignInAsync(user, ct);

        if (user.MustChangePassword)
        {
            return RedirectToAction(nameof(Profile), new { force = 1 });
        }

        return Redirect(SafeReturnUrl(form.ReturnUrl) ?? "/Home/Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/Home/Index");
    }

    /// <summary>个人中心:查看账号信息 + 修改密码。</summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Profile(bool force = false, CancellationToken ct = default)
    {
        var user = await _currentUserAsync(ct);
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var gameCount = await _db.Games.CountAsync(g => g.OwnerUserId == user.Id, ct);

        return View(new ProfileViewModel
        {
            UserName = user.UserName,
            Email = user.Email,
            IsAdmin = user.IsAdmin,
            MustChangePassword = user.MustChangePassword,
            GameCount = gameCount,
        });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel form, CancellationToken ct)
    {
        var user = await _currentUserAsync(ct);
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await _accounts.ChangePasswordAsync(
            user,
            form.OldPassword,
            form.NewPassword,
            form.ConfirmPassword,
            ct);

        if (!result.Ok)
        {
            form.Error = result.Error;
            form.UserName = user.UserName;
            form.Email = user.Email;
            form.IsAdmin = user.IsAdmin;
            form.GameCount = await _db.Games.CountAsync(g => g.OwnerUserId == user.Id, ct);
            return View(form);
        }

        // 改完密码重新签发,去掉「必须改密」的标记
        await SignInAsync(user, ct);

        return RedirectToAction(nameof(Profile), new { changed = 1 });
    }

    /// <summary>当前登录用户对应的账号记录。</summary>
    private async Task<User?> _currentUserAsync(CancellationToken ct)
    {
        if (_current.UserId is not int userId)
        {
            return null;
        }

        return await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
    }

    /// <summary>
    /// 签发登录 Cookie,并把本次匿名会话上传的棋局转到这个账号名下。
    /// </summary>
    private async Task SignInAsync(User user, CancellationToken ct)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Role, user.IsAdmin ? "Admin" : "User"),
        };

        if (user.MustChangePassword)
        {
            claims.Add(new Claim(CurrentUser.MustChangePasswordClaim, "1"));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        await ClaimAnonymousGamesAsync(user.Id, ct);
    }

    /// <summary>把匿名会话里的棋局转给登录用户。</summary>
    private async Task ClaimAnonymousGamesAsync(int userId, CancellationToken ct)
    {
        var anonKey = _current.AnonKey;
        if (string.IsNullOrEmpty(anonKey))
        {
            return;
        }

        var affected = await _db.Games
            .Where(g => g.OwnerAnonKey == anonKey)
            .ExecuteUpdateAsync(
                s => s.SetProperty(g => g.OwnerUserId, userId).SetProperty(g => g.OwnerAnonKey, (string?)null),
                ct);

        if (affected > 0)
        {
            Response.Cookies.Delete(CurrentUser.AnonCookieName, new CookieOptions { Path = "/" });
        }
    }

    /// <summary>只允许跳回本站页面,挡掉开放重定向。</summary>
    private string? SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return null;
        }

        if (!returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        return returnUrl;
    }
}
