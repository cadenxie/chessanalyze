using ChessGameD.Web.Data;
using ChessGameD.Web.Services.Analysis;
using ChessGameD.Web.Services.Auth;
using ChessGameD.Web.Services.Engine;
using ChessGameD.Web.Services.Pgn;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// 本地私有配置(真实口令之类),已 gitignore,不会进版本库。
// 测试环境不加载:免得开发机上的本地配置(连着真实数据库)影响测试。
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
}

builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

// ---- 认证 / 授权 ----
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "chessgame.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // 站点暂时只有 HTTP;SameAsRequest = 以后上了 HTTPS 会自动带上 Secure
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.Events.OnValidatePrincipal = async context =>
        {
            // 每次请求都确认账号还在、也没被停用:停用要能立刻生效
            var rawId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(rawId, out var userId))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null || !user.IsEnabled)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.Name = "chessgame.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// ---- 数据库(SQLite) ----
var dataDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=App_Data/chessgame.db";
const string dataSourcePrefix = "Data Source=";
if (connectionString.StartsWith(dataSourcePrefix, StringComparison.OrdinalIgnoreCase))
{
    var raw = connectionString[dataSourcePrefix.Length..].Trim();
    if (!Path.IsPathRooted(raw))
    {
        connectionString = dataSourcePrefix + Path.Combine(builder.Environment.ContentRootPath, raw);
    }
}

builder.Services.AddDbContextFactory<AppDbContext>(opt => opt.UseSqlite(connectionString));
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

// ---- 配置 ----
builder.Services.Configure<ChessOptions>(builder.Configuration.GetSection("Chess"));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Chess"));

builder.Services.AddHttpClient("stockfish", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ChessGameD/1.0");
    client.Timeout = TimeSpan.FromMinutes(15);
});

// ---- 应用服务 ----
// 运行环境(系统 + 架构)决定下载哪个 Stockfish 发行包,整个进程共用一份
builder.Services.AddSingleton<EnginePlatform>(_ => EnginePlatform.Detect());
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<EngineManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EngineManager>());
builder.Services.AddSingleton<EngineInstaller>();
builder.Services.AddSingleton<PgnImportService>();
builder.Services.AddSingleton<GameAnalyzer>();

// 账号模块
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddSingleton<PasswordHashing>();
builder.Services.AddSingleton<RegistrationLimiter>();
builder.Services.AddSingleton<AnonymousCleanupService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AnonymousCleanupService>());

var app = builder.Build();
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

// ---- 初始化数据库 ----
await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    var dbFile = db.Database.GetDbConnection().DataSource;
    var dbPath = string.IsNullOrEmpty(dbFile) ? null : Path.GetFullPath(dbFile);

    if (dbPath is null || !File.Exists(dbPath))
    {
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        try
        {
            // 探测表结构是否匹配;不匹配(例如换了 schema)则重建
            await db.Games.AsNoTracking().Take(1).ToListAsync();
            await db.Users.AsNoTracking().Take(1).ToListAsync();
        }
        catch (Exception)
        {
            // 重建会把旧库删掉,先备份一份
            await DatabaseBackup.BackupAsync(dbPath, startupLogger);
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }
    }

    // 种子管理员:账号名与口令来自本地配置 / 环境变量,真实值不进版本库
    var authOptions = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
    var passwords = app.Services.GetRequiredService<PasswordHashing>();
    await SeedAdmin.EnsureAsync(db, authOptions, passwords, startupLogger);
}

// 只建管理员然后退出:dotnet ChessGameD.Web.dll --seed-admin
if (args.Contains("--seed-admin", StringComparer.OrdinalIgnoreCase))
{
    startupLogger.LogInformation("种子管理员已就绪,进程退出。");
    return;
}

// 测试环境跟开发环境一样:不挂异常页,让接口直接返回真实状态码
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<MustChangePasswordMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

/// <summary>供集成测试引用的入口标记。</summary>
public partial class Program { }
