using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using IdentityService.Domain;
using IdentityService.Infrastructure;
using IdentityService.Models;
using EventBus.Abstractions;
using ServiceDefaults.Security;

namespace IdentityService.Endpoints;

/// <summary>
/// Administrative operations on existing accounts. Roles and organization membership decide what every other
/// service lets a user do, so only a SystemAdmin may change them.
/// </summary>
public static class UserAdministrationEndpoints
{
    public static IEndpointRouteBuilder MapUserAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/users")
            .RequireAuthorization(Policies.AuthenticatedUser)
            .RequireAuthorization(new AuthorizeAttribute { Roles = Roles.SystemAdmin });

        group.MapPut("/{id:guid}/roles", AssignRolesAsync);
        group.MapPut("/{id:guid}/organization", AssignOrganizationAsync);

        return endpoints;
    }

    /// <summary>
    /// Replaces the user's roles with exactly the requested set. Tokens already issued keep their old roles until
    /// they expire (60 minutes by default).
    /// </summary>
    internal static async Task<IResult> AssignRolesAsync(
        Guid id,
        AssignRolesRequest request,
        UserContext caller,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        IEventBus eventBus)
    {
        var requested = (request.Roles ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (requested.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["roles"] = ["At least one role is required."] });
        }

        var unknown = requested.Where(r => !Roles.IsKnown(r)).ToList();
        if (unknown.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["roles"] = [$"Unknown role(s): {string.Join(", ", unknown)}. Valid roles: {string.Join(", ", Roles.All)}."]
            });
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Results.NotFound();
        }

        // A SystemAdmin demoting themselves could leave the platform with nobody able to assign roles.
        if (id == caller.UserId && !requested.Contains(Roles.SystemAdmin))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Cannot remove your own SystemAdmin role",
                detail: "Ask another SystemAdmin to change your roles.");
        }

        var current = (await userManager.GetRolesAsync(user)).ToList();
        var toAdd = requested.Except(current, StringComparer.Ordinal).ToList();
        var toRemove = current.Except(requested, StringComparer.Ordinal).ToList();

        // Add before remove so a failure part-way never leaves the user with fewer permissions than either set.
        if (toAdd.Count > 0)
        {
            var added = await userManager.AddToRolesAsync(user, toAdd);
            if (!added.Succeeded)
            {
                return Results.BadRequest(added.Errors);
            }
        }

        if (toRemove.Count > 0)
        {
            var removed = await userManager.RemoveFromRolesAsync(user, toRemove);
            if (!removed.Succeeded)
            {
                return Results.BadRequest(removed.Errors);
            }
        }

        await IdentityAudit.PublishAsync(eventBus, httpContext, caller.TenantId, caller.UserId,
            entityId: user.Id.ToString(), action: "AssignRoles",
            oldValues: string.Join(",", current), newValues: string.Join(",", requested),
            success: true);

        return Results.Ok(new { UserId = user.Id, Roles = requested });
    }

    internal static async Task<IResult> AssignOrganizationAsync(
        Guid id,
        AssignOrganizationRequest request,
        UserContext caller,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        IEventBus eventBus)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["organizationId"] = ["A non-empty organizationId is required."] });
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Results.NotFound();
        }

        var previous = user.OrganizationId;
        user.OrganizationId = request.OrganizationId;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Results.BadRequest(result.Errors);
        }

        await IdentityAudit.PublishAsync(eventBus, httpContext, caller.TenantId, caller.UserId,
            entityId: user.Id.ToString(), action: "AssignOrganization",
            oldValues: previous?.ToString() ?? string.Empty, newValues: request.OrganizationId.ToString(),
            success: true);

        return Results.Ok(new { UserId = user.Id, OrganizationId = request.OrganizationId });
    }
}
