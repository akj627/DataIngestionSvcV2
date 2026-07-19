using DataIngestion.Model.Models;
using Microsoft.EntityFrameworkCore;

namespace DataIngestion.Model.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<IngestionRun> IngestionRuns => Set<IngestionRun>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Holding> Holdings => Set<Holding>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IngestionRun>()
            .HasIndex(r => r.ZipHash)
            .IsUnique();

        // Same client can appear in multiple runs; unique within a single run
        modelBuilder.Entity<Client>()
            .HasIndex(c => new { c.ClientId, c.IngestionRunId })
            .IsUnique();
    }
}
