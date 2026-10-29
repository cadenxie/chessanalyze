using ChessGameD.Web.Data;
using ChessGameD.Web.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChessGameD.Web.Controllers.Api;

/// <summary>管理员的账号管理接口(页面版在 /Admin/Users)。</summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminUsersApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccountService _accounts;
    private readonly CurrentUser _current;

    public AdminUsersApiController(AppDbContext db, AccountService accounts, CurrentUser current)
    {
        _db = db;
        _accounts = accounts;
        _current = current;
    }

    /// <summary>用户列表。</summary>
    [HttpGet("")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var users = await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.Id)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.Email,
                u.IsAdmin,
                u.IsEnabled,
                u.MustChangePassword,
                u.CreatedAt,
                u.LastLoginAt,
            })
            .ToListAsync(ct);

        return Ok(users);
    }

    /// <summary>启用 / 停用账号。</summary>
    [HttpPost("{id:int}/enabled")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetEnabled(int id, SetEnabledRequest request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound(new { error = "账号不存在。" });
        }

        // 不让自己把自己停用,否则会把自己锁在门外
        if (!request.Enabled && _current.UserId == user.Id)
        {
            return BadRequest(new { error = "不能停用当前登录的账号。" });
        }

        await _accounts.SetEnabledAsync(user, request.Enabled, ct);
        return Ok(new { ok = true, enabled = user.IsEnabled });
    }

    /// <summary>重置密码;不填就生成一个临时密码,只在响应里返回一次。</summary>
    [HttpPost("{id:int}/reset-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest? request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound(new { error = "账号不存在。" });
        }

        var password = string.IsNullOrWhiteSpace(request?.NewPassword)
            ? GenerateTempPassword()
            : request!.NewPassword!.Trim();

        var result = await _accounts.SetPasswordAsync(user, password, null, ct);
        if (!result.Ok)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new { ok = true, temporaryPassword = password });
    }

    /// <summary>提升 / 降级管理员。</summary>
    [HttpPost("{id:int}/role")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(int id, SetRoleRequest request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound(new { error = "账号不存在。" });
        }

        // 最后一个管理员不能降级,否则没人能进设置页了
        if (!request.IsAdmin && user.IsAdmin && await CountAdminsAsync(ct) <= 1)
        {
            return BadRequest(new { error = "至少要保留一个管理员。" });
        }

        await _accounts.SetAdminAsync(user, request.IsAdmin, ct);
        return Ok(new { ok = true, isAdmin = user.IsAdmin });
    }

    /// <summary>删除账号(连同它的棋局与分析)。</summary>
    [HttpDelete("{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound(new { error = "账号不存在。" });
        }

        if (_current.UserId == user.Id)
        {
            return BadRequest(new { error = "不能删除当前登录的账号。" });
        }

        if (user.IsAdmin && await CountAdminsAsync(ct) <= 1)
        {
            return BadRequest(new { error = "至少要保留一个管理员。" });
        }

        await _accounts.DeleteUserAsync(user, ct);
        return Ok(new { ok = true });
    }

    private Task<int> CountAdminsAsync(CancellationToken ct)
        => _db.Users.CountAsync(u => u.IsAdmin && u.IsEnabled, ct);

    /// <summary>生成一个临时口令(字母 + 数字,10 位)。</summary>
    private static string GenerateTempPassword()
    {
        const string chars = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rng = Random.Shared;
        return new string(Enumerable.Range(0, 10).Select(_ => chars[rng.Next(chars.Length)]).ToArray());
    }
}

/// <summary>启用 / 停用请求。</summary>
public sealed class SetEnabledRequest
{
    public bool Enabled { get; set; }
}

/// <summary>重置密码请求(新密码可空,空则自动生成)。</summary>
public sealed class ResetPasswordRequest
{
    public string? NewPassword { get; set; }
}

/// <summary>设置角色请求。</summary>
public sealed class SetRoleRequest
{
    public bool IsAdmin { get; set; }
}
