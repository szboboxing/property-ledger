using PropertyLedger.Core.Models;

namespace PropertyLedger.Tests;

/// <summary>用户功能权限：CSV 归一化与 HasFeature 判定。</summary>
public class UserFeatureTests
{
    [Fact]
    public void Normalize_FiltersInvalidKeys_Dedupes_OrdersByDefinition()
    {
        var csv = UserFeatures.Normalize(" settings , bills, hack, bills, leases ");
        Assert.Equal("bills,leases,settings", csv);
    }

    [Fact]
    public void Normalize_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Equal("", UserFeatures.Normalize(null));
        Assert.Equal("", UserFeatures.Normalize("   "));
        Assert.Equal("", UserFeatures.Normalize("nope,xx"));
    }

    [Fact]
    public void HasFeature_Admin_AlwaysTrue()
    {
        var admin = new AppUser { IsAdmin = true, Features = null };
        Assert.True(admin.HasFeature("bills"));
        Assert.True(admin.HasFeature("settings"));
    }

    [Fact]
    public void HasFeature_NormalUser_FollowsCsv()
    {
        var u = new AppUser { IsAdmin = false, Features = "bills,leases" };
        Assert.True(u.HasFeature("bills"));
        Assert.True(u.HasFeature("leases"));
        Assert.False(u.HasFeature("users"));
        Assert.False(u.HasFeature("settings"));
    }

    [Fact]
    public void HasFeature_NormalUser_NullFeatures_NothingAllowed()
    {
        var u = new AppUser { IsAdmin = false, Features = null };
        Assert.False(u.HasFeature("bills"));
    }

    [Fact]
    public void Parse_And_Names_RoundTrip()
    {
        var keys = UserFeatures.Parse("leases,bills");
        Assert.Equal(new[] { "bills", "leases" }, keys); // 按 All 定义顺序
        var names = UserFeatures.Names("leases,bills");
        Assert.Equal(new[] { "账单记账", "租约" }, names);
    }
}
