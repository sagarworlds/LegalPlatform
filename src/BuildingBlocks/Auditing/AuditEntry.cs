using ServiceDefaults.Security;

namespace Auditing;

/// <summary>
/// What happened, to what, by whom. Request details (IP address, user agent, correlation id) and the originating
/// service name are added by <see cref="IAuditPublisher"/>, so callers state only the facts of the action.
/// </summary>
/// <param name="TenantId">Tenant the action belongs to; <see cref="Guid.Empty"/> for actions with no tenant, such as a failed login.</param>
/// <param name="UserId">Acting user; <see cref="Guid.Empty"/> when the actor is not authenticated.</param>
/// <param name="EntityType">Kind of thing acted on, for example <c>Case</c> or <c>User</c>.</param>
/// <param name="EntityId">Identifier of the thing acted on, or a marker such as <c>LoginFailed</c> when there is none.</param>
/// <param name="Action">Verb, for example <c>Create</c> or <c>Login</c>.</param>
/// <param name="NewValues">Human-readable description of the state after the action.</param>
/// <param name="OldValues">Human-readable description of the state before the action, if any.</param>
/// <param name="Success">False records the attempt as a failure outcome.</param>
public sealed record AuditEntry(
    Guid TenantId,
    Guid UserId,
    string EntityType,
    string EntityId,
    string Action,
    string NewValues,
    string OldValues = "",
    bool Success = true)
{
    /// <summary>A successful action by an authenticated caller, attributed to their tenant and user id.</summary>
    public static AuditEntry By(UserContext user, string entityType, string entityId, string action, string newValues, string oldValues = "") =>
        new(user.TenantId, user.UserId, entityType, entityId, action, newValues, oldValues);
}
