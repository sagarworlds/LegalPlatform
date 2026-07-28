using Microsoft.EntityFrameworkCore;
using OrganizationService.Domain;

namespace OrganizationService.Infrastructure;

public class OrganizationDbContext : DbContext
{
    public DbSet<Organization> Organizations => Set<Organization>();

    public OrganizationDbContext(DbContextOptions<OrganizationDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.ToTable("Organizations");
            entity.HasIndex(x => x.TenantId).IsUnique();
        });
    }
}
