namespace PropertyLedger.Core.Models;

/// <summary>登录用户（多用户账号；密码以 PBKDF2 哈希存储）。</summary>
public class AppUser
{
    public int Id { get; set; }

    /// <summary>登录名（唯一）</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>显示名</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>PBKDF2 密码哈希（格式：iterations.salt.hash，均 Base64）</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>是否管理员（可管理用户账号）</summary>
    public bool IsAdmin { get; set; }

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? LastLoginAt { get; set; }
}
