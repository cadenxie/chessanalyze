using ChessGameD.Web.Data;
using ChessGameD.Web.Services.Analysis;
using ChessGameD.Web.Services.Engine;
using ChessGameD.Web.Services.Pgn;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();

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

var app = builder.Build();

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
        }
        catch (Exception)
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

/// <summary>供集成测试引用的入口标记。</summary>
public partial class Program { }
