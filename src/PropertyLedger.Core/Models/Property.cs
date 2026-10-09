namespace PropertyLedger.Core.Models;

/// <summary>物业（楼盘 / 楼栋 / 院落），一个房东可登记多个物业。</summary>
public class Property
{
    public int Id { get; set; }

    /// <summary>物业名称，如「阳光花园」「春熙路自建房」</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>详细地址（选填）</summary>
    public string? Address { get; set; }

    /// <summary>备注</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // 导航
    public List<Room> Rooms { get; set; } = new();
}
