using Microsoft.AspNetCore.Http;
using Contracts;
using EventBus.Abstractions;

namespace IdentityService.Infrastructure;

/// <summary>Builds and publishes the audit event every identity operation emits, so the payload is defined in one place.</summary>
internal static class IdentityAudit
{
    public static Task PublishAsync(
        IEventBus eventBus,
        HttpContext httpContext,
        Guid tenantId,
        Guid userId,
        string entityId,
        string action,
        string oldValues,
        string newValues,
        bool success) =>
        eventBus.PublishAsync(new AuditLogCreatedIntegrationEvent
        {
            TenantId = tenantId,
            UserId = userId,
            ServiceName = "IdentityService",
            EntityType = "User",
            EntityId = entityId,
            Action = action,
            OldValues = oldValues,
            NewValues = newValues,
            IPAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            UserAgent = httpContext.Request.Headers["User-Agent"].ToString() ?? "Unknown",
            CorrelationId = httpContext.TraceIdentifier,
            TraceId = httpContext.TraceIdentifier,
            Outcome = success ? "Success" : "Failure"
        });
}
