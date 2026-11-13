namespace BetaTestBot.Data;
using Microsoft.EntityFrameworkCore;
using BetaTestBot.Data.Entities;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<Project> Projects { get; set; }
    public DbSet<Application> Applications { get; set; }
    public DbSet<Build> Builds { get; set; }
    public DbSet<BetaTester> BetaTesters { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasIndex(u => u.TelegramID)
            .IsUnique();

        modelBuilder.Entity<BetaTester>()
            .HasIndex(bt => new { bt.UserID, bt.ProjectID })
            .IsUnique();
    }
}
