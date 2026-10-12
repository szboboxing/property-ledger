using PropertyLedger.Core.Models;
using PropertyLedger.Core.Services;

namespace PropertyLedger.Tests;

/// <summary>租金递增：递增月租计算、月付/季付出账套用、批量结清「账单当月」日期模式。</summary>
public class LeaseEscalationTests
{
    private static Lease Lease(
        DateTime start, decimal rent = 1000m, int escMode = 0, decimal escValue = 0m,
        PaymentCycle cycle = PaymentCycle.Monthly, DateTime? end = null)
        => new()
        {
            RoomId = 1, TenantId = 1, StartDate = start, EndDate = end,
            RentAmount = rent, EscMode = escMode, EscValue = escValue,
            PaymentDay = 1, PaymentCycle = cycle,
        };

    // ── EscalatedMonthlyRent ──

    [Fact]
    public void EscalatedRent_NoEscalation_ReturnsBase()
    {
        var l = Lease(new DateTime(2026, 1, 1), rent: 1500m);
        Assert.Equal(1500m, BillingService.EscalatedMonthlyRent(l, 2028, 5));
    }

    [Fact]
    public void EscalatedRent_FirstYear_ReturnsBase()
    {
        var l = Lease(new DateTime(2026, 3, 15), escMode: 1, escValue: 5m);
        Assert.Equal(1000m, BillingService.EscalatedMonthlyRent(l, 2026, 12));
        Assert.Equal(1000m, BillingService.EscalatedMonthlyRent(l, 2027, 2));
    }

    [Fact]
    public void EscalatedRent_PercentMode_CompoundsYearly()
    {
        // 起租 2025-07：第 1 租约年（至 2026-06）原值，第 2 年（2026-07 起）= 1000×1.03，第 3 年（2027-07 起）= 1000×1.03²
        var l = Lease(new DateTime(2025, 7, 1), escMode: 1, escValue: 3m);
        Assert.Equal(1000m, BillingService.EscalatedMonthlyRent(l, 2025, 7));
        Assert.Equal(1000m, BillingService.EscalatedMonthlyRent(l, 2026, 6));
        Assert.Equal(1030m, BillingService.EscalatedMonthlyRent(l, 2026, 7));
        Assert.Equal(1060.90m, BillingService.EscalatedMonthlyRent(l, 2027, 7));
    }

    [Fact]
    public void EscalatedRent_AmountMode_AddsYearly()
    {
        var l = Lease(new DateTime(2025, 1, 1), escMode: 2, escValue: 100m);
        Assert.Equal(1000m, BillingService.EscalatedMonthlyRent(l, 2025, 12));
        Assert.Equal(1100m, BillingService.EscalatedMonthlyRent(l, 2026, 1));
        Assert.Equal(1300m, BillingService.EscalatedMonthlyRent(l, 2028, 6));
    }

    [Fact]
    public void EscalatedRent_ZeroValue_ReturnsBase()
    {
        var l = Lease(new DateTime(2025, 1, 1), escMode: 1, escValue: 0m);
        Assert.Equal(1000m, BillingService.EscalatedMonthlyRent(l, 2027, 1));
    }

    // ── TryGetPeriod 套用递增 ──

    [Fact]
    public void TryGetPeriod_Monthly_UsesEscalatedRent()
    {
        var l = Lease(new DateTime(2025, 1, 1), escMode: 2, escValue: 100m);
        Assert.True(BillingService.TryGetPeriod(l, 2026, 3, out var plan));
        Assert.Equal(1100m, plan.RentAmount);
        Assert.True(BillingService.TryGetPeriod(l, 2025, 3, out var plan1));
        Assert.Equal(1000m, plan1.RentAmount);
    }

