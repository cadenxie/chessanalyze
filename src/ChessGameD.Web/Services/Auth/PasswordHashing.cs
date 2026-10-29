using ChessGameD.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace ChessGameD.Web.Services.Auth;

/// <summary>
/// 密码哈希。
///
/// 用 ASP.NET Core 的 PBKDF2 实现(HMAC-SHA256),输出自描述格式:
/// 算法标识 + 迭代次数 + salt + 子密钥,以后想提高强度可以直接平滑升级。
/// 数据库里只有哈希,没有明文。
/// </summary>
public sealed class PasswordHashing
{
    private readonly PasswordHasher<User> _hasher = new();

    /// <summary>算哈希。</summary>
    public string Hash(string password)
        => _hasher.HashPassword(new User(), password);

    /// <summary>校验口令;强度过时会在 <paramref name="rehash"/> 里带回新哈希。</summary>
    public bool Verify(string hash, string password, out string? rehash)
    {
        rehash = null;
        if (string.IsNullOrEmpty(hash))
        {
            return false;
        }

        var result = _hasher.VerifyHashedPassword(new User(), hash, password);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            rehash = Hash(password);
        }

        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
