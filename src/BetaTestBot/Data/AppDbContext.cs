namespace BetaTestBot.Data;
using Microsoft.EntityFrameworkCore;
using BetaTestBot.Data.Entities;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Project> Projects { get; set; } = null!;
    public DbSet<Application> Applications { get; set; } = null!;
    public DbSet<Build> Builds { get; set; } = null!;
    public DbSet<BetaTester> BetaTesters { get; set; } = null!;
    public DbSet<Feedback> Feedbacks { get; set; } = null!;

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
