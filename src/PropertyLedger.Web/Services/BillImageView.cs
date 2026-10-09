namespace PropertyLedger.Web.Services;

/// <summary>账单图片渲染所需的只读视图（由调用方从数据库组装）。</summary>
public record BillImageView
{
    public required string LandlordName { get; init; }
    public required string RoomName { get; init; }
    public required string? PropertyName { get; init; }
    public required string TenantName { get; init; }
    public required string? TenantPhone { get; init; }
    public required string Period { get; init; }      // 如 2026-10
    public required string PeriodRange { get; init; } // 如 2026-10-01 ~ 2026-10-31
    public required string DueDate { get; init; }
    public required string StatusText { get; init; }
    public required List<(string Name, decimal Amount)> Lines { get; init; }
    public required decimal Total { get; init; }
    public required decimal Paid { get; init; }
    public required decimal Remaining { get; init; }
    public required string PaymentNotice { get; init; }
    /// <summary>收款码图片 data URI（可空）</summary>
    public string? PayQrCodeDataUri { get; init; }
}
