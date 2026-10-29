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
            // Ids are assigned by the domain constructors. Without ValueGeneratedNever, EF treats a child that has a
            // pre-assigned GUID and is added to an already-tracked parent as an existing row and issues an UPDATE
            // (DbUpdateConcurrencyException) instead of an INSERT.
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.ToTable("Cases");
            // Case numbers are unique per tenant, matching the duplicate check in CreateCaseAsync. A global index let
            // one tenant's number block every other tenant.
            entity.HasIndex(x => new { x.TenantId, x.CaseNumber }).IsUnique();
        });

        modelBuilder.Entity<CaseTimeline>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.ToTable("CaseTimelines");

            entity.HasOne(x => x.Case)
                .WithMany(x => x.TimelineEntries)
                .HasForeignKey(x => x.CaseId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
