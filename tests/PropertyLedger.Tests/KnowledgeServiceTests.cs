using Microsoft.EntityFrameworkCore;
using PropertyLedger.Core.Models;
using PropertyLedger.Core.Services;

namespace PropertyLedger.Tests;

/// <summary>经验库：标题校验、增改删、按类别与关键字检索。</summary>
public class KnowledgeServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly KnowledgeService Svc;

    public KnowledgeServiceTests() => Svc = new KnowledgeService(_db.Db);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Add_RequiresTitle()
    {
        await Assert.ThrowsAsync<DomainException>(
            () => Svc.AddAsync("   ", null, KnowledgeCategory.Rental));
        await Assert.ThrowsAsync<DomainException>(
            () => Svc.AddAsync(null, null, KnowledgeCategory.Rental));
    }

    [Fact]
    public async Task Add_TitleTooLong_Throws()
    {
        await Assert.ThrowsAsync<DomainException>(
            () => Svc.AddAsync(new string('字', 101), null, KnowledgeCategory.Rental));
    }

    [Fact]
    public async Task Add_TrimsTitle_And_Roundtrips()
    {
        var entry = await Svc.AddAsync("  商铺签约经验  ", "<p>确认免租期</p>", KnowledgeCategory.Rental);
        Assert.Equal("商铺签约经验", entry.Title);
        Assert.Equal("<p>确认免租期</p>", entry.Content);
        Assert.Equal(KnowledgeCategory.Rental, entry.Category);

        await Svc.UpdateAsync(entry.Id, "改名", "<p>新内容</p>", KnowledgeCategory.Termination);
        var reloaded = await _db.Db.Knowledge.SingleAsync(k => k.Id == entry.Id);
        Assert.Equal("改名", reloaded.Title);
        Assert.Equal(KnowledgeCategory.Termination, reloaded.Category);
        Assert.True(reloaded.UpdatedAt >= reloaded.CreatedAt);
    }

    [Fact]
    public async Task Search_FiltersByCategoryAndKeyword()
    {
        await Svc.AddAsync("押金经验", "<p>水电押金要写清退还条件</p>", KnowledgeCategory.Rental);
        await Svc.AddAsync("退租教训", "<p>提前 30 天书面通知</p>", KnowledgeCategory.Termination);
        await Svc.AddAsync("杂物", null, KnowledgeCategory.Other);

        var all = await Svc.SearchAsync(null, null);
        Assert.Equal(3, all.Count);

        var rental = await Svc.SearchAsync(null, KnowledgeCategory.Rental);
        Assert.Single(rental);
        Assert.Equal("押金经验", rental[0].Title);

        var byKw = await Svc.SearchAsync("水电押金", null); // 命中富文本正文
        Assert.Single(byKw);
        Assert.Equal("押金经验", byKw[0].Title);

        var byTitleKw = await Svc.SearchAsync("教训", null);
        Assert.Single(byTitleKw);
        Assert.Equal("退租教训", byTitleKw[0].Title);

        var combo = await Svc.SearchAsync("通知", KnowledgeCategory.Termination);
        Assert.Single(combo);
        var comboMiss = await Svc.SearchAsync("通知", KnowledgeCategory.Rental);
        Assert.Empty(comboMiss);
    }

    [Fact]
    public async Task Search_NewestFirst()
    {
        var a = await Svc.AddAsync("第一条", null, KnowledgeCategory.Other);
        var b = await Svc.AddAsync("第二条", null, KnowledgeCategory.Other);
        var list = await Svc.SearchAsync(null, null);
        Assert.Equal(new[] { b.Id, a.Id }, list.Select(k => k.Id));
    }

    [Fact]
    public async Task Delete_Removes_And_MissingIsSilent()
    {
        var entry = await Svc.AddAsync("待删除", null, KnowledgeCategory.Other);
        await Svc.DeleteAsync(entry.Id);
        Assert.False(await _db.Db.Knowledge.AnyAsync(k => k.Id == entry.Id));
        await Svc.DeleteAsync(99999); // 不存在 → 静默
    }

    [Fact]
    public async Task Update_Missing_Throws()
    {
        await Assert.ThrowsAsync<DomainException>(
            () => Svc.UpdateAsync(99999, "t", null, KnowledgeCategory.Other));
    }
}
