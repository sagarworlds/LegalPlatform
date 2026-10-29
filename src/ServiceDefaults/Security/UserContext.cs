using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ServiceDefaults.Security;

/// <summary>
/// The authenticated caller, resolved once from JWT claims. Handlers take this as a parameter instead
/// of re-parsing claims, so a missing or malformed claim can never degrade to <see cref="Guid.Empty"/>
/// and quietly put different callers into one shared tenant.
/// </summary>
public sealed record UserContext(Guid UserId, Guid TenantId, IReadOnlySet<string> Roles)
{
    /// <summary>False for users who registered without an organization.</summary>
    public bool HasTenant => TenantId != Guid.Empty;

    public bool IsInRole(string role) => Roles.Contains(role);

    public bool IsInAnyRole(params string[] roles) => roles.Any(Roles.Contains);

    /// <summary>
    /// Minimal-API parameter binding. The <see cref="Policies"/> applied on the route group guarantee the
    /// claims are valid before the handler runs; if a route forgets the policy, binding fails loudly (400)
    /// rather than handing the handler an empty identity.
    /// </summary>
    public static ValueTask<UserContext?> BindAsync(HttpContext httpContext)
        => ValueTask.FromResult(httpContext.User.TryGetUserContext(out var context) ? context : null);
}

public static class ClaimsPrincipalExtensions
{
    private const string TenantClaim = "organizationId";
    private const string AlternateTenantClaim = "tenant_id";
    private const string RoleClaimShortName = "role";

    /// <summary>
    /// Builds a <see cref="UserContext"/> from the principal. Returns false when the user id claim is
    /// missing or not a non-empty GUID. A missing tenant claim is allowed here (<see cref="UserContext.HasTenant"/>
    /// is false) because some endpoints, such as a user reading their own profile, do not need one.
    /// </summary>
    public static bool TryGetUserContext(this ClaimsPrincipal principal, [NotNullWhen(true)] out UserContext? context)
    {
        context = null;

        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            userId == Guid.Empty)
        {
            return false;
        }

        var tenantValue = principal.FindFirstValue(TenantClaim) ?? principal.FindFirstValue(AlternateTenantClaim);
        var tenantId = Guid.TryParse(tenantValue, out var parsedTenant) ? parsedTenant : Guid.Empty;

        // A user can hold several roles; the previous "first role claim" lookup made authorization depend on claim order.
        var roles = principal.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == RoleClaimShortName)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        context = new UserContext(userId, tenantId, roles);
        return true;
    }
}
