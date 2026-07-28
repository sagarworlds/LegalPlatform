using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuditService.Domain;

namespace AuditService.Infrastructure;

public class AuditDbContext : DbContext
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.ToTable("AuditLogs");
        });
    }

    public override int SaveChanges()
    {
        PreventModifications();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        PreventModifications();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void PreventModifications()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified || e.State == EntityState.Deleted);

        if (entries.Any())
        {
            throw new InvalidOperationException("Audit trail is immutable and append-only. Updates and deletions are strictly prohibited.");
        }
    }
}
