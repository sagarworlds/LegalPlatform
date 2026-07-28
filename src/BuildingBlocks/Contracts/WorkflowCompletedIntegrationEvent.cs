using System;
using EventBus.Events;

namespace Contracts;

public record WorkflowCompletedIntegrationEvent : IntegrationEvent
{
    public Guid WorkflowInstanceId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty; 
    public Guid TenantId { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public Guid EntityId { get; init; }
}
