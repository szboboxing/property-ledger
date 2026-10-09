using PropertyLedger.Core.Models;
using PropertyLedger.Core.Services;

namespace PropertyLedger.Tests;

/// <summary>账期纯函数 TryGetPeriod：月付 / 季付对齐 / 短月付租日 / 租期边界。</summary>
public class PeriodTests
{
    private static Lease MonthlyLease(DateTime start, DateTime? end = null, int payDay = 5, decimal rent = 1500m) =>
        new()
        {
            RoomId = 1, TenantId = 1,
            StartDate = start, EndDate = end,
            RentAmount = rent, Deposit = rent,
            PaymentDay = payDay, PaymentCycle = PaymentCycle.Monthly,
            Status = LeaseStatus.Active,
        };

    [Fact]
    public void Monthly_NormalMonth_ProducesFullMonthPeriod()
    {
        var lease = MonthlyLease(new DateTime(2026, 9, 1));
        Assert.True(BillingService.TryGetPeriod(lease, 2026, 10, out var plan));
        Assert.Equal(new DateTime(2026, 10, 1), plan.PeriodStart);
        Assert.Equal(new DateTime(2026, 10, 31), plan.PeriodEnd);
        Assert.Equal(new DateTime(2026, 10, 5), plan.DueDate);
        Assert.Equal(1500m, plan.RentAmount);
    }

    [Fact]
    public void Monthly_BeforeLeaseStart_NoBill()
    {
        var lease = MonthlyLease(new DateTime(2026, 9, 15));
        Assert.False(BillingService.TryGetPeriod(lease, 2026, 8, out _));
    }

    [Fact]
    public void Monthly_MidMonthStart_StillBillsThatMonth()
    {
        // 月中起租，当月仍出账（账期按整月计，金额由房东按实际调整）
        var lease = MonthlyLease(new DateTime(2026, 9, 15));
        Assert.True(BillingService.TryGetPeriod(lease, 2026, 9, out var plan));
        Assert.Equal(new DateTime(2026, 9, 1), plan.PeriodStart);
    }

    [Fact]
    public void Monthly_AfterLeaseEnd_NoBill()
    {
        var lease = MonthlyLease(new DateTime(2026, 9, 1), new DateTime(2026, 11, 20));
        Assert.True(BillingService.TryGetPeriod(lease, 2026, 11, out _));
        Assert.False(BillingService.TryGetPeriod(lease, 2026, 12, out _));
    }

    [Fact]
    public void Monthly_ShortMonth_DueDateClampedToLastDay()
    {
        // 付租日 31，2 月只有 28 天 → 到期日应为 2 月 28 日
        var lease = MonthlyLease(new DateTime(2026, 1, 1), payDay: 31);
        Assert.True(BillingService.TryGetPeriod(lease, 2026, 2, out var plan));
        Assert.Equal(new DateTime(2026, 2, 28), plan.DueDate);
        Assert.Equal(new DateTime(2026, 2, 28), plan.PeriodEnd);
    }

    [Fact]
    public void Quarterly_OnlyOnAlignedMonths()
    {
        var lease = MonthlyLease(new DateTime(2026, 9, 1));
        lease.PaymentCycle = PaymentCycle.Quarterly;

        Assert.True(BillingService.TryGetPeriod(lease, 2026, 9, out var q1));
        Assert.Equal(4500m, q1.RentAmount);                    // 3 个月租金
        Assert.Equal(new DateTime(2026, 9, 1), q1.PeriodStart);
        Assert.Equal(new DateTime(2026, 11, 30), q1.PeriodEnd);

        Assert.False(BillingService.TryGetPeriod(lease, 2026, 10, out _));
        Assert.False(BillingService.TryGetPeriod(lease, 2026, 11, out _));
        Assert.True(BillingService.TryGetPeriod(lease, 2026, 12, out var q2));
        Assert.Equal(new DateTime(2026, 12, 1), q2.PeriodStart);
        Assert.Equal(new DateTime(2027, 2, 28), q2.PeriodEnd); // 跨年到 2 月短月
    }

    [Fact]
    public void Quarterly_CrossYearAlignment()
    {
        var lease = MonthlyLease(new DateTime(2026, 11, 1));
        lease.PaymentCycle = PaymentCycle.Quarterly;
        Assert.True(BillingService.TryGetPeriod(lease, 2027, 2, out var plan));
        Assert.Equal(new DateTime(2027, 2, 1), plan.PeriodStart);
        Assert.Equal(new DateTime(2027, 4, 30), plan.PeriodEnd);
    }
}
