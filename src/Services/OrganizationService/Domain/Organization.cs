using SharedKernel.Domain;

namespace OrganizationService.Domain;

public class Organization : AggregateRoot<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Organization(Guid id, string name, string tenantId, string domain) : base(id)
    {
        Name = name;
        TenantId = tenantId;
        Domain = domain;
        CreatedAt = DateTime.UtcNow;
    }

#pragma warning disable CS8618
    protected Organization() { }
#pragma warning restore CS8618
}
