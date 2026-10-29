using Contracts;
using EventBus.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Auditing;

public sealed class AuditPublisherOptions
{
    /// <summary>Name recorded as <c>ServiceName</c> on every event this service publishes.</summary>
    public string ServiceName { get; set; } = string.Empty;
}

/// <summary>Maps an <see cref="AuditEntry"/> plus the current request to an <see cref="AuditLogCreatedIntegrationEvent"/> and publishes it.</summary>
public sealed class AuditPublisher : IAuditPublisher
{
    private const string Unknown = "Unknown";

    private readonly IEventBus _eventBus;
    private readonly string _serviceName;

    public AuditPublisher(IEventBus eventBus, IOptions<AuditPublisherOptions> options)
    {
        _eventBus = eventBus;
        _serviceName = options.Value.ServiceName;
    }

    public Task PublishAsync(HttpContext httpContext, AuditEntry entry, CancellationToken cancellationToken = default)
    {
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();

        return _eventBus.PublishAsync(new AuditLogCreatedIntegrationEvent
        {
            TenantId = entry.TenantId,
            UserId = entry.UserId,
            ServiceName = _serviceName,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Action = entry.Action,
            OldValues = entry.OldValues,
            NewValues = entry.NewValues,
            IPAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? Unknown,
            UserAgent = string.IsNullOrEmpty(userAgent) ? Unknown : userAgent,
            CorrelationId = httpContext.TraceIdentifier,
            TraceId = httpContext.TraceIdentifier,
            Outcome = entry.Success ? "Success" : "Failure"
        }, cancellationToken);
    }
}

public static class AuditPublisherServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IAuditPublisher"/>. Requires <c>AddEventBus</c>. The publisher is scoped because the event bus is.
    /// </summary>
    /// <param name="serviceName">The name events from this service are recorded under, for example <c>CaseService</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="serviceName"/> is blank.</exception>
    public static IServiceCollection AddAuditPublisher(this IServiceCollection services, string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        services.Configure<AuditPublisherOptions>(options => options.ServiceName = serviceName);
        services.TryAddScoped<IAuditPublisher, AuditPublisher>();
        return services;
    }
}
