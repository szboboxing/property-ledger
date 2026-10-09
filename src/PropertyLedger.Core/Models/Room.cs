namespace PropertyLedger.Core.Models;

/// <summary>房间（一套可出租单元）。</summary>
public class Room
{
    public int Id { get; set; }

    public int PropertyId { get; set; }

    /// <summary>房号 / 房间名，如「3栋502」「2楼左间」</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>建筑面积（平方米，选填）</summary>
    public decimal? Area { get; set; }

    /// <summary>默认月租金（新建租约时带出，可在租约中修改）</summary>
    public decimal DefaultRent { get; set; }

    /// <summary>备注</summary>
    public string? Note { get; set; }

    /// <summary>是否归档（停用但保留历史）</summary>
    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // 导航
    public Property? Property { get; set; }
    public List<Lease> Leases { get; set; } = new();

    /// <summary>当前生效租约（界面用，非映射）</summary>
    public Lease? ActiveLease => Leases?.FirstOrDefault(l => l.Status == LeaseStatus.Active);
}
