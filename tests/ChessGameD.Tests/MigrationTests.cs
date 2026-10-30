using ChessGameD.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChessGameD.Tests;

/// <summary>
/// 数据库的增量迁移。
///
/// 项目以前是 <c>EnsureCreated()</c> + 「结构对不上就删库重建」,数据说没就没了;
/// 现在改成 EF Core Migrations,这里守住两条底线:
/// 老库打基线标记后数据原样保留,新库从零建表。
/// </summary>
public sealed class MigrationTests
{
    [Fact]
    public async Task 老库_打基线标记_只补新表_数据保留()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);

        // 造一个"老库":EnsureCreated 建的、没有迁移历史表
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS PlaySessions;");

        db.Games.Add(new Game
        {
            White = "老棋",
            Black = "老黑",
            Result = "1-0",
            PgnText = "1. e4 e5 1-0",
            PlyCount = 2,
            ImportedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);

        // 老数据还在
        Assert.Equal(1, await db.Games.CountAsync());
        Assert.Equal("老棋", (await db.Games.FirstAsync()).White);

        // 新表补上了
        Assert.True(TableExists(db, "PlaySessions"));

        // 迁移历史里有基线标记 + 后续的迁移
        var applied = db.Database.GetAppliedMigrations().ToList();
        Assert.Contains(applied, m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));
        Assert.Contains(applied, m => m.EndsWith("_AddPlaySessions", StringComparison.Ordinal));
        Assert.Empty(db.Database.GetPendingMigrations());
    }

    [Fact]
    public async Task 新库_从零迁移_所有表都建出来()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);

        await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);

        Assert.True(TableExists(db, "Games"));
        Assert.True(TableExists(db, "Moves"));
        Assert.True(TableExists(db, "Analyses"));
        Assert.True(TableExists(db, "ImportBatches"));
        Assert.True(TableExists(db, "AppSettings"));
        Assert.True(TableExists(db, "Users"));
        Assert.True(TableExists(db, "PlaySessions"));
        Assert.Empty(db.Database.GetPendingMigrations());
    }

    [Fact]
    public async Task 重复迁移_幂等_不会报错()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);

        await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);
        await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance);

        Assert.Empty(db.Database.GetPendingMigrations());
        Assert.True(TableExists(db, "PlaySessions"));
    }

    private static bool TableExists(AppDbContext db, string table)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = table;
        command.Parameters.Add(parameter);

        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }
}
