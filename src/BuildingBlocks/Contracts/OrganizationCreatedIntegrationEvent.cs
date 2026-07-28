using EventBus.Events;

namespace Contracts;

public record OrganizationCreatedIntegrationEvent : IntegrationEvent
{
    public Guid OrganizationId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
}
