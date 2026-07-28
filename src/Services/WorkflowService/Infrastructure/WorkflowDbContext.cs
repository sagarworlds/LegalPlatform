using Microsoft.EntityFrameworkCore;
using WorkflowService.Domain;

namespace WorkflowService.Infrastructure;

public class WorkflowDbContext : DbContext
{
    public DbSet<WorkflowTemplate> Templates => Set<WorkflowTemplate>();
    public DbSet<WorkflowStep> Steps => Set<WorkflowStep>();
    public DbSet<WorkflowInstance> Instances => Set<WorkflowInstance>();
    public DbSet<WorkflowHistory> History => Set<WorkflowHistory>();

    public WorkflowDbContext(DbContextOptions<WorkflowDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<WorkflowTemplate>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.ToTable("WorkflowTemplates");
        });

        modelBuilder.Entity<WorkflowStep>(entity =>
        {
            entity.HasKey(x => x.StepId);
            entity.ToTable("WorkflowSteps");

            entity.HasOne(x => x.WorkflowTemplate)
                .WithMany(x => x.Steps)
                .HasForeignKey(x => x.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowInstance>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.ToTable("WorkflowInstances");

            entity.HasOne(x => x.WorkflowTemplate)
                .WithMany()
                .HasForeignKey(x => x.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkflowHistory>(entity =>
        {
            entity.HasKey(x => x.HistoryId);
            entity.ToTable("WorkflowHistory");

            entity.HasOne(x => x.WorkflowInstance)
                .WithMany(x => x.HistoryEntries)
                .HasForeignKey(x => x.WorkflowInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
