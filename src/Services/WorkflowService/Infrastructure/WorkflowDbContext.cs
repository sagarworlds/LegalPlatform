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

        // Every key is assigned by the domain constructors. Without ValueGeneratedNever, EF treats a child that has
        // a pre-assigned GUID and is added to an already-tracked parent as an existing row and issues an UPDATE
        // (DbUpdateConcurrencyException) instead of an INSERT, which broke approve and reject.
        modelBuilder.Entity<WorkflowTemplate>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.ToTable("WorkflowTemplates");
            entity.HasIndex(x => x.TenantId);
        });

        modelBuilder.Entity<WorkflowStep>(entity =>
        {
            entity.HasKey(x => x.StepId);
            entity.Property(x => x.StepId).ValueGeneratedNever();
            entity.ToTable("WorkflowSteps");

            entity.HasOne(x => x.WorkflowTemplate)
                .WithMany(x => x.Steps)
                .HasForeignKey(x => x.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowInstance>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.ToTable("WorkflowInstances");

            entity.HasOne(x => x.WorkflowTemplate)
                .WithMany()
                .HasForeignKey(x => x.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkflowHistory>(entity =>
        {
            entity.HasKey(x => x.HistoryId);
            entity.Property(x => x.HistoryId).ValueGeneratedNever();
            entity.ToTable("WorkflowHistory");

            entity.HasOne(x => x.WorkflowInstance)
                .WithMany(x => x.HistoryEntries)
                .HasForeignKey(x => x.WorkflowInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
