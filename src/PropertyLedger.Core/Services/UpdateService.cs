using System.Text.Json;

namespace PropertyLedger.Core.Services;

/// <summary>一次更新检查的结果。</summary>
public sealed record UpdateCheckResult(
    bool HasUpdate,
    string CurrentVersion,
    string LatestVersion,
    string? ReleaseUrl,
    string? ReleaseNotes,
    DateTime? PublishedAt);

/// <summary>
/// 在线更新检查：查询 GitHub 仓库最新 Release，与当前版本比较。
/// 结果内存缓存 6 小时（GitHub 匿名 API 限流 60 次/小时/IP），避免每次刷新页面都请求。
/// 仓储与版本号在注册时注入（版本号来自 Web 层 AppVersion，Core 不反向依赖 Web）。
/// </summary>
public class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/szboboxing/property-ledger/releases/latest";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    private static readonly HttpClient Http = CreateClient();

    private readonly string _currentVersion;
    private readonly object _gate = new();
    private UpdateCheckResult? _cached;
    private DateTimeOffset _cachedAt;

    public UpdateService(string currentVersion) => _currentVersion = currentVersion;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        // GitHub API 必须携带 User-Agent
        c.DefaultRequestHeaders.UserAgent.ParseAdd("property-ledger-updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>取缓存中的检查结果；未检查过或缓存过期返回 null（不发起网络请求）。</summary>
    public UpdateCheckResult? PeekCached()
    {
        lock (_gate)
        {
            return _cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheTtl ? _cached : null;
        }
    }

    /// <summary>检查更新（有新鲜缓存时直接返回；force=true 强制重新请求）。</summary>
    public async Task<UpdateCheckResult> CheckAsync(bool force = false, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!force && _cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheTtl)
                return _cached;
        }

        using var resp = await Http.GetAsync(LatestReleaseApi, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        var result = ParseRelease(_currentVersion, json);

        lock (_gate)
        {
            _cached = result;
            _cachedAt = DateTimeOffset.UtcNow;
        }
        return result;
    }

    /// <summary>解析 GitHub latest release JSON（便于单元测试，不走网络）。</summary>
    public static UpdateCheckResult ParseRelease(string currentVersion, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = GetString(root, "tag_name") ?? string.Empty;
        var latest = tag.Trim().TrimStart('v', 'V');
        var url = GetString(root, "html_url")
                  ?? "https://github.com/szboboxing/property-ledger/releases/latest";
        var notes = GetString(root, "body");

        DateTime? publishedAt = null;
        if (root.TryGetProperty("published_at", out var pub) && pub.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(pub.GetString(), out var dto))
        {
            publishedAt = dto.LocalDateTime;
        }

        return new UpdateCheckResult(
            HasUpdate: IsNewerVersion(latest, currentVersion),
            CurrentVersion: currentVersion,
            LatestVersion: latest,
            ReleaseUrl: url,
            ReleaseNotes: string.IsNullOrWhiteSpace(notes) ? null : notes,
            PublishedAt: publishedAt);
    }

    /// <summary>版本比较：支持 v 前缀与 2~4 段版本号（1.9 / 1.9.1）；无法解析时一律视为不更新。</summary>
    public static bool IsNewerVersion(string latest, string current)
    {
        var l = Version.TryParse(Normalize(latest), out var lv) ? lv : null;
        var c = Version.TryParse(Normalize(current), out var cv) ? cv : null;
        return l is not null && c is not null && l > c;
    }

    private static string Normalize(string? v) =>
        (v ?? string.Empty).Trim().TrimStart('v', 'V').Split('-', '+')[0].Trim();

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
