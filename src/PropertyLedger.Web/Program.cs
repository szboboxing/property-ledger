using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PropertyLedger.Core.Data;
using PropertyLedger.Core.Models;
using PropertyLedger.Core.Services;
using PropertyLedger.Web.Components;
using PropertyLedger.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ── 数据库：PostgreSQL。连接串取配置 ConnectionStrings:Default，
//    生产环境用环境变量 ConnectionStrings__Default 覆盖（docker-compose 注入）；
//    启用重试以容忍容器编排中数据库容器稍晚就绪 ──
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=propertyledger;Username=property;Password=property";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql
        .MigrationsAssembly(typeof(Program).Assembly.FullName)
        .EnableRetryOnFailure()));

// 领域服务（DbContext 为 Scoped，与 Blazor Server 电路及 HTTP 请求生命周期一致）
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<BillImageRenderer>();

// ── Cookie 认证（多标签共享、刷新不掉线，适合内网工具）──
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
        options.Cookie.Name = "property-ledger-auth";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(options =>
{
    // 功能级策略：总管理员全通过；普通用户按登录时颁发的 feature 声明判定
    foreach (var (key, _) in UserFeatures.All)
    {
        options.AddPolicy($"Feature:{key}", policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => ctx.User.IsInRole("Admin") || ctx.User.HasClaim("feature", key)));
    }
});
builder.Services.AddCascadingAuthenticationState();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// 首次启动：建库 + 默认管理员
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

// 账单图片（供下载后用微信转发给租客）
app.MapBillImageEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
