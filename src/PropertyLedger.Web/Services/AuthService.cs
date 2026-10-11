using Microsoft.EntityFrameworkCore;
using PropertyLedger.Core.Data;
using PropertyLedger.Core.Models;
using PropertyLedger.Core.Services;

namespace PropertyLedger.Web.Services;

/// <summary>登录校验与用户账号管理。</summary>
public class AuthService
{
    private readonly AppDbContext _db;
    public AuthService(AppDbContext db) => _db = db;

    public async Task<AppUser?> ValidateAsync(string userName, string password, CancellationToken ct = default)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.UserName == userName.Trim() && u.IsActive, ct);
        if (user is null) return null;
        if (!PasswordHasher.Verify(password, user.PasswordHash)) return null;

        user.LastLoginAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<List<AppUser>> ListAsync(CancellationToken ct = default) =>
        await _db.Users.OrderBy(u => u.Id).ToListAsync(ct);

    public async Task<AppUser> CreateAsync(string userName, string displayName, string password, bool isAdmin,
        string? features = null, CancellationToken ct = default)
    {
        userName = userName.Trim();
        if (string.IsNullOrWhiteSpace(userName)) throw new DomainException("登录名不能为空。");
        if (password.Length < 6) throw new DomainException("密码至少 6 位。");
        if (await _db.Users.AnyAsync(u => u.UserName == userName, ct))
            throw new DomainException("登录名已存在。");

        var user = new AppUser
        {
            UserName = userName,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? userName : displayName.Trim(),
            PasswordHash = PasswordHasher.Hash(password),
            IsAdmin = isAdmin,
            Features = UserFeatures.Normalize(features),
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>更新普通用户的功能权限（CSV；总管理员忽略）。</summary>
    public async Task SetFeaturesAsync(int userId, string? features, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync(new object[] { userId }, ct)
                   ?? throw new DomainException("用户不存在。");
        user.Features = UserFeatures.Normalize(features);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ChangePasswordAsync(int userId, string newPassword, CancellationToken ct = default)
    {
        if (newPassword.Length < 6) throw new DomainException("密码至少 6 位。");
        var user = await _db.Users.FindAsync(new object[] { userId }, ct)
                   ?? throw new DomainException("用户不存在。");
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(int userId, bool active, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync(new object[] { userId }, ct)
                   ?? throw new DomainException("用户不存在。");
        if (user.IsAdmin && !active)
        {
            var adminCount = await _db.Users.CountAsync(u => u.IsAdmin && u.IsActive, ct);
            if (adminCount <= 1) throw new DomainException("至少保留一个启用的管理员。");
        }
        user.IsActive = active;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>修改普通用户的登录名与显示名（管理员账号不可修改，防止误动主账号）。</summary>
    public async Task RenameUserAsync(int userId, string userName, string? displayName, CancellationToken ct = default)
    {
        userName = userName.Trim();
        if (string.IsNullOrWhiteSpace(userName)) throw new DomainException("登录名不能为空。");
        var user = await _db.Users.FindAsync(new object[] { userId }, ct)
                   ?? throw new DomainException("用户不存在。");
        if (user.IsAdmin) throw new DomainException("管理员账号不支持修改登录名。");
        if (await _db.Users.AnyAsync(u => u.UserName == userName && u.Id != userId, ct))
            throw new DomainException("登录名已存在。");
        user.UserName = userName;
        user.DisplayName = string.IsNullOrWhiteSpace(displayName) ? userName : displayName.Trim();
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>删除普通用户账号（管理员账号不可删除；账号不与业务数据关联，删除不影响账目）。</summary>
    public async Task DeleteUserAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync(new object[] { userId }, ct)
                   ?? throw new DomainException("用户不存在。");
        if (user.IsAdmin) throw new DomainException("管理员账号不可删除。");
        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
    }
}
