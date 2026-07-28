using Microsoft.EntityFrameworkCore;
using DocumentService.Domain;

namespace DocumentService.Infrastructure;

public class DocumentDbContext : DbContext
{
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    public DocumentDbContext(DbContextOptions<DocumentDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.ToTable("Documents");

            entity.HasOne(x => x.CurrentVersion)
                .WithMany()
                .HasForeignKey(x => x.CurrentVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DocumentVersion>(entity =>
        {
            entity.HasKey(x => x.VersionId);
            entity.ToTable("DocumentVersions");

            entity.HasOne(x => x.Document)
                .WithMany(x => x.Versions)
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
