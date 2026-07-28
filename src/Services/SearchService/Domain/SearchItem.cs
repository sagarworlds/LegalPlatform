using System;

namespace SearchService.Domain;

public class SearchItem
{
    public string Id { get; set; } = string.Empty; 
    public Guid TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; 
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    public SearchItem(string id, Guid tenantId, string title, string description, string type)
    {
        Id = id;
        TenantId = tenantId;
        Title = title;
        Description = description;
        Type = type;
        LastModified = DateTime.UtcNow;
    }

    public SearchItem() { }
}
