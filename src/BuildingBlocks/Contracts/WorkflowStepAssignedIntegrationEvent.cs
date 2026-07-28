using System;
using EventBus.Events;

namespace Contracts;

public record WorkflowStepAssignedIntegrationEvent : IntegrationEvent
{
    public Guid WorkflowInstanceId { get; init; }
    public Guid StepId { get; init; }
    public string StepName { get; init; } = string.Empty;
    public string AssignedRole { get; init; } = string.Empty;
    public Guid TenantId { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public Guid EntityId { get; init; }
}
