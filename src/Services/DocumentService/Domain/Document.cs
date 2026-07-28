using System;
using System.Collections.Generic;
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

    public Guid? CurrentVersionId { get; set; }
    public virtual DocumentVersion? CurrentVersion { get; set; }

    public virtual ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();

    public Document(Guid id, Guid tenantId, string title, string category, Guid ownerId) : base(id)
    {
        TenantId = tenantId;
        Title = title;
        Category = category;
        OwnerId = ownerId;
        CreatedDate = DateTime.UtcNow;
        Status = "Active";
    }

#pragma warning disable CS8618
    protected Document() { }
#pragma warning restore CS8618
}
