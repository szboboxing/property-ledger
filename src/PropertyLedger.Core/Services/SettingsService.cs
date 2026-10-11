using Microsoft.EntityFrameworkCore;
using PropertyLedger.Core.Data;

namespace PropertyLedger.Core.Services;

/// <summary>系统设置键名常量。</summary>
public static class SettingKeys
{
    /// <summary>房东称呼 / 落款（显示在账单顶部）</summary>
    public const string LandlordName = "landlord_name";

    /// <summary>收款提示语（显示在账单底部）</summary>
    public const string PaymentNotice = "payment_notice";

    /// <summary>收款码图片（data URI，data:image/png;base64,...），显示在账单上供微信扫码</summary>
    public const string PayQrCode = "pay_qrcode";
}

/// <summary>键值设置读写。</summary>
public class SettingsService
{
    /// <summary>收款提示语默认内容（设置页从未保存过该键时，界面预填、账单图兜底使用）。</summary>
    public const string DefaultPaymentNotice = "请在到期日前支付，转账后请告知，谢谢~";

    /// <summary>设置页收款提示语快选话术（点选替换输入框内容）。</summary>
    public static readonly string[] PaymentNoticePresets =
    {
        "请在到期日前支付，转账后请告知，谢谢~",
        "房租请按时支付，如有疑问随时联系我，谢谢~",
        "转账后请截图告知，方便核对登记，谢谢~",
        "请按账单金额支付，付款后我们会第一时间确认，谢谢~",
        "温馨提示：请按时交租，逾期会影响下期续租，感谢配合~",
    };

    private readonly AppDbContext _db;
    public SettingsService(AppDbContext db) => _db = db;

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var s = await _db.Settings.FindAsync(new object[] { key }, ct);
        return s?.Value;
    }

    public async Task<Dictionary<string, string?>> GetManyAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var ks = keys.ToList();
        var rows = await _db.Settings.Where(s => ks.Contains(s.Key)).ToListAsync(ct);
        return ks.ToDictionary(k => k, k => rows.FirstOrDefault(r => r.Key == k)?.Value);
    }

    public async Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        var s = await _db.Settings.FindAsync(new object[] { key }, ct);
        if (s is null)
        {
            _db.Settings.Add(new Models.Setting { Key = key, Value = value });
        }
        else
        {
            s.Value = value;
        }
        await _db.SaveChangesAsync(ct);
    }

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
}
