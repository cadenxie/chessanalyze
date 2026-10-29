using Microsoft.Data.Sqlite;

namespace ChessGameD.Web.Data;

/// <summary>
/// 数据库备份。
///
/// 项目用 <c>EnsureCreated()</c> 管理表结构,改了实体之后启动时会发现结构不匹配,
/// 于是删库重建——重建之前先把旧库备份一份,免得数据说没就没了。
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
            await using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO $target";
            cmd.Parameters.AddWithValue("$target", target);
            await cmd.ExecuteNonQueryAsync(ct);

            logger.LogWarning("数据库结构不匹配,旧库已备份到 {Path}", target);
            return target;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "备份旧数据库失败");
            return null;
        }
    }
}
