using ChessGameD.Web.Data;

namespace ChessGameD.Web.Services.Auth;

/// <summary>
/// 棋局归属。
///
/// 所有按 id 访问棋局的入口都必须先过归属过滤,越权一律按「不存在」处理(404),
/// 避免通过状态码泄露「这个 id 存在」。
/// </summary>
public static class GameOwnership
{
    /// <summary>当前身份可见的棋局;管理员可见全部。</summary>
    public static IQueryable<Game> Owned(this IQueryable<Game> query, CurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(user);

        if (user.IsAdmin)
        {
            return query;
        }

        // 登录用户只看自己的棋局
        if (user.UserId is int userId)
        {
            return query.Where(g => g.OwnerUserId == userId);
        }

        // 匿名访客只看本次会话上传的棋局
        var anonKey = user.EnsureAnonKey();
        if (string.IsNullOrEmpty(anonKey))
        {
            return query.Where(_ => false);
        }

        return query.Where(g => g.OwnerAnonKey == anonKey);
    }

    /// <summary>管理员用的「全部棋局」查询。</summary>
    public static IQueryable<Game> All(this IQueryable<Game> query)
        => query;
}

/// <summary>棋局归属:登录用户与匿名会话二选一。</summary>
public readonly record struct GameOwner(int? UserId, string? AnonKey)
{
    /// <summary>按当前身份取归属。</summary>
    public static GameOwner For(CurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.UserId is int userId
            ? new GameOwner(userId, null)
            : new GameOwner(null, user.EnsureAnonKey());
    }
}
