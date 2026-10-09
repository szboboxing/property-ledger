using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PropertyLedger.Core.Data;

namespace PropertyLedger.Web.Data;

/// <summary>
/// EF Core 设计时上下文工厂：dotnet ef 命令（migrations / dbcontext）直接使用，
/// 不会启动应用程序、也不会执行种子逻辑。连接串无需真实可达，仅用于生成迁移。
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=propertyledger;Username=property;Password=design-time",
                npgsql => npgsql.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.FullName))
            .Options;
        return new AppDbContext(options);
    }
}
