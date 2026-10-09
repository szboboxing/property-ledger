using System.Net;
using System.Text.RegularExpressions;

namespace PropertyLedger.Core.Services;

/// <summary>
/// 富文本（HTML）备注的工具方法：剥离标签取纯文本摘要，用于列表/表格中的安全展示。
/// 正文渲染时仍由界面层用 MarkupString 输出原始 HTML（仅登录用户可录入，图片走受保护上传接口）。
/// </summary>
public static class HtmlText
{
    private static readonly Regex ScriptStyleRegex =
        new(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex BlockTagRegex =
        new(@"<br\s*/?>|</(p|div|li|h[1-6]|tr)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>剥离 HTML 标签与脚本样式，返回纯文本摘要（超过 max 字符截断加省略号）。</summary>
    public static string Excerpt(string? html, int max = 40)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var text = ScriptStyleRegex.Replace(html, " ");
        text = BlockTagRegex.Replace(text, " ");
        text = TagRegex.Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = WhitespaceRegex.Replace(text, " ").Trim();

        if (max > 0 && text.Length > max)
            text = text[..max] + "…";
        return text;
    }
}
