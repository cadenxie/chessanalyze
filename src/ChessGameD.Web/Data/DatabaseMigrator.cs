using Microsoft.EntityFrameworkCore;

namespace ChessGameD.Web.Data;

/// <summary>
/// 数据库的增量迁移。
///
/// 项目以前用 <c>EnsureCreated()</c> 管表结构:改了实体之后启动时发现结构对不上,
/// 就删库重建——数据说没就没了。现在改成 EF Core Migrations:
/// <list type="bullet">
/// <item>迁移文件在 <c>src/ChessGameD.Web/Migrations</c>,一条一条往上加,绝不重建;</item>
/// <item>老库(<c>EnsureCreated</c> 建的、没有迁移历史表的)启动时打上 baseline 标记,
/// 只补后面新增的迁移,老数据原样保留;</item>
/// <item>应用迁移之前自动备份一份数据库,备份失败也不阻断启动。</item>
/// </list>
/// </summary>
public static class DatabaseMigrator
{
    /// <summary>EF 的迁移历史表名。</summary>
    public const string HistoryTable = "__EFMigrationsHistory";

    /// <summary>baseline 迁移 id 的结尾(第一条迁移固定叫这个名字)。</summary>
    public const string BaselineSuffix = "_InitialCreate";

    /// <summary>
    /// 把数据库迁移到最新版本。
    ///
    /// 顺序:老库打 baseline 标记 → 有待应用的迁移就先备份 → 应用迁移。
    /// </summary>
    public static async Task MigrateAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken ct = default)
    {
        var migrations = db.Database.GetMigrations().ToList();
        if (migrations.Count == 0)
        {
            // 项目里一个迁移文件都没有(例如刚被误删):退回到建全表的旧办法
            logger.LogWarning("没有找到任何迁移文件,退回到 EnsureCreated 建表。");
            await db.Database.EnsureCreatedAsync(ct);
            return;
        }

        var hasHistory = await TableExistsAsync(db, HistoryTable, ct);
        if (!hasHistory)
        {
            // 没有迁移历史表:要么是全新的空库,要么是 EnsureCreated 造的老库
            var hasGames = await TableExistsAsync(db, "Games", ct);
            if (hasGames)
            {
                await StampBaselineAsync(db, migrations, logger, ct);
            }
        }

        var pending = db.Database.GetPendingMigrations().ToList();
        if (pending.Count > 0)
        {
            await BackupAsync(db, logger, ct);
            logger.LogInformation("应用数据库迁移: {Migrations}", string.Join(", ", pending));
        }

        db.Database.Migrate();

        if (pending.Count > 0)
        {
            logger.LogInformation("数据库迁移完成。");
        }
    }

    /// <summary>
    /// 给老库打 baseline 标记:把第一条迁移记成「已应用」,这样 EF 就不会去建已经存在的表。
    /// </summary>
    private static async Task StampBaselineAsync(
        AppDbContext db,
        IReadOnlyList<string> migrations,
        ILogger logger,
        CancellationToken ct)
    {
        var baseline = migrations.FirstOrDefault(m => m.EndsWith(BaselineSuffix, StringComparison.Ordinal))
            ?? migrations[0];

        var version = GetEfCoreVersion();

        await db.Database.ExecuteSqlRawAsync($"""
            CREATE TABLE IF NOT EXISTS "{HistoryTable}" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK_{HistoryTable}" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            $"INSERT OR IGNORE INTO \"{HistoryTable}\" (\"MigrationId\", \"ProductVersion\") VALUES ({{0}}, {{1}});",
            new object[] { baseline, version },
            ct);

        logger.LogInformation("老库已打上迁移基线标记({Baseline}),原有数据保留。", baseline);
    }

    /// <summary>迁移前备份一次数据库(失败不阻断启动)。</summary>
    private static async Task BackupAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var dataSource = db.Database.GetDbConnection().DataSource;
        if (string.IsNullOrEmpty(dataSource))
        {
            return;
        }

        var path = Path.GetFullPath(dataSource);
        if (!File.Exists(path))
        {
            return;
        }

        await DatabaseBackup.BackupAsync(path, logger, ct);
    }

    /// <summary>EF Core 的版本号(写进迁移历史表,只作记录用)。</summary>
    private static string GetEfCoreVersion()
    {
        var assembly = typeof(DbContext).Assembly;
        var informational = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;

        return informational ?? assembly.GetName().Version?.ToString() ?? "8.0.0";
    }

    /// <summary>表是否存在。</summary>
    private static async Task<bool> TableExistsAsync(AppDbContext db, string table, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = table;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result) > 0;
    }
}
