using Microsoft.AspNetCore.Http;

namespace Auditing;

/// <summary>
/// Publishes an audit event for the current request. Every service records its audit trail through this one
/// abstraction instead of building the integration event by hand, so the event shape and the request details captured
/// with it cannot drift between services.
/// </summary>
public interface IAuditPublisher
{
    /// <param name="httpContext">The request the action was performed in; supplies the caller's IP address, user agent and correlation id.</param>
    /// <param name="entry">The facts of the action.</param>
    /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
    Task PublishAsync(HttpContext httpContext, AuditEntry entry, CancellationToken cancellationToken = default);
}
