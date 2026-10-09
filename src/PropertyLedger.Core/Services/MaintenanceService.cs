using Microsoft.EntityFrameworkCore;
using PropertyLedger.Core.Data;

namespace PropertyLedger.Core.Services;

/// <summary>
/// 维护操作：清空业务数据（物业/房间/租客/租约/账单/收款）。
/// 只保留登录账号与基础设置（房东称呼、收款码等），供总管理员在新一批物业入账前重置台账。
/// 按外键依赖顺序批量删除（EF Core ExecuteDelete，单条 SQL/表，效率高）。
/// </summary>
public class MaintenanceService
{
    private readonly AppDbContext _db;
    public MaintenanceService(AppDbContext db) => _db = db;

    /// <summary>清空全部业务数据，返回各表删除的行数。</summary>
    public async Task<(long Payments, long BillItems, long Bills, long Leases, long Rooms, long Tenants, long Properties)>
        WipeBusinessDataAsync(CancellationToken ct = default)
    {
        var payments = await _db.Payments.ExecuteDeleteAsync(ct);
        var billItems = await _db.BillItems.ExecuteDeleteAsync(ct);
        var bills = await _db.Bills.ExecuteDeleteAsync(ct);
        var leases = await _db.Leases.ExecuteDeleteAsync(ct);
        var rooms = await _db.Rooms.ExecuteDeleteAsync(ct);
        var tenants = await _db.Tenants.ExecuteDeleteAsync(ct);
        var properties = await _db.Properties.ExecuteDeleteAsync(ct);
        return (payments, billItems, bills, leases, rooms, tenants, properties);
    }
}
