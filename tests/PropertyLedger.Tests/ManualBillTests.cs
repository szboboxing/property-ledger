using Microsoft.EntityFrameworkCore;
using PropertyLedger.Core.Models;
using PropertyLedger.Core.Services;

namespace PropertyLedger.Tests;

/// <summary>补录过往账单：正常补录、重复拒绝、租期外拒绝、已退租可补录、金额覆盖。</summary>
public class ManualBillTests
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

    [Fact]
    public async Task ManualBill_DefaultAmount_UsesLeaseRent()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1), new DateTime(2026, 6, 30));
        var svc = new BillingService(db.Db);

        var bill = await svc.CreateManualBillAsync(lease.Id, 2026, 3, null, "接手前旧账");

        Assert.True(bill.IsManual);
        Assert.Equal(new DateTime(2026, 3, 1), bill.PeriodStart);
        Assert.Equal(new DateTime(2026, 3, 31), bill.PeriodEnd);
        Assert.Equal(1500m, bill.TotalAmount);
        Assert.Equal(0m, bill.PaidAmount);
        Assert.Equal(BillStatus.Unpaid, bill.Status);
        Assert.Equal("接手前旧账", bill.Note);
    }

    [Fact]
    public async Task ManualBill_CustomAmount_Applies()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var svc = new BillingService(db.Db);

        var bill = await svc.CreateManualBillAsync(lease.Id, 2026, 2, 800m, null);
        Assert.Equal(800m, bill.RentAmount);
        Assert.Equal(800m, bill.TotalAmount);
    }

    [Fact]
    public async Task ManualBill_DuplicatePeriod_Rejected()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var svc = new BillingService(db.Db);
        await svc.CreateManualBillAsync(lease.Id, 2026, 3, null, null);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => svc.CreateManualBillAsync(lease.Id, 2026, 3, null, null));
        Assert.Contains("已存在账单", ex.Message);
    }

    [Fact]
    public async Task ManualBill_OutsideLeasePeriod_Rejected()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1), new DateTime(2026, 6, 30));
        var svc = new BillingService(db.Db);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => svc.CreateManualBillAsync(lease.Id, 2026, 8, null, null));
        Assert.Contains("不在该租约的租期内", ex.Message);

        var ex2 = await Assert.ThrowsAsync<DomainException>(
            () => svc.CreateManualBillAsync(lease.Id, 2025, 12, null, null));
        Assert.Contains("不在该租约的租期内", ex2.Message);
    }

    [Fact]
    public async Task ManualBill_EndedLease_StillAllowed()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1), new DateTime(2026, 6, 30));
        lease.Status = LeaseStatus.Ended;
        await db.Db.SaveChangesAsync();
        var svc = new BillingService(db.Db);

        // 已退租的租约仍可补录租期内历史月份（记录旧欠款）
        var bill = await svc.CreateManualBillAsync(lease.Id, 2026, 5, null, null);
        Assert.True(bill.IsManual);
        Assert.Equal(new DateTime(2026, 5, 1), bill.PeriodStart);
    }

    [Fact]
    public async Task ManualBill_ZeroAmount_Rejected()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var svc = new BillingService(db.Db);

        await Assert.ThrowsAsync<DomainException>(
            () => svc.CreateManualBillAsync(lease.Id, 2026, 3, 0m, null));
    }

    [Fact]
    public async Task ManualRange_MultiMonths_CreatesAll()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
        var svc = new BillingService(db.Db);

        var result = await svc.CreateManualBillsRangeAsync(lease.Id, 2026, 1, 2026, 3, null, "接手旧账");

        Assert.Equal(3, result.Created);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.OutOfRange);
        Assert.All(result.CreatedBills, b => { Assert.True(b.IsManual); Assert.Equal(1500m, b.TotalAmount); });
        Assert.Equal(new[] { "2026-01", "2026-02", "2026-03" },
            result.CreatedBills.Select(b => b.PeriodStart.ToString("yyyy-MM")).ToArray());
    }

    [Fact]
    public async Task ManualRange_SkipsExisting_AndCountsOutOfRange()
    {
        using var db = new TestDb();
        // 租约 2026-02-01 起：1 月在租期外；先自动出账 2 月
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 2, 1), new DateTime(2026, 12, 31));
        var svc = new BillingService(db.Db);
        await svc.CreateManualBillAsync(lease.Id, 2026, 2, null, null);

        var result = await svc.CreateManualBillsRangeAsync(lease.Id, 2026, 1, 2026, 4, null, null);

        Assert.Equal(2, result.Created);        // 3 月、4 月新建
        Assert.Equal(1, result.Skipped);        // 2 月已有账单
        Assert.Equal(1, result.OutOfRange);     // 1 月不在租期内
    }

    [Fact]
    public async Task ManualRange_InvertedRange_Rejected()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1));
        var svc = new BillingService(db.Db);

        await Assert.ThrowsAsync<DomainException>(
            () => svc.CreateManualBillsRangeAsync(lease.Id, 2026, 3, 2026, 1, null, null));
    }

    [Fact]
    public async Task ManualRange_PerBillAmount_AppliesToEach()
    {
        using var db = new TestDb();
        var lease = await SeedLeaseAsync(db, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
        var svc = new BillingService(db.Db);

        var result = await svc.CreateManualBillsRangeAsync(lease.Id, 2026, 5, 2026, 6, 800m, null);
        Assert.Equal(2, result.Created);
        Assert.All(result.CreatedBills, b => Assert.Equal(800m, b.TotalAmount));
    }
}

