namespace PropertyLedger.Core.Models;

/// <summary>系统设置（键值存储），如房东称呼、收款码图片等。</summary>
public class Setting
{
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }
}
