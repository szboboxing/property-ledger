namespace PropertyLedger.Core.Models;

/// <summary>收款记录：一张账单可分多次收款。</summary>
public class Payment
{
    public int Id { get; set; }

    public int BillId { get; set; }

    /// <summary>收款金额</summary>
    public decimal Amount { get; set; }

    /// <summary>收款日期</summary>
    public DateTime PaidAt { get; set; } = DateTime.Now;

    /// <summary>收款方式</summary>
    public PaymentMethod Method { get; set; } = PaymentMethod.WeChat;

    /// <summary>备注</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public Bill? Bill { get; set; }
}
