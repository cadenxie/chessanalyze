namespace ChessGameD.Web.Models;

/// <summary>注册表单。</summary>
public sealed class RegisterViewModel
{
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? ConfirmPassword { get; set; }
    public string? ReturnUrl { get; set; }
    public string? Error { get; set; }
}

/// <summary>登录表单。</summary>
public sealed class LoginViewModel
{
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? ReturnUrl { get; set; }
    public string? Error { get; set; }
}

/// <summary>个人中心(改密码)。</summary>
public sealed class ProfileViewModel
{
    public string UserName { get; set; } = "";
    public string? Email { get; set; }
    public bool IsAdmin { get; set; }
    public bool MustChangePassword { get; set; }
    public int GameCount { get; set; }
    public string? OldPassword { get; set; }
    public string? NewPassword { get; set; }
    public string? ConfirmPassword { get; set; }
    public string? Error { get; set; }
    public string? Success { get; set; }
}

/// <summary>管理员的用户列表。</summary>
public sealed class AdminUsersViewModel
{
    public IReadOnlyList<AdminUserItem> Users { get; set; } = Array.Empty<AdminUserItem>();
}

/// <summary>用户列表里的一行。</summary>
public sealed class AdminUserItem
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string? Email { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsEnabled { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int GameCount { get; set; }
}
