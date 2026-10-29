using System;
using System.Collections.Generic;
using System.Linq;
using SharedKernel.Domain;

namespace DocumentService.Domain;

public class Document : AggregateRoot<Guid>
{
    public Guid TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Active";

    public virtual ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();

    /// <summary>
    /// The version flagged <see cref="DocumentVersion.IsCurrent"/>. Derived from <see cref="Versions"/> (which must be loaded)
    /// rather than stored as a foreign key: a Documents.CurrentVersionId column pointing at DocumentVersions, alongside
    /// DocumentVersions.DocumentId pointing back, is a circular dependency EF cannot insert in one save, so no document
    /// could ever be created.
    /// </summary>
    public DocumentVersion? CurrentVersion => Versions.FirstOrDefault(v => v.IsCurrent);

    public Document(Guid id, Guid tenantId, string title, string category, Guid ownerId) : base(id)
    {
        TenantId = tenantId;
        Title = title;
        Category = category;
        OwnerId = ownerId;
        CreatedDate = DateTime.UtcNow;
        Status = "Active";
    }

    /// <summary>
    /// Appends a new version and makes it the only current one. <see cref="Versions"/> must be loaded so the next
    /// version number is computed from every existing version.
    /// </summary>
    public DocumentVersion AddVersion(
        Guid versionId,
        string storagePath,
        string fileName,
        long fileSize,
        string mimeType,
        string sha256Hash,
        Guid createdBy,
        string changeComment)
    {
        foreach (var existing in Versions)
        {
            existing.IsCurrent = false;
        }

        var nextNumber = Versions.Count == 0 ? 1 : Versions.Max(v => v.VersionNumber) + 1;

        var version = new DocumentVersion(
            versionId, Id, nextNumber, storagePath, fileName, fileSize, mimeType, sha256Hash, createdBy, changeComment);

        Versions.Add(version);
        return version;
    }

#pragma warning disable CS8618
    protected Document() { }
#pragma warning restore CS8618
}
