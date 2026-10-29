using ChessGameD.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessGameD.Web.Services.Auth;

/// <summary>
/// 创建管理员种子账号。
///
/// 账号名与初始口令来自配置(本地 <c>appsettings.Local.json</c> 或环境变量),
/// 真实值不进版本库;口令只在创建时用一次,库里存的是哈希。
/// </summary>
public static class SeedAdmin
{
    /// <summary>确保种子管理员存在;返回 true 表示这次真的创建了。</summary>
    public static async Task<bool> EnsureAsync(
        AppDbContext db,
        AuthOptions options,
        PasswordHashing passwords,
        ILogger logger,
        CancellationToken ct = default)
    {
        var userName = AccountService.NormalizeUserName(options.Admin.Username);
        var password = options.Admin.Password;

        if (userName is null || string.IsNullOrEmpty(password))
        {
            return false;
        }

        if (await db.Users.AnyAsync(u => u.UserName == userName, ct))
        {
            // 已经有同名账号了,不覆盖它的口令
            return false;
        }

        db.Users.Add(new User
        {
            UserName = userName,
            Email = null,
            PasswordHash = passwords.Hash(password),
            IsAdmin = true,
            IsEnabled = true,
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("已创建管理员账号 {UserName}(首次登录需要修改密码)", userName);
        return true;
    }
}
