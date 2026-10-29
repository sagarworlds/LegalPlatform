using OrganizationService.Domain;

namespace OrganizationService.Models;

/// <summary>Response shape for the Organization API; keeps the aggregate's internals (such as its domain-event list) out of the JSON.</summary>
public sealed record OrganizationResponse(Guid Id, string Name, string TenantId, string Domain, DateTime CreatedAt)
{
    public static OrganizationResponse From(Organization organization) =>
        new(organization.Id, organization.Name, organization.TenantId, organization.Domain, organization.CreatedAt);
}
