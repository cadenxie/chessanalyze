using Microsoft.EntityFrameworkCore;

namespace ChessGameD.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameMove> Moves => Set<GameMove>();
    public DbSet<Analysis> Analyses => Set<Analysis>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<User> Users => Set<User>();
    public DbSet<PlaySession> PlaySessions => Set<PlaySession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Game>(b =>
        {
            b.ToTable("Games");
            b.HasIndex(g => new { g.White, g.Black });
            b.HasIndex(g => g.Date);
            b.HasIndex(g => g.ImportBatchId);
            b.HasIndex(g => g.OwnerUserId);
            b.HasIndex(g => g.OwnerAnonKey);
            b.HasOne(g => g.ImportBatch)
                .WithMany(b => b.Games)
                .HasForeignKey(g => g.ImportBatchId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(g => g.OwnerUser)
                .WithMany(u => u.Games)
                .HasForeignKey(g => g.OwnerUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("Users");
            b.HasIndex(u => u.UserName).IsUnique();
            // 邮箱可空:SQLite 里多个 NULL 不算重复,所以可空字段照样能建唯一索引
            b.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<GameMove>(b =>
        {
            b.ToTable("Moves");
            b.HasIndex(m => new { m.GameId, m.Ply }).IsUnique();
            b.HasOne(m => m.Game)
                .WithMany(g => g.Moves)
                .HasForeignKey(m => m.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Analysis>(b =>
        {
            b.ToTable("Analyses");
            b.HasIndex(a => new { a.GameId, a.Ply, a.EngineVersion }).IsUnique();
            b.HasOne(a => a.Game)
                .WithMany(g => g.Analyses)
                .HasForeignKey(a => a.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ImportBatch>(b =>
        {
            b.ToTable("ImportBatches");
            b.HasIndex(x => x.ImportedAt);
        });

        modelBuilder.Entity<AppSetting>(b =>
        {
            b.ToTable("AppSettings");
            b.HasKey(s => s.Key);
        });

        modelBuilder.Entity<PlaySession>(b =>
        {
            b.ToTable("PlaySessions");
            b.HasIndex(s => s.OwnerUserId);
            b.HasIndex(s => s.OwnerAnonKey);
            b.HasIndex(s => s.Status);
            b.HasOne(s => s.OwnerUser)
                .WithMany(u => u.PlaySessions)
                .HasForeignKey(s => s.OwnerUserId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(s => s.Game)
                .WithMany()
                .HasForeignKey(s => s.GameId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
