using ChessGameD.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChessGameD.Tests;

/// <summary>内存 SQLite 数据库,供测试使用。</summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext Context { get; }
    public IDbContextFactory<AppDbContext> Factory { get; }
    public SettingsStore Settings { get; }

    /// <summary>测试用户的固定 id。</summary>
    public const int TestUserId = 1;

    public TestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new AppDbContext(options);
        Context.Database.EnsureCreated();
        Factory = new TestFactory(options);
        Settings = new SettingsStore(Factory, new MemoryCache(new MemoryCacheOptions()), NullLogger<SettingsStore>.Instance);

        // 建一个固定 id 的测试用户:棋局要挂在用户名下,外键得有对应的人
        Context.Users.Add(new User
        {
            Id = TestUserId,
            UserName = "tester",
            PasswordHash = "not-a-real-hash",
            CreatedAt = DateTime.UtcNow,
        });
        Context.SaveChanges();
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class TestFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestFactory(DbContextOptions<AppDbContext> options) => _options = options;

        public AppDbContext CreateDbContext() => new(_options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}

public static class TestPgn
{
    public const string SimpleGame = """
[Event "Test Game"]
[Site "Local"]
[Date "2026.01.01"]
[Round "1"]
[White "Alice"]
[Black "Bob"]
[Result "1-0"]
[WhiteElo "1500"]
[BlackElo "1400"]
[ECO "C42"]

1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 1-0
""";

    public const string ThreeGames = """
[Event "G1"]
[White "A1"]
[Black "B1"]
[Result "1-0"]

1. e4 e5 2. Nf3 1-0

[Event "G2"]
[White "A2"]
[Black "B2"]
[Result "0-1"]

1. d4 d5 2. c4 0-1

[Event "G3"]
[White "A3"]
[Black "B3"]
[Result "1/2-1/2"]

1. e4 c5 2. Nf3 1/2-1/2
""";

    public const string WithVariation = """
[Event "With Variation"]
[White "Alice"]
[Black "Bob"]
[Result "*"]

1. e4 (1. d4 d5 (1... Nf6 2. c4)) 1... e5 2. Nf3 *
""";

    public const string WithIllegalMove = """
[Event "Bad"]
[White "X"]
[Black "Y"]
[Result "*"]

1. e4 e5 2. Qxh8 *
""";

    public const string FromPosition = """
[Event "From Position"]
[Site "Local"]
[White "Alice"]
[Black "Bob"]
[Result "*"]
[FEN "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3"]

3... Nf6 4. d3 *
""";
}
