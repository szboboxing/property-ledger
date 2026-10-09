namespace PropertyLedger.Core.Services;

/// <summary>业务规则异常，消息可直接展示给用户。</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

/// <summary>一个出账周期的计算结果。</summary>
public readonly record struct PeriodPlan(DateTime PeriodStart, DateTime PeriodEnd, DateTime DueDate, decimal RentAmount);
