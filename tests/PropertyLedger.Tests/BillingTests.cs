using Microsoft.EntityFrameworkCore;
using PropertyLedger.Core.Models;
using PropertyLedger.Core.Services;

namespace PropertyLedger.Tests;

/// <summary>出账幂等、部分收款、超额拒绝、撤销回退、状态迁移。</summary>
public class BillingTests
{
    private static async Task<(Property p, Room r, Tenant t, Lease l)> SeedLeaseAsync(TestDb db,
        DateTime start, PaymentCycle cycle = PaymentCycle.Monthly, decimal rent = 1500m)
    {
        var p = new Property { Name = "幸福小区" };
        var r = new Room { Property = p, Name = "101", DefaultRent = rent };
        var t = new Tenant { Name = "张三", Phone = "13800000000" };
        var l = new Lease
        {
            Room = r, Tenant = t,
            StartDate = start,
            RentAmount = rent, HouseDeposit = rent,
            PaymentDay = 5, PaymentCycle = cycle,
            Status = LeaseStatus.Active,
        };
        db.Db.Leases.Add(l);
        await db.Db.SaveChangesAsync();
        return (p, r, t, l);
    }

    [Fact]
    public async Task EnsureBills_CreatesMonthlyBills_AndIsIdempotent()
    {
        using var db = new TestDb();
        var (_, _, _, lease) = await SeedLeaseAsync(db, new DateTime(2026, 9, 1));
        var svc = new BillingService(db.Db);

        var created1 = await svc.EnsureBillsThroughAsync(new DateTime(2026, 11, 1));
        Assert.Equal(3, created1); // 9 / 10 / 11 月

        var created2 = await svc.EnsureBillsThroughAsync(new DateTime(2026, 11, 1));
        Assert.Equal(0, created2); // 幂等，不重复出账

        var bills = await db.Db.Bills.Where(b => b.LeaseId == lease.Id).OrderBy(b => b.PeriodStart).ToListAsync();
        Assert.Equal(3, bills.Count);
        Assert.All(bills, b => Assert.Equal(BillStatus.Unpaid, b.Status));
        Assert.All(bills, b => Assert.Equal(1500m, b.TotalAmount));
        Assert.Equal(new DateTime(2026, 9, 5), bills[0].DueDate);
    }

    [Fact]
    public async Task EnsureBills_Quarterly_OnlyAlignedMonths()
    {
        using var db = new TestDb();
        await SeedLeaseAsync(db, new DateTime(2026, 9, 1), PaymentCycle.Quarterly);
        var svc = new BillingService(db.Db);

        var created = await svc.EnsureBillsThroughAsync(new DateTime(2027, 3, 1));
        Assert.Equal(3, created); // 9-11 / 12-2 / 3-5 三期

        var bills = await db.Db.Bills.OrderBy(b => b.PeriodStart).ToListAsync();
        Assert.Equal(new DateTime(2026, 11, 30), bills[0].PeriodEnd);
        Assert.Equal(4500m, bills[0].TotalAmount);
        Assert.Equal(new DateTime(2026, 12, 1), bills[1].PeriodStart);
    }

    [Fact]
    public async Task EnsureBills_TerminatedLease_NoBills()
    {
        using var db = new TestDb();
        var (_, _, _, lease) = await SeedLeaseAsync(db, new DateTime(2026, 9, 1));
        lease.Status = LeaseStatus.Ended;
        await db.Db.SaveChangesAsync();

        var svc = new BillingService(db.Db);
        Assert.Equal(0, await svc.EnsureBillsThroughAsync(new DateTime(2026, 11, 1)));
    }

    [Fact]
    public async Task PartialPayment_ThenFull_StatusTransitions()
    {
        using var db = new TestDb();
        var (_, _, _, lease) = await SeedLeaseAsync(db, new DateTime(2026, 9, 1));
        var svc = new BillingService(db.Db);
        await svc.EnsureBillsThroughAsync(new DateTime(2026, 9, 1));
        var bill = await db.Db.Bills.SingleAsync();

        // 部分收款 → Partial
        await svc.RecordPaymentAsync(bill.Id, 500m, PaymentMethod.WeChat, new DateTime(2026, 9, 3), null);
        bill = await db.Db.Bills.Include(b => b.Payments).SingleAsync();
        Assert.Equal(500m, bill.PaidAmount);
        Assert.Equal(1000m, bill.RemainingAmount);
        Assert.Equal(BillStatus.Partial, bill.Status);
        Assert.Null(bill.PaidAt);

        // 补足 → Paid
        await svc.RecordPaymentAsync(bill.Id, 1000m, PaymentMethod.Cash, new DateTime(2026, 9, 4), null);
        bill = await db.Db.Bills.Include(b => b.Payments).SingleAsync();
        Assert.Equal(BillStatus.Paid, bill.Status);
        Assert.Equal(0m, bill.RemainingAmount);
        Assert.Equal(new DateTime(2026, 9, 4), bill.PaidAt);
    }

    [Fact]
    public async Task Overpayment_Rejected()
    {
        using var db = new TestDb();
        await SeedLeaseAsync(db, new DateTime(2026, 9, 1));
        var svc = new BillingService(db.Db);
        await svc.EnsureBillsThroughAsync(new DateTime(2026, 9, 1));
        var bill = await db.Db.Bills.SingleAsync();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => svc.RecordPaymentAsync(bill.Id, 1500.01m, PaymentMethod.WeChat, DateTime.Today, null));
        Assert.Contains("超出剩余应收", ex.Message);

