using ChessGameD.Web.Data;

namespace ChessGameD.Web.Services.Play;

/// <summary>
/// 指导对弈会话的归属。
///
/// 和 <see cref="Auth.GameOwnership"/> 一样走显式谓词,不用 EF 全局查询过滤器:
/// 对弈的引擎应答是在请求里同步做的,但整局分析是后台作业,不能带着已回收的 HttpContext 走。
/// </summary>
public static class PlayOwnership
{
    /// <summary>当前身份可见的对局会话;管理员可见全部。</summary>
    public static IQueryable<PlaySession> Owned(this IQueryable<PlaySession> query, Auth.CurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(user);

        if (user.IsAdmin)
        {
            return query;
        }

        if (user.UserId is int userId)
        {
            return query.Where(s => s.OwnerUserId == userId);
        }

        var anonKey = user.EnsureAnonKey();
        if (string.IsNullOrEmpty(anonKey))
        {
            return query.Where(_ => false);
        }

        return query.Where(s => s.OwnerAnonKey == anonKey);
    }
}
