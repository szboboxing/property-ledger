using PropertyLedger.Core.Services;

namespace PropertyLedger.Tests;

/// <summary>设置服务：电话脱敏与默认收款提示语。</summary>
public class SettingsMaskTests
{
    [Theory]
    [InlineData("13812345678", "1381***5678")]   // 11 位手机号：首尾各 4 位，中间 ***
    [InlineData("028-12345678", "028-***5678")]  // 带区号横杠同样按位置遮盖
    [InlineData(" 13812345678 ", "1381***5678")] // 首尾空白先裁剪再脱敏
    [InlineData("12345678", "12345678")]         // 8 位座机：长度不足 9 位原样保留
    [InlineData("12345", "12345")]               // 过短原样保留
    [InlineData("", "")]
    public void MaskPhone_KeepsFirstAndLast4(string input, string expected)
        => Assert.Equal(expected, SettingsService.MaskPhone(input));

    [Fact]
    public void MaskPhone_Null_ReturnsEmpty()
        => Assert.Equal("", SettingsService.MaskPhone(null));

    [Fact]
    public void DefaultPaymentNotice_NotEmpty()
        => Assert.False(string.IsNullOrWhiteSpace(SettingsService.DefaultPaymentNotice));

    [Fact]
    public void PaymentNoticePresets_ContainDefault()
        => Assert.Contains(SettingsService.DefaultPaymentNotice, SettingsService.PaymentNoticePresets);
}
