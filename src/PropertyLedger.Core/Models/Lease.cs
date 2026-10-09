namespace PropertyLedger.Core.Models;

/// <summary>租约：某个房间在一段时间内租给某个租客的约定。</summary>
public class Lease
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    public int TenantId { get; set; }

    /// <summary>起租日期</summary>
    public DateTime StartDate { get; set; }

    /// <summary>到期日期；为 null 表示长期 / 不定期</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>每月租金（月付时单月金额；季付时为单月金额，出账乘 3）</summary>
    public decimal RentAmount { get; set; }

    /// <summary>押金</summary>
    public decimal Deposit { get; set; }

    /// <summary>每月付租日（1-31）。31 日在短月自动落到当月最后一天。</summary>
    public int PaymentDay { get; set; } = 1;

    /// <summary>付租周期：月付 / 季付</summary>
    public PaymentCycle PaymentCycle { get; set; } = PaymentCycle.Monthly;

    /// <summary>状态：生效中 / 已终止</summary>
    public LeaseStatus Status { get; set; } = LeaseStatus.Active;

    /// <summary>备注（如「含宽带」「押一付三」）</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // 导航
    public Room? Room { get; set; }
    public Tenant? Tenant { get; set; }
    public List<Bill> Bills { get; set; } = new();

    /// <summary>该租约在指定日期是否仍在租期内。</summary>
    public bool IsActiveOn(DateTime date)
    {
        if (Status != LeaseStatus.Active) return false;
        if (date.Date < StartDate.Date) return false;
        if (EndDate.HasValue && date.Date > EndDate.Value.Date) return false;
        return true;
    }
}
