namespace ChessGameD.Web.Services.Auth;

/// <summary>
/// 账号模块的配置。绑定到 <c>Chess</c> 节点,与引擎配置同段但互不干扰。
/// </summary>
public sealed class AuthOptions
{
    /// <summary>管理员种子账号(仅在本地配置或环境变量里填真实值,不要提交到版本库)。</summary>
    public AdminSeedOptions Admin { get; set; } = new();

    /// <summary>匿名棋局保留多少小时后被定时任务清理。</summary>
    public int AnonymousRetentionHours { get; set; } = 24;

    /// <summary>同一 IP 每小时最多注册几个账号。</summary>
    public int RegistrationRateLimitPerHour { get; set; } = 5;
}

/// <summary>管理员种子账号配置。</summary>
public sealed class AdminSeedOptions
{
    /// <summary>管理员登录名;留空表示不创建种子管理员。</summary>
    public string? Username { get; set; }

    /// <summary>管理员初始口令;留空表示不创建种子管理员。</summary>
    public string? Password { get; set; }
}
