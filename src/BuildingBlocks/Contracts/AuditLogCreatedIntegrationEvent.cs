using System;
using EventBus.Events;

namespace Contracts;

public record AuditLogCreatedIntegrationEvent : IntegrationEvent
{
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public string ServiceName { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string OldValues { get; init; } = string.Empty;
    public string NewValues { get; init; } = string.Empty;
    public string IPAddress { get; init; } = string.Empty;
    public string UserAgent { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty; // Success, Failure
}
