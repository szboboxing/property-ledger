using Microsoft.EntityFrameworkCore;
using PropertyLedger.Core.Data;
using PropertyLedger.Core.Models;

namespace PropertyLedger.Core.Services;

/// <summary>首次启动初始化：建库 + 创建默认管理员（admin / admin123，首次登录请改密）。</summary>
public static class DbSeeder
{
    public const string DefaultAdmin = "admin";
    public const string DefaultPassword = "admin123";

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (!await db.Users.AnyAsync(ct))
        {
            db.Users.Add(new AppUser
            {
                UserName = DefaultAdmin,
                DisplayName = "管理员",
                PasswordHash = PasswordHasher.Hash(DefaultPassword),
                IsAdmin = true,
                IsActive = true,
            });
            await db.SaveChangesAsync(ct);
        }
    }
}
