using Microsoft.AspNetCore.StaticFiles;

namespace PropertyLedger.Web.Services;

/// <summary>
/// 富文本备注插图的上传 / 查看接口：
/// - POST /api/uploads：仅登录用户；要求同源 fetch 附加自定义头 X-Requested-With（跨站无法伪造）防 CSRF；
///   扩展名白名单 + 单文件 5MB 上限，落盘 {ContentRoot}/uploads（Docker 中以卷持久化）。
/// - GET /uploads/{name}：仅登录用户可查看；文件名规范化校验防目录穿越。
/// </summary>
public static class UploadEndpoints
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];
    private const long MaxFileSize = 5 * 1024 * 1024;

    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        var env = app.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var uploadsDir = Path.Combine(env.ContentRootPath, "uploads");
        Directory.CreateDirectory(uploadsDir);

        var contentTypeProvider = new FileExtensionContentTypeProvider();

        app.MapPost("/api/uploads", async (HttpRequest request) =>
        {
            if (!string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.Ordinal))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var file = request.Form.Files["file"];
            if (file is null || file.Length == 0) return Results.BadRequest("未选择图片。");
            if (file.Length > MaxFileSize) return Results.BadRequest("图片不能超过 5MB。");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext)) return Results.BadRequest("仅支持 jpg / png / gif / webp 图片。");

            var name = $"{Guid.NewGuid():N}{ext}";
            await using var fs = File.Create(Path.Combine(uploadsDir, name));
            await file.CopyToAsync(fs);
            return Results.Json(new { url = $"/uploads/{name}" });
        })
        .RequireAuthorization()
        // CSRF 防护不靠 antiforgery 表单令牌（富文本编辑器由 JS 发起 fetch，拿不到页面令牌），
        // 而是要求自定义头 X-Requested-With + 登录 Cookie：跨站请求无法伪造自定义头。
        .DisableAntiforgery();

        app.MapGet("/uploads/{name}", (string name) =>
        {
            if (name.Contains('/') || name.Contains('\\') || name.Contains(".."))
                return Results.BadRequest();
            var ext = Path.GetExtension(name).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext)) return Results.NotFound();
            var path = Path.Combine(uploadsDir, name);
            if (!File.Exists(path)) return Results.NotFound();
            return contentTypeProvider.TryGetContentType(name, out var type)
                ? Results.File(path, type)
                : Results.File(path);
        }).RequireAuthorization();

        return app;
    }
}
