namespace PropertyLedger.Core.Models;

/// <summary>
/// 经验库条目：签约（出租）/ 退租等场景的想法、经验与注意事项沉淀。
/// 内容为富文本 HTML（与各模块备注同一套编辑器与图片上传通道），可选关联一份租约。
/// </summary>
public class KnowledgeEntry
{
    public int Id { get; set; }

    /// <summary>标题（必填，≤100 字）</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>内容（富文本 HTML）</summary>
    public string? Content { get; set; }

    /// <summary>类别：出租经验 / 退租经验 / 其他</summary>
    public KnowledgeCategory Category { get; set; } = KnowledgeCategory.Rental;

    /// <summary>可选关联的租约；租约删除后置空</summary>
    public int? LeaseId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    // 导航
    public Lease? Lease { get; set; }
}
