using ChessGameD.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChessGameD.Tests;

/// <summary>
/// 重建数据库前的自动备份。
///
/// 项目用 <c>EnsureCreated()</c> 管表结构,改了实体就会删库重建,
/// 重建前必须先把旧库备份一份。
/// </summary>
public sealed class DatabaseBackupTests : IDisposable
{
    private readonly string _dir;

    public DatabaseBackupTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "chessgamed-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task 备份_旧库被复制到backup目录_并且内容完整()
    {
        var dbPath = Path.Combine(_dir, "test.db");

        // 先建一个库,塞一局棋
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();

            db.ImportBatches.Add(new ImportBatch
            {
                FileName = "old.pgn",
                ImportedAt = DateTime.UtcNow,
                TotalGames = 1,
                ImportedGames = 1,
            });
            await db.SaveChangesAsync();

            var batchId = await db.ImportBatches.Select(b => b.Id).FirstAsync();
            db.Games.Add(new Game
            {
                White = "Alice",
                Black = "Bob",
                ImportedAt = DateTime.UtcNow,
                ImportBatchId = batchId,
            });
            await db.SaveChangesAsync();
        }

        var backup = await DatabaseBackup.BackupAsync(dbPath, NullLogger.Instance);

        Assert.NotNull(backup);
        Assert.True(File.Exists(backup!), $"备份文件不存在: {backup}");
        Assert.Contains(DatabaseBackup.BackupFolderName, backup);

        // 备份里的数据要跟原来一样
        var backupOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={backup}")
            .Options;
        await using var backupDb = new AppDbContext(backupOptions);
        var game = await backupDb.Games.SingleAsync();
        Assert.Equal("Alice", game.White);
    }

    [Fact]
    public async Task 备份_库文件不存在时_不抛异常()
    {
        var missing = Path.Combine(_dir, "not-here.db");

        var backup = await DatabaseBackup.BackupAsync(missing, NullLogger.Instance);

        Assert.Null(backup);
    }
}
