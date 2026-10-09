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
            RentAmount = 1500m, Deposit = 1500m,
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
