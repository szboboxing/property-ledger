namespace PropertyLedger.Core.Services;

/// <summary>
/// 隐私脱敏工具：账单图等对外展示场景使用，界面内仍显示完整信息。
/// </summary>
public static class PrivacyMask
{
    /// <summary>
    /// 电话脱敏：保留首尾各 4 位，中间用 *** 隐藏（如 13812345678 → 1381***5678）。
    /// 长度不足 9 位（如 8 位座机）无法安全遮盖，原样返回。
    /// </summary>
    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Trim().Length < 9) return phone ?? "";
        var p = phone.Trim();
        return string.Concat(p.AsSpan(0, 4), "***", p.AsSpan(^4));
    }

    /// <summary>
    /// 姓名脱敏：保留首字（姓氏），其余每个字以 * 替换（如 张三 → 张*、王小明 → 王**）。
    /// 单字姓名原样返回。
    /// </summary>
    public static string MaskName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var n = name.Trim();
        if (n.Length == 1) return n;
        return string.Concat(n.AsSpan(0, 1), new string('*', n.Length - 1));
    }
}
