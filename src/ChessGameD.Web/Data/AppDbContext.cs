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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Game>(b =>
        {
            b.ToTable("Games");
            b.HasIndex(g => new { g.White, g.Black });
            b.HasIndex(g => g.Date);
            b.HasIndex(g => g.ImportBatchId);
            b.HasOne(g => g.ImportBatch)
                .WithMany(b => b.Games)
                .HasForeignKey(g => g.ImportBatchId)
                .OnDelete(DeleteBehavior.Cascade);
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
    }
}
