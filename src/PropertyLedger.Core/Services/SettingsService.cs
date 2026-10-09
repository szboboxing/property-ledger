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
}
