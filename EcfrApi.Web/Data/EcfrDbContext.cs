using Microsoft.EntityFrameworkCore;
using EcfrApi.Web.Models;

namespace EcfrApi.Web.Data;

public class EcfrDbContext : DbContext
{
    public EcfrDbContext(DbContextOptions<EcfrDbContext> options) : base(options)
    {
    }

    public DbSet<TitleVersionCache> TitleVersions { get; set; } = null!;
    public DbSet<TitleWordCountCache> TitleWordCounts { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure TitleVersionCache
        modelBuilder.Entity<TitleVersionCache>()
            .HasIndex(t => new { t.TitleNumber, t.IssueDate })
            .IsUnique();

        // Configure TitleWordCountCache
        modelBuilder.Entity<TitleWordCountCache>()
            .HasIndex(t => new { t.TitleNumber, t.Date })
            .IsUnique();
    }
}
