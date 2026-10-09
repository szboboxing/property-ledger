namespace PropertyLedger.Core.Models;

/// <summary>物业（楼盘 / 楼栋 / 院落），一个房东可登记多个物业。</summary>
public class Property
{
    public int Id { get; set; }

    /// <summary>物业名称，如「阳光花园」「春熙路自建房」</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>详细地址（选填）</summary>
    public string? Address { get; set; }

    /// <summary>物业类型：房屋 / 商铺 / 公寓</summary>
    public PropertyType Type { get; set; } = PropertyType.House;

    /// <summary>管理处联系方式（电话等）</summary>
    public string? ManagementContact { get; set; }

    /// <summary>水电燃气联系方式</summary>
    public string? UtilityContact { get; set; }

    /// <summary>备注（支持富文本/图片，HTML 存储）</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // 导航
    public List<Room> Rooms { get; set; } = new();
}
