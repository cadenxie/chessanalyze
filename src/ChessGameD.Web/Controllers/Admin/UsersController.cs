using ChessGameD.Web.Data;
using ChessGameD.Web.Models;
using ChessGameD.Web.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChessGameD.Web.Controllers.Admin;

/// <summary>管理员的账号管理。</summary>
[Authorize(Policy = "AdminOnly")]
[Route("Admin/Users")]
public class UsersController : Controller
{
    private readonly AppDbContext _db;
    private readonly AccountService _accounts;
    private readonly CurrentUser _current;

    public UsersController(AppDbContext db, AccountService accounts, CurrentUser current)
    {
        _db = db;
        _accounts = accounts;
        _current = current;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var users = await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.Id)
            .ToListAsync(ct);

        // 只统计挂在用户名下的棋局(匿名棋局不计入)
        var counts = await _db.Games
            .AsNoTracking()
            .Where(g => g.OwnerUserId != null)
            .GroupBy(g => g.OwnerUserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var model = new AdminUsersViewModel
        {
            Users = users.Select(u => new AdminUserItem
            {
                Id = u.Id,
                UserName = u.UserName,
                Email = u.Email,
                IsAdmin = u.IsAdmin,
                IsEnabled = u.IsEnabled,
                MustChangePassword = u.MustChangePassword,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
                GameCount = counts.TryGetValue(u.Id, out var c) ? c : 0,
            }).ToList(),
        };

        return View(model);
    }

    /// <summary>启用 / 停用账号。</summary>
    [HttpPost("ToggleEnabled")]
    public async Task<IActionResult> ToggleEnabled(int id, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound();
        }

        // 不让自己把自己停用,否则会把自己锁在门外
        if (!user.IsEnabled && _current.UserId == user.Id)
        {
            return RedirectToAction(nameof(Index), new { error = "不能停用当前登录的账号。" });
        }

        await _accounts.SetEnabledAsync(user, !user.IsEnabled, ct);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>重置密码:不填就生成一个临时密码,只在这次响应里显示一次。</summary>
    [HttpPost("ResetPassword")]
    public async Task<IActionResult> ResetPassword(int id, string? newPassword, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound();
        }

        var password = string.IsNullOrWhiteSpace(newPassword) ? GenerateTempPassword() : newPassword.Trim();
        var result = await _accounts.SetPasswordAsync(user, password, null, ct);

        if (!result.Ok)
        {
            return RedirectToAction(nameof(Index), new { error = result.Error });
        }

        return RedirectToAction(nameof(Index), new { temp = password });
    }

    /// <summary>提升 / 降级管理员。</summary>
    [HttpPost("SetRole")]
    public async Task<IActionResult> SetRole(int id, bool isAdmin, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound();
        }

        // 最后一个管理员不能降级,否则没人能进设置页了
        if (!isAdmin && user.IsAdmin && await CountAdminsAsync(ct) <= 1)
        {
            return RedirectToAction(nameof(Index), new { error = "至少要保留一个管理员。" });
        }

        await _accounts.SetAdminAsync(user, isAdmin, ct);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>删除账号(连同它的棋局)。</summary>
    [HttpPost("Delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound();
        }

        if (_current.UserId == user.Id)
        {
            return RedirectToAction(nameof(Index), new { error = "不能删除当前登录的账号。" });
        }

        if (user.IsAdmin && await CountAdminsAsync(ct) <= 1)
        {
            return RedirectToAction(nameof(Index), new { error = "至少要保留一个管理员。" });
        }

        await _accounts.DeleteUserAsync(user, ct);
        return RedirectToAction(nameof(Index));
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
