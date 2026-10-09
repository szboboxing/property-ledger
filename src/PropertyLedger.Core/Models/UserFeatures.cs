namespace PropertyLedger.Core.Models;

/// <summary>
/// 普通用户可分配的功能模块（总管理员始终拥有全部功能与用户管理）。
/// 权限以 CSV 形式存储在 AppUser.Features 中，登录时转为 "feature" 声明。
/// </summary>
public static class UserFeatures
{
    /// <summary>全部可分配功能（Key, 显示名），顺序即界面展示顺序。</summary>
    public static readonly IReadOnlyList<(string Key, string Name)> All = new (string, string)[]
    {
        ("bills", "账单记账"),
        ("reports", "报表"),
        ("leases", "租约"),
        ("rooms", "房间"),
        ("tenants", "租客"),
        ("properties", "物业"),
        ("settings", "设置"),
    };

    /// <summary>新增普通用户的默认功能：仅账单记账。</summary>
    public const string DefaultForNewUser = "bills";

    public static bool IsValidKey(string key) => All.Any(a => a.Key == key);

    /// <summary>归一化 CSV：去空白、去重、剔除非法键，并按 All 的顺序输出。</summary>
    public static string Normalize(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return string.Empty;
        var set = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Where(IsValidKey).ToHashSet();
        return string.Join(',', All.Where(a => set.Contains(a.Key)).Select(a => a.Key));
    }

    /// <summary>解析为键列表（已归一化）。</summary>
    public static List<string> Parse(string? csv) =>
        Normalize(csv).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>解析为显示名列表。</summary>
    public static List<string> Names(string? csv) =>
        Parse(csv).Select(k => All.First(a => a.Key == k).Name).ToList();
}
