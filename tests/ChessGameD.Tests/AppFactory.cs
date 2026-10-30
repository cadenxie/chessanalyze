using ChessGameD.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChessGameD.Tests;

/// <summary>测试用的应用实例:每个实例一个独立的临时数据库。</summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly string _dir;
    private readonly (string Key, string Value)[] _settings;
    private readonly Action<IServiceCollection>? _configureServices;

    public AppFactory(params (string Key, string Value)[] settings)
        : this(null, settings)
    {
    }

    /// <summary>带服务覆盖的构造(例如把陪练引擎换成假的)。</summary>
    public AppFactory(Action<IServiceCollection>? configureServices, params (string Key, string Value)[] settings)
    {
        _dir = Path.Combine(Path.GetTempPath(), "chessgamed-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _settings = settings;
        _configureServices = configureServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // 测试环境:不加载 appsettings.Local.json(开发机上的本地配置连着真实数据库),
        // 也不挂异常页,让接口直接返回真实状态码
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={Path.Combine(_dir, "test.db")}");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        if (_configureServices is not null)
        {
            builder.ConfigureServices((_, services) => _configureServices(services));
        }
    }

    /// <summary>在应用自己的数据库上跑一段逻辑(测试里模拟管理员操作时用)。</summary>
    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(db);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
