using PropertyLedger.Core.Services;

namespace PropertyLedger.Tests;

/// <summary>PBKDF2 密码哈希：可校验、盐随机、错误密码拒绝、篡改拒绝。</summary>
public class PasswordHasherTests
{
    [Fact]
    public void HashThenVerify_Succeeds()
    {
        var hash = PasswordHasher.Hash("admin123");
        Assert.True(PasswordHasher.Verify("admin123", hash));
    }

    [Fact]
    public void WrongPassword_Rejected()
    {
        var hash = PasswordHasher.Hash("admin123");
        Assert.False(PasswordHasher.Verify("admin124", hash));
        Assert.False(PasswordHasher.Verify("", hash));
    }

    [Fact]
    public void SamePassword_DifferentSalts()
    {
        var h1 = PasswordHasher.Hash("same-password");
        var h2 = PasswordHasher.Hash("same-password");
        Assert.NotEqual(h1, h2); // 盐随机
        Assert.True(PasswordHasher.Verify("same-password", h1));
        Assert.True(PasswordHasher.Verify("same-password", h2));
    }

    [Fact]
    public void TamperedHash_Rejected()
    {
        var hash = PasswordHasher.Hash("admin123");
        var parts = hash.Split('.');
        Assert.Equal(3, parts.Length); // iterations.salt.hash 格式
        var tampered = $"{parts[0]}.{parts[1]}.{parts[2][..^2]}AA";
        Assert.False(PasswordHasher.Verify("admin123", tampered));
        Assert.False(PasswordHasher.Verify("admin123", "garbage"));
    }
}