/// <summary>HTML 摘要：剥标签、去脚本、块级转空格、截断。</summary>
public class HtmlTextTests
{
    [Fact]
    public void Excerpt_StripsTagsAndDecodes()
    {
        var html = "<b>你好</b><img src=\"/uploads/a.png\"><span>世界 &amp; 同事</span>";
        Assert.Equal("你好世界 & 同事", HtmlText.Excerpt(html));
    }

    [Fact]
    public void Excerpt_RemovesScriptAndStyle()
    {
        Assert.Equal("内容", HtmlText.Excerpt("<script>alert(1)</script>内容"));
        Assert.Equal("内容", HtmlText.Excerpt("<style>p{}</style>内容"));
    }

    [Fact]
    public void Excerpt_BlockTagsBecomeSpaces()
    {
        Assert.Equal("行一 行二", HtmlText.Excerpt("<p>行一</p><p>行二</p>"));
        Assert.Equal("a b", HtmlText.Excerpt("a<br>b"));
    }

    [Fact]
    public void Excerpt_TruncatesWithEllipsis()
    {
        var result = HtmlText.Excerpt(new string('x', 100), 40);
        Assert.Equal(41, result.Length); // 40 + 省略号
        Assert.EndsWith("…", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Excerpt_Empty(string? input) => Assert.Equal(string.Empty, HtmlText.Excerpt(input));
}

/// <summary>报表功能注册。</summary>
public class ReportsFeatureTests
{
    [Fact]
    public void UserFeatures_ContainsReports()
    {
        Assert.Contains(("reports", "报表"), UserFeatures.All);
        Assert.True(UserFeatures.IsValidKey("reports"));
        Assert.Equal("bills,reports", UserFeatures.Normalize("reports, bills, bogus"));
    }
}

/// <summary>清空业务数据：按外键顺序删除，保留用户与设置。</summary>
public class MaintenanceTests
{
    [Fact]
    public async Task WipeBusinessData_RemovesAllBusinessRows_KeepsUsersAndSettings()
    {
        using var db = new TestDb();
        var svc = new BillingService(db.Db);

        var lease = new Lease
        {
            Room = new Room { Property = new Property { Name = "幸福小区" }, Name = "101", DefaultRent = 1500m },
            Tenant = new Tenant { Name = "张三" },
            StartDate = new DateTime(2026, 1, 1),
            RentAmount = 1500m, HouseDeposit = 1500m,
            PaymentDay = 5, PaymentCycle = PaymentCycle.Monthly,
            Status = LeaseStatus.Active,
        };
        db.Db.Leases.Add(lease);
        await db.Db.SaveChangesAsync();
        await svc.EnsureBillsThroughAsync(new DateTime(2026, 1, 1));
        var bill = await db.Db.Bills.Include(b => b.Payments).FirstAsync(b => b.LeaseId == lease.Id);
        await svc.RecordPaymentAsync(bill.Id, 500m, PaymentMethod.Cash, DateTime.Now, null);
        db.Db.Users.Add(new AppUser { UserName = "wipeadmin", PasswordHash = "x", IsAdmin = true });
        db.Db.Settings.Add(new Setting { Key = "landlord_name", Value = "王女士" });
        await db.Db.SaveChangesAsync();

        Assert.True(await db.Db.Bills.AnyAsync());

        var maint = new MaintenanceService(db.Db);
        var (payments, items, bills, leases, rooms, tenants, properties) = await maint.WipeBusinessDataAsync();

        Assert.Equal(1, payments);
        Assert.Equal(0, items);
        Assert.Equal(1, bills);
        Assert.Equal(1, leases);
        Assert.Equal(1, rooms);
        Assert.Equal(1, tenants);
        Assert.Equal(1, properties);
        Assert.False(await db.Db.Bills.AnyAsync());
        // 用户与设置保留
        Assert.True(await db.Db.Users.AnyAsync(u => u.UserName == "wipeadmin"));
        Assert.True(await db.Db.Settings.AnyAsync(s => s.Key == "landlord_name"));
    }
}
