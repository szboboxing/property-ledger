using Microsoft.EntityFrameworkCore;
using PropertyLedger.Core.Models;
using PropertyLedger.Core.Services;

namespace PropertyLedger.Tests;

/// <summary>
/// 编辑租约关键字段后的账单重算：
/// 无收款自动账单重算/删除/补建；有收款与补录账单冻结；挂杂费的失效账单阻止。
/// </summary>
public class LeaseRecalcTests
{
    private static async Task<Lease> SeedLeaseAsync(
        TestDb db, DateTime start, decimal rent = 1000m, int paymentDay = 1,
        PaymentCycle cycle = PaymentCycle.Monthly, DateTime? end = null,
        int escMode = 0, decimal escValue = 0m)
    {
        var l = new Lease
        {
            Room = new Room { Property = new Property { Name = "幸福小区" }, Name = "101", DefaultRent = rent },
            Tenant = new Tenant { Name = "张三" },
            StartDate = start, EndDate = end,
            RentAmount = rent, PaymentDay = paymentDay, PaymentCycle = cycle,
            EscMode = escMode, EscValue = escValue,
            Status = LeaseStatus.Active,
        };
        db.Db.Leases.Add(l);
        await db.Db.SaveChangesAsync();
        return l;
    }

    private static async Task<Bill> SeedAutoBillAsync(
        TestDb db, Lease lease, int year, int month, decimal rent,
        bool manual = false)
    {
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(lease.PaymentCycle == PaymentCycle.Quarterly ? 2 : 0);
        var bill = new Bill
        {
            LeaseId = lease.Id, RoomId = lease.RoomId, TenantId = lease.TenantId,
            PeriodStart = start, PeriodEnd = new DateTime(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month)),
            DueDate = new DateTime(year, month, Math.Min(lease.PaymentDay, DateTime.DaysInMonth(year, month))),
            RentAmount = rent, TotalAmount = rent, PaidAmount = 0m,
            Status = BillStatus.Unpaid, IsManual = manual,
        };
        db.Db.Bills.Add(bill);
        await db.Db.SaveChangesAsync();
        return bill;
    }

    private static async Task SeedPaymentAsync(TestDb db, Bill bill, decimal amount)
    {
        db.Db.Payments.Add(new Payment
        {
            BillId = bill.Id, Amount = amount, PaidAt = bill.DueDate, Method = PaymentMethod.WeChat,
        });
        bill.PaidAmount = amount;
        bill.TotalAmount = bill.RentAmount;
        bill.Status = amount >= bill.TotalAmount - 0.001m ? BillStatus.Paid : BillStatus.Partial;
        if (bill.Status == BillStatus.Paid) bill.PaidAt = bill.DueDate;
        await db.Db.SaveChangesAsync();
    }

    private static async Task SeedItemAsync(TestDb db, Bill bill, decimal amount)
    {
        db.Db.BillItems.Add(new BillItem { BillId = bill.Id, Name = "水费", Amount = amount });
        bill.TotalAmount = bill.RentAmount + amount;
        await db.Db.SaveChangesAsync();
    }

    [Fact]
    public void IsBillAffectingChange_OnlyReactsToBillKeyFields()
    {
        var a = new Lease
        {
            StartDate = new DateTime(2026, 1, 1), EndDate = null,
            RentAmount = 1000m, EscMode = 0, EscValue = 0m,
            PaymentDay = 1, PaymentCycle = PaymentCycle.Monthly,
        };

        var harmless = new Lease
        {
            StartDate = a.StartDate, RentAmount = a.RentAmount, PaymentDay = 1,
            PaymentCycle = a.PaymentCycle,
            HouseDeposit = 500m, UtilityDeposit = 200m, Note = "改了备注",
            RoomId = 9, TenantId = 9,
        };
        Assert.False(BillingService.IsBillAffectingChange(a, harmless));

        Assert.True(ChangeDetected(a, l => l.RentAmount = 1200m));
        Assert.True(ChangeDetected(a, l => l.PaymentDay = 10));
        Assert.True(ChangeDetected(a, l => l.PaymentCycle = PaymentCycle.Quarterly));
        Assert.True(ChangeDetected(a, l => l.StartDate = new DateTime(2026, 2, 1)));
        Assert.True(ChangeDetected(a, l => l.EndDate = new DateTime(2026, 12, 31)));
    }

    /// <summary>仅复制参与重算判定的关键字段（其余默认）。</summary>
    private static Lease KeyClone(Lease l) => new()
    {
        StartDate = l.StartDate, EndDate = l.EndDate, RentAmount = l.RentAmount,
        EscMode = l.EscMode, EscValue = l.EscValue,
        PaymentDay = l.PaymentDay, PaymentCycle = l.PaymentCycle,
    };

    private static bool ChangeDetected(Lease a, Action<Lease> mutate)
    {
        var c = KeyClone(a);
        mutate(c);
        return BillingService.IsBillAffectingChange(a, c);
    }

    [Fact]
    public async Task RentChange_RecalculatesUnpaid_AndPreservesExtra()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var jan = await SeedAutoBillAsync(db, lease, 2026, 1, 1000m);
        var feb = await SeedAutoBillAsync(db, lease, 2026, 2, 1000m);
        await SeedItemAsync(db, feb, 50m);
        await SeedAutoBillAsync(db, lease, 2026, 3, 1000m);

        lease.RentAmount = 1200m;
        await db.Db.SaveChangesAsync();
        var result = await new BillingService(db.Db).ApplyLeaseRecalcAsync(lease.Id, new DateTime(2026, 3, 1));

        Assert.Equal(3, result.Recalculated);
        Assert.Equal(0, result.Deleted);
        Assert.Equal(0, result.Created);
        var j = await db.Db.Bills.Include(b => b.Items).FirstAsync(b => b.Id == jan.Id);
        Assert.Equal(1200m, j.RentAmount);
        Assert.Equal(1200m, j.TotalAmount);
        var f = await db.Db.Bills.Include(b => b.Items).FirstAsync(b => b.Id == feb.Id);
        Assert.Equal(1200m, f.RentAmount);
        Assert.Equal(1250m, f.TotalAmount);   // 杂费 50 保留
        Assert.Single(f.Items);
    }

    [Fact]
    public async Task PaidBill_IsFrozen_WhenRentRaised()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var jan = await SeedAutoBillAsync(db, lease, 2026, 1, 1000m);
        await SeedPaymentAsync(db, jan, 1000m);
        await SeedAutoBillAsync(db, lease, 2026, 2, 1000m);

        lease.RentAmount = 1200m;
        await db.Db.SaveChangesAsync();
        var result = await new BillingService(db.Db).ApplyLeaseRecalcAsync(lease.Id, new DateTime(2026, 2, 1));

        Assert.Equal(1, result.Frozen);
        Assert.Equal(1, result.Recalculated);
        var j = await db.Db.Bills.FirstAsync(b => b.Id == jan.Id);
        Assert.Equal(1000m, j.RentAmount);
        Assert.Equal(1000m, j.PaidAmount);
        Assert.Equal(BillStatus.Paid, j.Status);
    }

    [Fact]
    public async Task PartialPaidBill_IsFrozen()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var jan = await SeedAutoBillAsync(db, lease, 2026, 1, 1000m);
        await SeedPaymentAsync(db, jan, 400m);

        lease.RentAmount = 900m;   // 降租也不动有收款的账单
        await db.Db.SaveChangesAsync();
        var result = await new BillingService(db.Db).ApplyLeaseRecalcAsync(lease.Id, new DateTime(2026, 1, 1));

        var j = await db.Db.Bills.FirstAsync(b => b.Id == jan.Id);
        Assert.Equal(1000m, j.RentAmount);
        Assert.Equal(400m, j.PaidAmount);
        Assert.Equal(BillStatus.Partial, j.Status);
        Assert.Equal(1, result.Frozen);
        Assert.Equal(0, result.Recalculated);
    }

    [Fact]
    public async Task ManualBill_IsFrozen_EvenWhenUnpaid()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var jan = await SeedAutoBillAsync(db, lease, 2026, 1, 900m, manual: true);

        lease.RentAmount = 1200m;
        await db.Db.SaveChangesAsync();
        var result = await new BillingService(db.Db).ApplyLeaseRecalcAsync(lease.Id, new DateTime(2026, 1, 1));

        var j = await db.Db.Bills.FirstAsync(b => b.Id == jan.Id);
        Assert.Equal(900m, j.RentAmount);
        Assert.True(j.IsManual);
        Assert.Equal(1, result.Frozen);
    }

    [Fact]
    public async Task ShortenEndDate_DeletesUnpaidBillsOutsideRange()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1), end: new DateTime(2026, 6, 30));
        for (var m = 1; m <= 6; m++)
            await SeedAutoBillAsync(db, lease, 2026, m, 1000m);

        lease.EndDate = new DateTime(2026, 3, 31);
        await db.Db.SaveChangesAsync();
        var result = await new BillingService(db.Db).ApplyLeaseRecalcAsync(lease.Id, new DateTime(2026, 6, 1));

        Assert.Equal(3, result.Recalculated);   // 1-3 月仍匹配（金额不变也过一遍）
        Assert.Equal(3, result.Deleted);        // 4-6 月删除
        Assert.Equal(0, result.Created);
        var remain = await db.Db.Bills.CountAsync(b => b.LeaseId == lease.Id);
        Assert.Equal(3, remain);
    }

    [Fact]
    public async Task DeleteBlocked_WhenDriftingBillCarriesExtra()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1), end: new DateTime(2026, 6, 30));
        for (var m = 1; m <= 6; m++)
            await SeedAutoBillAsync(db, lease, 2026, m, 1000m);
        var apr = await db.Db.Bills.FirstAsync(b => b.PeriodStart == new DateTime(2026, 4, 1));
        await SeedItemAsync(db, apr, 88m);

        lease.EndDate = new DateTime(2026, 3, 31);
        await db.Db.SaveChangesAsync();
        var svc = new BillingService(db.Db);
        await Assert.ThrowsAsync<DomainException>(
            () => svc.ApplyLeaseRecalcAsync(lease.Id, new DateTime(2026, 6, 1)));

        // 整体未改动：账单仍 6 张、4 月杂费仍在
        Assert.Equal(6, await db.Db.Bills.CountAsync(b => b.LeaseId == lease.Id));
        Assert.Equal(88m, (await db.Db.BillItems.FirstAsync(i => i.BillId == apr.Id)).Amount);
    }

    [Fact]
    public async Task MonthlyToQuarterly_RealignsBillsWithoutDuplication()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        for (var m = 1; m <= 6; m++)
            await SeedAutoBillAsync(db, lease, 2026, m, 1000m);

        lease.PaymentCycle = PaymentCycle.Quarterly;
        await db.Db.SaveChangesAsync();
        var result = await new BillingService(db.Db).ApplyLeaseRecalcAsync(lease.Id, new DateTime(2026, 6, 1));

        Assert.Equal(2, result.Recalculated);   // 1 月、4 月账单升级为季账单
        Assert.Equal(4, result.Deleted);        // 2/3/5/6 月月账单删除
        Assert.Equal(0, result.Created);        // 不重复出账
        var bills = await db.Db.Bills.Where(b => b.LeaseId == lease.Id)
            .OrderBy(b => b.PeriodStart).ToListAsync();
        Assert.Equal(2, bills.Count);
        Assert.Equal(3000m, bills[0].RentAmount);
        Assert.Equal(new DateTime(2026, 3, 31), bills[0].PeriodEnd);
        Assert.Equal(3000m, bills[1].RentAmount);
        Assert.Equal(new DateTime(2026, 6, 30), bills[1].PeriodEnd);
    }

    [Fact]
    public async Task CycleSwitchWithPaidBill_DoesNotCreateOverlappingBills()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var jan = await SeedAutoBillAsync(db, lease, 2026, 1, 1000m);
        await SeedPaymentAsync(db, jan, 1000m);
        for (var m = 2; m <= 6; m++)
            await SeedAutoBillAsync(db, lease, 2026, m, 1000m);

        lease.PaymentCycle = PaymentCycle.Quarterly;
        await db.Db.SaveChangesAsync();
        var result = await new BillingService(db.Db).ApplyLeaseRecalcAsync(lease.Id, new DateTime(2026, 6, 1));

        Assert.Equal(0, result.Created);
        var janNow = await db.Db.Bills.FirstAsync(b => b.Id == jan.Id);
        Assert.Equal(1000m, janNow.RentAmount);   // 1 月已收冻结
    }

    [Fact]
    public async Task StartDateMovedEarlier_CreatesMissingBills()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 3, 1));
        await SeedAutoBillAsync(db, lease, 2026, 3, 1000m);

        lease.StartDate = new DateTime(2026, 1, 1);
        await db.Db.SaveChangesAsync();
        var result = await new BillingService(db.Db).ApplyLeaseRecalcAsync(lease.Id, new DateTime(2026, 3, 1));

        Assert.Equal(2, result.Created);
        var periods = await db.Db.Bills.Where(b => b.LeaseId == lease.Id)
            .Select(b => b.PeriodStart).OrderBy(d => d).ToListAsync();
        Assert.Equal(new DateTime(2026, 1, 1), periods[0]);
        Assert.Equal(new DateTime(2026, 2, 1), periods[1]);
        Assert.Equal(new DateTime(2026, 3, 1), periods[2]);
    }

    [Fact]
    public async Task PaymentDayChange_UpdatesDueDatesOfUnpaid()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1), paymentDay: 1);
        var jan = await SeedAutoBillAsync(db, lease, 2026, 1, 1000m);
        var feb = await SeedAutoBillAsync(db, lease, 2026, 2, 1000m);
        await SeedPaymentAsync(db, feb, 1000m);

        lease.PaymentDay = 10;
        await db.Db.SaveChangesAsync();
        await new BillingService(db.Db).ApplyLeaseRecalcAsync(lease.Id, new DateTime(2026, 2, 1));

        var j = await db.Db.Bills.FirstAsync(b => b.Id == jan.Id);
        Assert.Equal(new DateTime(2026, 1, 10), j.DueDate);
        var f = await db.Db.Bills.FirstAsync(b => b.Id == feb.Id);
        Assert.Equal(new DateTime(2026, 2, 1), f.DueDate);   // 已收款冻结
    }

    [Fact]
    public async Task EscalationChange_RecalculatesYearTwoRent()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2025, 1, 1));
        await SeedAutoBillAsync(db, lease, 2025, 12, 1000m);
        await SeedAutoBillAsync(db, lease, 2026, 1, 1000m);

        lease.EscMode = 2;
        lease.EscValue = 100m;
        await db.Db.SaveChangesAsync();
        await new BillingService(db.Db).ApplyLeaseRecalcAsync(lease.Id, new DateTime(2026, 1, 1));

        var y1 = await db.Db.Bills.FirstAsync(b => b.PeriodStart == new DateTime(2025, 12, 1));
        var y2 = await db.Db.Bills.FirstAsync(b => b.PeriodStart == new DateTime(2026, 1, 1));
        Assert.Equal(1000m, y1.RentAmount);   // 租约第 1 年不递增
        Assert.Equal(1100m, y2.RentAmount);
    }

    [Fact]
    public async Task RoomChange_SyncsRedundantFields_OnFrozenBills()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var jan = await SeedAutoBillAsync(db, lease, 2026, 1, 1000m);
        await SeedPaymentAsync(db, jan, 1000m);

        var room2 = new Room { Property = new Property { Name = "幸福小区" }, Name = "202", DefaultRent = 2000m };
        db.Db.Rooms.Add(room2);
        await db.Db.SaveChangesAsync();

        lease.RoomId = room2.Id;
        await db.Db.SaveChangesAsync();
        await new BillingService(db.Db).ApplyLeaseRecalcAsync(lease.Id, new DateTime(2026, 1, 1));

        var j = await db.Db.Bills.FirstAsync(b => b.Id == jan.Id);
        Assert.Equal(room2.Id, j.RoomId);
        Assert.Equal(1000m, j.RentAmount);   // 钱不动
    }
}
