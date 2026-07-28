using Microsoft.EntityFrameworkCore;
using ReportingService.Domain;

namespace ReportingService.Infrastructure;

public class ReportingDbContext : DbContext
{
    public DbSet<MetricAggregate> MetricAggregates => Set<MetricAggregate>();

    public ReportingDbContext(DbContextOptions<ReportingDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MetricAggregate>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.ToTable("MetricAggregates");
        });
    }
}
