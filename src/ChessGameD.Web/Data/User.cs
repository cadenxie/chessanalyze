using System.ComponentModel.DataAnnotations;

namespace ChessGameD.Web.Data;

/// <summary>用户账号。表名 Users。</summary>
public class User
{
    public int Id { get; set; }

    /// <summary>登录名,规范化小写后存储,唯一。</summary>
    [MaxLength(32)] public string UserName { get; set; } = "";

    /// <summary>可选邮箱,填了则唯一;不参与登录。</summary>
    [MaxLength(254)] public string? Email { get; set; }

    /// <summary>密码哈希(PBKDF2 输出),绝不存明文。</summary>
    [MaxLength(256)] public string PasswordHash { get; set; } = "";

    /// <summary>是否管理员:可进设置页、可管理用户、可看所有棋局。</summary>
    public bool IsAdmin { get; set; }

    /// <summary>是否启用;停用后无法登录,已登录的会话也会在下一次请求时失效。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>首次登录必须改密(种子管理员用)。</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>注册时间(UTC)。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>最后登录时间(UTC)。</summary>
    public DateTime? LastLoginAt { get; set; }

    public ICollection<Game> Games { get; set; } = new List<Game>();
}
