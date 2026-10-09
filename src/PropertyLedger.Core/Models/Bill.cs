namespace PropertyLedger.Core.Models;

/// <summary>账单：某个租约在一个账期（一个月或一个季度）的应收款。</summary>
public class Bill
{
    public int Id { get; set; }

    public int LeaseId { get; set; }

    /// <summary>冗余房间 / 租客，便于列表直接展示</summary>
    public int RoomId { get; set; }
    public int TenantId { get; set; }

    /// <summary>账期开始日（含）</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>账期结束日（含）</summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>应付截止日期</summary>
    public DateTime DueDate { get; set; }

    /// <summary>租金（月付=单月；季付=三个月合计）</summary>
    public decimal RentAmount { get; set; }

    /// <summary>应收总额 = 租金 + 杂费合计（由服务端维护，避免前端算错）</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>已收金额（由收款记录累加）</summary>
    public decimal PaidAmount { get; set; }

    /// <summary>收款状态</summary>
    public BillStatus Status { get; set; } = BillStatus.Unpaid;

    /// <summary>结清时间</summary>
    public DateTime? PaidAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>备注（支持富文本/图片，HTML 存储）</summary>
    public string? Note { get; set; }

    /// <summary>是否人工补录（非自动出账生成，用于记录过往欠款）</summary>
    public bool IsManual { get; set; }

    // 导航
    public Lease? Lease { get; set; }
    public Room? Room { get; set; }
    public Tenant? Tenant { get; set; }
    public List<BillItem> Items { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();

    /// <summary>杂费合计</summary>
    public decimal ExtraAmount => Items?.Sum(i => i.Amount) ?? 0m;

    /// <summary>剩余未收</summary>
    public decimal RemainingAmount => Math.Max(0m, TotalAmount - PaidAmount);

    /// <summary>是否逾期（截止日已过且未付清）</summary>
    public bool IsOverdue(DateTime today) => DueDate.Date < today.Date && Status != BillStatus.Paid;
}