        await Assert.ThrowsAsync<DomainException>(
            () => svc.RecordPaymentAsync(bill.Id, 0m, PaymentMethod.WeChat, DateTime.Today, null));
    }

    [Fact]
    public async Task DeletePayment_RollsBackStatus()
    {
        using var db = new TestDb();
        await SeedLeaseAsync(db, new DateTime(2026, 9, 1));
        var svc = new BillingService(db.Db);
        await svc.EnsureBillsThroughAsync(new DateTime(2026, 9, 1));
        var bill = await db.Db.Bills.SingleAsync();

        var pay = await svc.RecordPaymentAsync(bill.Id, 1500m, PaymentMethod.Bank, DateTime.Today, null);
        Assert.Equal(BillStatus.Paid, (await db.Db.Bills.SingleAsync()).Status);

        await svc.DeletePaymentAsync(pay.Id);
        var after = await db.Db.Bills.Include(b => b.Payments).SingleAsync();
        Assert.Equal(BillStatus.Unpaid, after.Status);
        Assert.Equal(0m, after.PaidAmount);
        Assert.Empty(after.Payments);
    }

    [Fact]
    public async Task ExtraFee_RecalculatesTotal()
    {
        using var db = new TestDb();
        await SeedLeaseAsync(db, new DateTime(2026, 9, 1));
        var svc = new BillingService(db.Db);
        await svc.EnsureBillsThroughAsync(new DateTime(2026, 9, 1));
        var bill = await db.Db.Bills.SingleAsync();

        db.Db.BillItems.Add(new BillItem { BillId = bill.Id, Name = "水费", Amount = 50m });
        db.Db.BillItems.Add(new BillItem { BillId = bill.Id, Name = "电费", Amount = 120m, Remark = "读数 233" });
        await db.Db.SaveChangesAsync();
        await svc.RecalculateBillAsync(bill.Id);

        var after = await db.Db.Bills.SingleAsync();
        Assert.Equal(1670m, after.TotalAmount);
        Assert.Equal(1670m, after.RemainingAmount);
    }

    [Fact]
    public async Task FeeCutBelowPaid_Rejected()
    {
        using var db = new TestDb();
        await SeedLeaseAsync(db, new DateTime(2026, 9, 1));
        var svc = new BillingService(db.Db);
        await svc.EnsureBillsThroughAsync(new DateTime(2026, 9, 1));
        var bill = await db.Db.Bills.SingleAsync();

        // 收全款后，删掉租金外的费用场景：直接改租金模拟总额下调
        await svc.RecordPaymentAsync(bill.Id, 1500m, PaymentMethod.Cash, DateTime.Today, null);
        var tracked = await db.Db.Bills.Include(b => b.Items).Include(b => b.Payments).SingleAsync();
        tracked.RentAmount = 1000m; // 已收 1500 > 新总额 1000 → 应拒绝
        Assert.Throws<DomainException>(() => BillingService.Recalculate(tracked));
    }

    [Fact]
    public async Task BatchSettle_SettlesAllUnpaidBills_Fully()
    {
        using var db = new TestDb();
        await SeedLeaseAsync(db, new DateTime(2026, 9, 1));
        var svc = new BillingService(db.Db);
        await svc.EnsureBillsThroughAsync(new DateTime(2026, 10, 1)); // 9 / 10 两张
        var ids = await db.Db.Bills.Select(b => b.Id).ToListAsync();

        var (settled, total) = await svc.BatchSettleAsync(ids, PaymentMethod.WeChat, new DateTime(2026, 10, 10));

        Assert.Equal(2, settled);
        Assert.Equal(3000m, total);
        var bills = await db.Db.Bills.Include(b => b.Payments).ToListAsync();
        Assert.All(bills, b => Assert.Equal(BillStatus.Paid, b.Status));
        Assert.All(bills, b => Assert.Equal(b.TotalAmount, b.PaidAmount));
        Assert.All(bills, b =>
        {
            var pay = Assert.Single(b.Payments);
            Assert.Equal("批量结清", pay.Note);
            Assert.Equal(PaymentMethod.WeChat, pay.Method);
        });
    }

    [Fact]
    public async Task BatchSettle_SkipsSettled_AndTopsUpPartial()
    {
        using var db = new TestDb();
        await SeedLeaseAsync(db, new DateTime(2026, 9, 1));
        var svc = new BillingService(db.Db);
        await svc.EnsureBillsThroughAsync(new DateTime(2026, 10, 1)); // 9 / 10 两张
        var bills = await db.Db.Bills.OrderBy(b => b.PeriodStart).ToListAsync();

        await svc.RecordPaymentAsync(bills[0].Id, 1500m, PaymentMethod.Cash, DateTime.Today, null); // 已结清
        await svc.RecordPaymentAsync(bills[1].Id, 500m, PaymentMethod.Cash, DateTime.Today, null);  // 部分已付

        var (settled, total) = await svc.BatchSettleAsync(
            bills.Select(b => b.Id), PaymentMethod.Bank, DateTime.Today);

        Assert.Equal(1, settled);           // 已结清的自动跳过
        Assert.Equal(1000m, total);         // 部分已付的只补剩余
        var after = await db.Db.Bills.OrderBy(b => b.PeriodStart).ToListAsync();
        Assert.All(after, b => Assert.Equal(BillStatus.Paid, b.Status));
    }

    [Fact]
    public async Task BatchSettle_EmptyOrUnknownIds_Noop()
    {
        using var db = new TestDb();
        await SeedLeaseAsync(db, new DateTime(2026, 9, 1));
        var svc = new BillingService(db.Db);
        await svc.EnsureBillsThroughAsync(new DateTime(2026, 9, 1));

        var (settled, total) = await svc.BatchSettleAsync(new[] { 999, 999 }, PaymentMethod.Cash, DateTime.Today);

        Assert.Equal(0, settled);
        Assert.Equal(0m, total);
        Assert.Equal(0, await db.Db.Payments.CountAsync());
    }
}
