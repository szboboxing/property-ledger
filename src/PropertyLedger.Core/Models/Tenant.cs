namespace PropertyLedger.Core.Models;

/// <summary>租客档案（可复用，退租后保留，便于再次出租时关联）。</summary>
public class Tenant
{
    public int Id { get; set; }

    /// <summary>姓名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>手机号（微信转发账单时使用）</summary>
    public string? Phone { get; set; }

    /// <summary>身份证号（选填）</summary>
    public string? IdCard { get; set; }

    /// <summary>备注</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // 导航
    public List<Lease> Leases { get; set; } = new();
}
