using Microsoft.EntityFrameworkCore;
using PropertyLedger.Core.Data;
using PropertyLedger.Core.Models;

namespace PropertyLedger.Core.Services;

/// <summary>
/// 经验库：签约（出租）/ 退租等场景的想法、经验与注意事项沉淀。
/// 内容为富文本 HTML；可按类别与关键字（标题或内容包含）检索。
/// </summary>
public class KnowledgeService
{
    private readonly AppDbContext _db;
    public KnowledgeService(AppDbContext db) => _db = db;

    /// <summary>按类别与关键字检索，最新在前。</summary>
    public async Task<List<KnowledgeEntry>> SearchAsync(string? keyword, KnowledgeCategory? category, CancellationToken ct = default)
    {
        var q = _db.Knowledge.AsNoTracking().AsQueryable();
        if (category is not null) q = q.Where(k => k.Category == category);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            q = q.Where(k => k.Title.Contains(kw) || (k.Content != null && k.Content.Contains(kw)));
        }
        return await q.OrderByDescending(k => k.CreatedAt).ThenByDescending(k => k.Id).ToListAsync(ct);
    }

    /// <summary>新增经验条目（标题必填，≤100 字）。</summary>
    public async Task<KnowledgeEntry> AddAsync(string? title, string? content, KnowledgeCategory category, int? leaseId = null, CancellationToken ct = default)
    {
        var entry = new KnowledgeEntry
        {
            Title = RequireTitle(title),
            Content = content,
            Category = category,
            LeaseId = leaseId,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };
        _db.Knowledge.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    /// <summary>更新经验条目（标题 / 内容 / 类别）。</summary>
    public async Task UpdateAsync(int id, string? title, string? content, KnowledgeCategory category, CancellationToken ct = default)
    {
        var entry = await _db.Knowledge.FindAsync(new object[] { id }, ct)
                    ?? throw new DomainException("经验条目不存在或已被删除。");
        entry.Title = RequireTitle(title);
        entry.Content = content;
        entry.Category = category;
        entry.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>删除经验条目；不存在时静默返回。</summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entry = await _db.Knowledge.FindAsync(new object[] { id }, ct);
        if (entry is null) return;
        _db.Knowledge.Remove(entry);
        await _db.SaveChangesAsync(ct);
    }

    private static string RequireTitle(string? title)
    {
        var t = (title ?? string.Empty).Trim();
        if (t.Length == 0) throw new DomainException("请填写经验标题。");
        if (t.Length > 100) throw new DomainException("标题不能超过 100 字。");
        return t;
    }
}
