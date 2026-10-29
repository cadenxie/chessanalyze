using ChessGameD.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChessGameD.Web.Services.Auth;

/// <summary>
/// 定时清理匿名棋局:每天凌晨 02:00 跑一次,
/// 把超过保留期(默认 24 小时)的匿名棋局连同它的走子与分析一起删掉。
///
/// 匿名会话的 Cookie 关掉浏览器就没了,这些棋局留着也没人看得到,
/// 不清理的话表会一直涨。
/// </summary>
public sealed class AnonymousCleanupService : BackgroundService
{
    /// <summary>每天的清理时刻(本地时间)。</summary>
    public const int CleanupHour = 2;

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IOptions<AuthOptions> _options;
    private readonly ILogger<AnonymousCleanupService> _logger;

    public AnonymousCleanupService(
        IDbContextFactory<AppDbContext> factory,
        IOptions<AuthOptions> options,
        ILogger<AnonymousCleanupService> logger)
    {
        _factory = factory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeUntilNextRun(DateTime.Now), stoppingToken);
                var removed = await CleanupAsync(stoppingToken);
                if (removed > 0)
                {
                    _logger.LogInformation("已清理 {Count} 局过期的匿名棋局", removed);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理匿名棋局失败");
            }
        }
    }

    /// <summary>距离下一次清理还有多久。</summary>
    public static TimeSpan TimeUntilNextRun(DateTime now)
    {
        var next = new DateTime(now.Year, now.Month, now.Day, CleanupHour, 0, 0, DateTimeKind.Local);
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        return next - now;
    }

    /// <summary>清理过期匿名数据,返回删掉的棋局数。</summary>
    public async Task<int> CleanupAsync(CancellationToken ct = default)
    {
        var hours = _options.Value.AnonymousRetentionHours;
        if (hours <= 0)
        {
            return 0;
        }

        var cutoff = DateTime.UtcNow.AddHours(-hours);

        await using var db = await _factory.CreateDbContextAsync(ct);

        var staleIds = await db.Games
            .Where(g => g.OwnerAnonKey != null && g.ImportedAt < cutoff)
            .Select(g => g.Id)
            .ToListAsync(ct);

        if (staleIds.Count == 0)
        {
            return 0;
        }

        // 显式删子表,别指望外键级联(SQLite 的外键开关不一定开着)
        await db.Analyses.Where(a => staleIds.Contains(a.GameId)).ExecuteDeleteAsync(ct);
        await db.Moves.Where(m => staleIds.Contains(m.GameId)).ExecuteDeleteAsync(ct);
        await db.Games.Where(g => staleIds.Contains(g.Id)).ExecuteDeleteAsync(ct);

        // 顺手清掉已经没有棋局的导入批次
        await db.ImportBatches
            .Where(b => !db.Games.Any(g => g.ImportBatchId == b.Id))
            .ExecuteDeleteAsync(ct);

        return staleIds.Count;
    }
}
