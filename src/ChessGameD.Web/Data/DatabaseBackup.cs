using Microsoft.Data.Sqlite;

namespace ChessGameD.Web.Data;

/// <summary>
/// 数据库备份。
///
/// 项目现在用 EF Core Migrations 管理表结构:改了实体之后只往上加迁移,绝不删库重建。
/// 但迁移前还是先备份一份旧库——万一迁移脚本有问题,至少能捡回来。
/// </summary>
public static class DatabaseBackup
{
    /// <summary>备份目录名(放在数据库同级)。</summary>
    public const string BackupFolderName = "backup";

    /// <summary>
    /// 把数据库备份到 <c>&lt;库目录&gt;/backup/chessgame-&lt;时间戳&gt;.db</c>,
    /// 返回备份文件路径;失败时返回 null(不阻断启动)。
    /// </summary>
    public static async Task<string?> BackupAsync(
        string dbPath,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(dbPath))
            {
                logger.LogWarning("数据库文件不存在,跳过备份: {Path}", dbPath);
                return null;
            }

            var dir = Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", BackupFolderName);
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, $"chessgame-{DateTime.Now:yyyyMMdd-HHmmss}.db");

            // VACUUM INTO 会把 WAL 里的数据一起带过去,比直接复制文件可靠
            // 连接串里关掉连接池:池里的连接会一直攥着库文件句柄,
            // 后面删库重建的时候就会报「文件被其他进程占用」
            await using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO $target";
            cmd.Parameters.AddWithValue("$target", target);
            await cmd.ExecuteNonQueryAsync(ct);

            logger.LogWarning("数据库已备份到 {Path}", target);
            return target;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "备份旧数据库失败");
            return null;
        }
    }
}
