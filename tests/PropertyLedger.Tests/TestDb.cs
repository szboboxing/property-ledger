using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PropertyLedger.Core.Data;

namespace PropertyLedger.Tests;

/// <summary>基于 SQLite 内存库的测试上下文（生产用 PostgreSQL，领域逻辑与提供程序无关）。</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _conn;
    public AppDbContext Db { get; }

    public TestDb()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .Options;
        Db = new AppDbContext(options);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        _conn.Dispose();
    }
}
