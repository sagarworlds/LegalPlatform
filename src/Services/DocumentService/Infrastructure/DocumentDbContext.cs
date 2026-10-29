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
            // Ids are assigned by the domain constructors. Without ValueGeneratedNever, EF treats a child that has a
            // pre-assigned GUID and is added to an already-tracked parent as an existing row and issues an UPDATE
            // (DbUpdateConcurrencyException) instead of an INSERT.
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.ToTable("Documents");
            entity.HasIndex(x => x.TenantId);

            // Computed from Versions; see Document.CurrentVersion for why it is not a foreign key.
            entity.Ignore(x => x.CurrentVersion);
        });

        modelBuilder.Entity<DocumentVersion>(entity =>
        {
            entity.HasKey(x => x.VersionId);
            entity.Property(x => x.VersionId).ValueGeneratedNever();
            entity.ToTable("DocumentVersions");

            // Two concurrent uploads that both compute the same next number: the second save fails instead of
            // silently creating a duplicate version number.
            entity.HasIndex(x => new { x.DocumentId, x.VersionNumber }).IsUnique();

            entity.HasOne(x => x.Document)
                .WithMany(x => x.Versions)
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
