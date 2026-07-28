using Microsoft.EntityFrameworkCore;
using CaseService.Domain;

namespace CaseService.Infrastructure;

public class CaseDbContext : DbContext
{
    public DbSet<Case> Cases => Set<Case>();
    public DbSet<CaseTimeline> CaseTimelines => Set<CaseTimeline>();

    public CaseDbContext(DbContextOptions<CaseDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Case>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.ToTable("Cases");
            entity.HasIndex(x => x.CaseNumber).IsUnique();
        });

        modelBuilder.Entity<CaseTimeline>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.ToTable("CaseTimelines");

            entity.HasOne(x => x.Case)
                .WithMany(x => x.TimelineEntries)
                .HasForeignKey(x => x.CaseId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
