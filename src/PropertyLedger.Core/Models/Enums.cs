namespace PropertyLedger.Core.Models;

/// <summary>付租周期</summary>
public enum PaymentCycle
{
    /// <summary>月付：每月一张账单</summary>
    Monthly = 0,

    /// <summary>季付：每三个月一张账单，金额为三个月租金</summary>
    Quarterly = 1,
}

/// <summary>租约状态</summary>
public enum LeaseStatus
{
    /// <summary>生效中</summary>
    Active = 0,

    /// <summary>已终止 / 已退租</summary>
    Ended = 1,
}

/// <summary>账单收款状态</summary>
public enum BillStatus
{
    /// <summary>未付</summary>
    Unpaid = 0,

    /// <summary>部分已付</summary>
    Partial = 1,

    /// <summary>已付清</summary>
    Paid = 2,
}

/// <summary>收款方式</summary>
public enum PaymentMethod
{
    WeChat = 0,   // 微信
    Alipay = 1,   // 支付宝
    Bank = 2,     // 银行转账
    Cash = 3,     // 现金
    Other = 4,    // 其他
}
