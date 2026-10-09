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

    /// <summary>是否总管理员（拥有全部功能 + 用户管理）</summary>
    public bool IsAdmin { get; set; }

    /// <summary>普通用户可用的功能键 CSV（见 UserFeatures；总管理员忽略此字段）</summary>
    public string? Features { get; set; }

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? LastLoginAt { get; set; }

    /// <summary>该用户是否可用指定功能（总管理员恒为 true；普通用户看 Features）。</summary>
    public bool HasFeature(string key) =>
        IsAdmin || (Features ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(key);
}
