using Microsoft.EntityFrameworkCore;
using PropertyLedger.Core.Models;

namespace PropertyLedger.Core.Data;

/// <summary>
/// EF Core 数据上下文。具体数据库提供程序（生产环境为 PostgreSQL）在 Web 启动层注册，
/// Core 只依赖 EF Core 抽象，便于单元测试替换为 SQLite / 内存提供程序。
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Lease> Leases => Set<Lease>();
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<BillItem> BillItems => Set<BillItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // 账期/日期为本地日期语义，不使用 timestamptz（避免 Npgsql 要求 UTC 的限制）
        foreach (var entity in b.Model.GetEntityTypes())
        foreach (var prop in entity.GetProperties())
            if (prop.ClrType == typeof(DateTime) || prop.ClrType == typeof(DateTime?))
                prop.SetColumnType("timestamp without time zone");

        b.Entity<Property>(e =>
        {
            e.HasIndex(p => p.Name);
            e.Property(p => p.Name).IsRequired().HasMaxLength(100);
            e.Property(p => p.Address).HasMaxLength(200);
            e.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.ManagementContact).HasMaxLength(100);
            e.Property(p => p.UtilityContact).HasMaxLength(100);
        });

        b.Entity<Room>(e =>
        {
            e.HasOne(r => r.Property)
             .WithMany(p => p.Rooms)
             .HasForeignKey(r => r.PropertyId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(r => r.Name).IsRequired().HasMaxLength(100);
            e.Property(r => r.DefaultRent).HasPrecision(12, 2);
        });

        b.Entity<Tenant>(e =>
        {
            e.Property(t => t.Name).IsRequired().HasMaxLength(50);
            e.Property(t => t.Phone).HasMaxLength(30);
            e.Property(t => t.IdCard).HasMaxLength(30);
        });

        b.Entity<Lease>(e =>
        {
            e.HasOne(l => l.Room)
             .WithMany(r => r.Leases)
             .HasForeignKey(l => l.RoomId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.Tenant)
             .WithMany(t => t.Leases)
             .HasForeignKey(l => l.TenantId)
             .OnDelete(DeleteBehavior.Restrict);
            e.Property(l => l.RentAmount).HasPrecision(12, 2);
            e.Property(l => l.HouseDeposit).HasPrecision(12, 2);
            e.Property(l => l.UtilityDeposit).HasPrecision(12, 2);
            // 同一租约同一账期不允许重复账单（由服务层按 PeriodStart 保证，这里加约束兜底）
        });

        b.Entity<Bill>(e =>
        {
            e.HasOne(x => x.Lease)
             .WithMany(l => l.Bills)
             .HasForeignKey(x => x.LeaseId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RentAmount).HasPrecision(12, 2);
            e.Property(x => x.TotalAmount).HasPrecision(12, 2);
            e.Property(x => x.PaidAmount).HasPrecision(12, 2);
            e.HasIndex(x => new { x.LeaseId, x.PeriodStart }).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.DueDate);
        });

        b.Entity<BillItem>(e =>
        {
            e.HasOne(i => i.Bill)
             .WithMany(x => x.Items)
             .HasForeignKey(i => i.BillId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(i => i.Name).IsRequired().HasMaxLength(50);
            e.Property(i => i.Amount).HasPrecision(12, 2);
        });

        b.Entity<Payment>(e =>
        {
            e.HasOne(p => p.Bill)
             .WithMany(x => x.Payments)
             .HasForeignKey(p => p.BillId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(p => p.Amount).HasPrecision(12, 2);
        });

        b.Entity<AppUser>(e =>
        {
            e.HasIndex(u => u.UserName).IsUnique();
            e.Property(u => u.UserName).IsRequired().HasMaxLength(50);
            e.Property(u => u.DisplayName).HasMaxLength(50);
            e.Property(u => u.PasswordHash).IsRequired();
        });

        b.Entity<Setting>(e =>
        {
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(64);
        });
    }
}
