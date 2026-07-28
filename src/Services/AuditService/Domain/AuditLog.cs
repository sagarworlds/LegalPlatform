using System;
using SharedKernel.Domain;

namespace AuditService.Domain;

public class AuditLog : Entity<Guid>
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; 
    public string OldValues { get; set; } = string.Empty; 
    public string NewValues { get; set; } = string.Empty; 
    public string IPAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty; 
    public string Hash { get; set; } = string.Empty; 

    public AuditLog(Guid id, Guid tenantId, Guid userId, string serviceName, string entityType, string entityId, string action, string oldValues, string newValues, string ipAddress, string userAgent, string correlationId, string traceId, string outcome, string hash) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        ServiceName = serviceName;
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        OldValues = oldValues;
        NewValues = newValues;
        IPAddress = ipAddress;
        UserAgent = userAgent;
        CorrelationId = correlationId;
        TraceId = traceId;
        Outcome = outcome;
        Hash = hash;
        Timestamp = DateTime.UtcNow;
    }

#pragma warning disable CS8618
    protected AuditLog() { }
#pragma warning restore CS8618
}
