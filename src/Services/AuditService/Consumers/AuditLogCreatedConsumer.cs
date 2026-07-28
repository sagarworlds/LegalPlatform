using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MassTransit;
using Contracts;
using AuditService.Domain;
using AuditService.Infrastructure;

namespace AuditService.Consumers;

public class AuditLogCreatedConsumer : IConsumer<AuditLogCreatedIntegrationEvent>
{
    private readonly AuditDbContext _dbContext;

    public AuditLogCreatedConsumer(AuditDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<AuditLogCreatedIntegrationEvent> context)
    {
        var @event = context.Message;

        var log = new AuditLog(
            Guid.NewGuid(),
            @event.TenantId,
            @event.UserId,
            @event.ServiceName,
            @event.EntityType,
            @event.EntityId,
            @event.Action,
            @event.OldValues,
            @event.NewValues,
            @event.IPAddress,
            @event.UserAgent,
            @event.CorrelationId,
            @event.TraceId,
            @event.Outcome,
            hash: string.Empty
        );

        log.Hash = ComputeRecordHash(log);

        _dbContext.AuditLogs.Add(log);
        await _dbContext.SaveChangesAsync();
    }

    private string ComputeRecordHash(AuditLog log)
    {
        var rawData = $"{log.Timestamp:O}|{log.TenantId}|{log.UserId}|{log.ServiceName}|{log.EntityType}|{log.EntityId}|{log.Action}|{log.OldValues}|{log.NewValues}|{log.IPAddress}|{log.UserAgent}|{log.CorrelationId}|{log.TraceId}|{log.Outcome}";
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
