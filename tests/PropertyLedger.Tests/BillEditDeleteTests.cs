using Microsoft.EntityFrameworkCore;
using PropertyLedger.Core.Models;
using PropertyLedger.Core.Services;

namespace PropertyLedger.Tests;

/// <summary>账单编辑：改租金/备注后自动重算总额与状态，越界（低于已收 / 负数）拒绝。</summary>
public class BillUpdateTests
{
    private static async Task<Lease> SeedLeaseAsync(TestDb db, DateTime start, DateTime? end = null)
    {
        var l = new Lease
        {
            Room = new Room { Property = new Property { Name = "幸福小区" }, Name = "101", DefaultRent = 1500m },
            Tenant = new Tenant { Name = "张三" },
            StartDate = start,
            EndDate = end,
            RentAmount = 1500m, HouseDeposit = 1500m,
            PaymentDay = 5, PaymentCycle = PaymentCycle.Monthly,
            Status = LeaseStatus.Active,
        };
        db.Db.Leases.Add(l);
        await db.Db.SaveChangesAsync();
        return l;
    }

    private static async Task<Bill> SeedManualBillAsync(TestDb db, BillingService svc, Lease lease)
    {
        var bill = await svc.CreateManualBillAsync(lease.Id, 2026, 3, 1500m, null);
        db.Db.BillItems.Add(new BillItem { BillId = bill.Id, Name = "水费", Amount = 100m });
        await db.Db.SaveChangesAsync();
        await svc.RecalculateBillAsync(bill.Id);
        return await db.Db.Bills.Include(b => b.Items).FirstAsync(b => b.Id == bill.Id);
    }

    [Fact]
    public async Task UpdateBill_ChangesRentAndNote_Recalculates()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var svc = new BillingService(db.Db);
        var bill = await SeedManualBillAsync(db, svc, lease);
        Assert.Equal(1600m, bill.TotalAmount); // 1500 租金 + 100 水费

        await svc.UpdateBillAsync(bill.Id, 1200m, "  本月优惠  ");

        var reloaded = await db.Db.Bills.Include(b => b.Items).FirstAsync(b => b.Id == bill.Id);
        Assert.Equal(1200m, reloaded.RentAmount);
        Assert.Equal("本月优惠", reloaded.Note);   // 去首尾空白
        Assert.Equal(1300m, reloaded.TotalAmount); // 1200 + 100 水费
        Assert.Equal(0m, reloaded.PaidAmount);
        Assert.Equal(BillStatus.Unpaid, reloaded.Status);
    }

    [Fact]
    public async Task UpdateBill_FreeRent_ZeroTotal()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var svc = new BillingService(db.Db);
        var bill = await SeedManualBillAsync(db, svc, lease);

        await svc.UpdateBillAsync(bill.Id, 0m, null);

        var reloaded = await db.Db.Bills.FirstAsync(b => b.Id == bill.Id);
        Assert.Equal(100m, reloaded.TotalAmount); // 仅剩杂费
        Assert.Null(reloaded.Note);
        Assert.Equal(BillStatus.Unpaid, reloaded.Status);
    }

    [Fact]
    public async Task UpdateBill_RentBelowPaid_Throws()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var svc = new BillingService(db.Db);
        var bill = await SeedManualBillAsync(db, svc, lease);
        await svc.RecordPaymentAsync(bill.Id, 1600m, PaymentMethod.Cash, DateTime.Now, null);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => svc.UpdateBillAsync(bill.Id, 1000m, null));
        Assert.Contains("已收金额超过应收总额", ex.Message);

        // 已收 1600：应收必须 ≥ 1600，1500 仍不足（含 100 杂费）
        await Assert.ThrowsAsync<DomainException>(() => svc.UpdateBillAsync(bill.Id, 1400m, null));

        // 足额时允许编辑，状态保持已结清
        await svc.UpdateBillAsync(bill.Id, 1500m, "调整说明");
        var reloaded = await db.Db.Bills.FirstAsync(b => b.Id == bill.Id);
        Assert.Equal(1600m, reloaded.TotalAmount);
        Assert.Equal(BillStatus.Paid, reloaded.Status);
    }

    [Fact]
    public async Task UpdateBill_NegativeRent_Throws()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var svc = new BillingService(db.Db);
        var bill = await SeedManualBillAsync(db, svc, lease);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => svc.UpdateBillAsync(bill.Id, -1m, null));
        Assert.Contains("不能为负数", ex.Message);
    }

    [Fact]
    public async Task UpdateBill_Nonexistent_Throws()
    {
        using var db = new TestDb();
        var svc = new BillingService(db.Db);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => svc.UpdateBillAsync(999, 100m, null));
        Assert.Contains("账单不存在", ex.Message);
    }
}

/// <summary>账单删除：无收款可删（含杂费明细）、有收款禁止、不存在抛错。</summary>
public class BillDeleteTests
{
    private static async Task<Lease> SeedLeaseAsync(TestDb db, DateTime start, DateTime? end = null)
    {
        var l = new Lease
        {
            Room = new Room { Property = new Property { Name = "幸福小区" }, Name = "101", DefaultRent = 1500m },
            Tenant = new Tenant { Name = "张三" },
            StartDate = start,
            EndDate = end,
            RentAmount = 1500m, HouseDeposit = 1500m,
            PaymentDay = 5, PaymentCycle = PaymentCycle.Monthly,
            Status = LeaseStatus.Active,
        };
        db.Db.Leases.Add(l);
        await db.Db.SaveChangesAsync();
        return l;
    }

    private static async Task<Bill> SeedManualBillAsync(TestDb db, BillingService svc, Lease lease)
    {
        var bill = await svc.CreateManualBillAsync(lease.Id, 2026, 3, 1500m, null);
        db.Db.BillItems.Add(new BillItem { BillId = bill.Id, Name = "水费", Amount = 100m });
        await db.Db.SaveChangesAsync();
        await svc.RecalculateBillAsync(bill.Id);
        return bill;
    }

    [Fact]
    public async Task DeleteBill_NoPayments_RemovesBillAndItems()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var svc = new BillingService(db.Db);
        var bill = await SeedManualBillAsync(db, svc, lease);

        await svc.DeleteBillAsync(bill.Id);

        Assert.False(await db.Db.Bills.AnyAsync(b => b.Id == bill.Id));
        Assert.False(await db.Db.BillItems.AnyAsync(i => i.BillId == bill.Id));
    }

    [Fact]
    public async Task DeleteBill_WithPayments_Throws()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var svc = new BillingService(db.Db);
        var bill = await SeedManualBillAsync(db, svc, lease);
        await svc.RecordPaymentAsync(bill.Id, 500m, PaymentMethod.Cash, DateTime.Now, null);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => svc.DeleteBillAsync(bill.Id));
        Assert.Contains("请先撤销全部收款", ex.Message);

        // 撤销收款后可删除
        var paymentId = await db.Db.Payments.Select(p => p.Id).SingleAsync();
        await svc.DeletePaymentAsync(paymentId);
        await svc.DeleteBillAsync(bill.Id);
        Assert.False(await db.Db.Bills.AnyAsync(b => b.Id == bill.Id));
    }

    [Fact]
    public async Task DeleteBill_Nonexistent_Throws()
    {
        using var db = new TestDb();
        var svc = new BillingService(db.Db);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => svc.DeleteBillAsync(999));
        Assert.Contains("账单不存在", ex.Message);
    }
}
