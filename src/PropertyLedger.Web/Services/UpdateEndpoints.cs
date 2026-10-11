namespace PropertyLedger.Web.Services;

using PropertyLedger.Core.Services;

/// <summary>
/// 在线升级相关端点：
/// - GET /healthz：匿名轻量健康检查，返回当前版本号。升级容器重建 web 期间，
///   浏览器据此探测服务恢复与新版本号（无需登录，避免升级后 Cookie 失效影响探测）。
/// - GET /api/update/check：检查更新（Feature:settings 权限）。
/// - POST /api/update/start：启动一键升级（仅总管理员），返回是否成功启动升级容器。
/// </summary>
public static class UpdateEndpoints
{
    public static IEndpointRouteBuilder MapUpdateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", () => Results.Json(new { healthy = true, version = AppVersion.Current }));

        app.MapGet("/api/update/check", async (UpdateService updates, bool? force, CancellationToken ct) =>
            Results.Json(await updates.CheckAsync(force == true, ct)))
            .RequireAuthorization("Feature:settings");

        app.MapPost("/api/update/start", async (HttpRequest request, DockerUpgradeService upgrader, UpdateService updates, CancellationToken ct) =>
        {
            // 与 /api/uploads 同一套 CSRF 防线：要求同源 fetch 附加自定义头
            if (!string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.Ordinal))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            // 先强制刷新一次最新版本信息（作为轮询目标版本号）
            UpdateCheckResult latest;
            try { latest = await updates.CheckAsync(force: true, ct); }
            catch { latest = new UpdateCheckResult(false, AppVersion.Current, AppVersion.Current, null, null, null); }

            var (started, message) = await upgrader.StartUpgradeAsync(ct);
            return Results.Json(new { started, message, latest = latest.LatestVersion });
        })
        .RequireAuthorization(policy => policy.RequireRole("Admin"))
        .DisableAntiforgery();

        return app;
    }
}
