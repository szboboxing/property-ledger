namespace PropertyLedger.Core.Models;

/// <summary>账单杂费明细（水费、电费、物业费、网费等），每张账单可含多条。</summary>
public class BillItem
{
    public int Id { get; set; }

    public int BillId { get; set; }

    /// <summary>费用名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>金额</summary>
    public decimal Amount { get; set; }

    /// <summary>备注（如水电表读数）</summary>
    public string? Remark { get; set; }

    public Bill? Bill { get; set; }
}