    [Fact]
    public void TryGetPeriod_Quarterly_SumsEscalatedMonths()
    {
        // 季付起租 2025-07，2026-07 账期覆盖 2026-07/08/09（第 2 租约年，单月 1030）
        var l = Lease(new DateTime(2025, 7, 1), cycle: PaymentCycle.Quarterly, escMode: 1, escValue: 3m);
        Assert.True(BillingService.TryGetPeriod(l, 2026, 7, out var plan));
        Assert.Equal(3090m, plan.RentAmount);
        Assert.Equal(new DateTime(2026, 9, 30), plan.PeriodEnd);
        // 起租首期（2025-07）三个月均原值
        Assert.True(BillingService.TryGetPeriod(l, 2025, 7, out var first));
        Assert.Equal(3000m, first.RentAmount);
    }

    [Fact]
    public void TryGetPeriod_NoEscalation_KeepsLegacyQuarterlyAmount()
    {
        var l = Lease(new DateTime(2026, 1, 1), rent: 800m, cycle: PaymentCycle.Quarterly);
        Assert.True(BillingService.TryGetPeriod(l, 2026, 4, out var plan));
        Assert.Equal(2400m, plan.RentAmount);
    }

    // ── 批量结清「账单当月」──

    private static async Task<(Lease Lease, TestDb Db)> SeedAsync(TestDb db, Lease lease)
    {
        var p = new Property { Name = "幸福小区" };
        var r = new Room { Property = p, Name = "101" };
        var t = new Tenant { Name = "张三" };
        lease.Room = r;
        lease.Tenant = t;
        db.Db.Leases.Add(lease);
        await db.Db.SaveChangesAsync();
        return (lease, db);
    }

    [Fact]
    public async Task BatchSettle_UseBillDueDate_RegistersPaymentsAtEachDueDate()
    {
        using var db = new TestDb();
        var svc = new BillingService(db.Db);
        var (lease, _) = await SeedAsync(db, Lease(new DateTime(2026, 1, 1)));

        var b1 = new Bill
        {
            LeaseId = lease.Id, RoomId = 1, TenantId = 1,
            PeriodStart = new(2026, 1, 1), PeriodEnd = new(2026, 1, 31),
            DueDate = new(2026, 1, 1), RentAmount = 1000m, TotalAmount = 1000m,
        };
        var b2 = new Bill
        {
            LeaseId = lease.Id, RoomId = 1, TenantId = 1,
            PeriodStart = new(2026, 2, 1), PeriodEnd = new(2026, 2, 28),
            DueDate = new(2026, 2, 1), RentAmount = 1000m, TotalAmount = 1000m,
        };
        db.Db.Bills.AddRange(b1, b2);
        await db.Db.SaveChangesAsync();

        var (settled, total) = await svc.BatchSettleAsync(
            new[] { b1.Id, b2.Id }, PaymentMethod.WeChat,
            new DateTime(2026, 3, 10), useBillDueDate: true);

        Assert.Equal(2, settled);
        Assert.Equal(2000m, total);
        var pays = db.Db.Payments.OrderBy(p => p.BillId).ToList();
        Assert.Equal(new DateTime(2026, 1, 1), pays[0].PaidAt);
        Assert.Equal(new DateTime(2026, 2, 1), pays[1].PaidAt);
    }

    [Fact]
    public async Task BatchSettle_UniformDate_KeepsSamePaidAt()
    {
        using var db = new TestDb();
        var svc = new BillingService(db.Db);
        var (lease, _) = await SeedAsync(db, Lease(new DateTime(2026, 1, 1)));

        var b1 = new Bill
        {
            LeaseId = lease.Id, RoomId = 1, TenantId = 1,
            PeriodStart = new(2026, 1, 1), PeriodEnd = new(2026, 1, 31),
            DueDate = new(2026, 1, 1), RentAmount = 1000m, TotalAmount = 1000m,
        };
        db.Db.Bills.Add(b1);
        await db.Db.SaveChangesAsync();

        var when = new DateTime(2026, 3, 10);
        await svc.BatchSettleAsync(new[] { b1.Id }, PaymentMethod.Cash, when, useBillDueDate: false);

        Assert.Equal(when, db.Db.Payments.Single().PaidAt);
    }
}
