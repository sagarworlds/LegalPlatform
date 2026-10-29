using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using UserService.Domain;
using UserService.Infrastructure;
using UserService.Models;
using ServiceDefaults.Security;

namespace UserService.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // These endpoints were anonymous: anyone could read, and overwrite, any user's profile by id.
        // Users without an organization must still reach their own profile, so the tenant claim is not required here.
        var group = endpoints.MapGroup("/api/v1/users").RequireAuthorization(Policies.AuthenticatedUser);

        group.MapGet("/{id:guid}", GetProfileAsync);
        group.MapPut("/{id:guid}", UpdateProfileAsync);

        return endpoints;
    }

    internal static async Task<IResult> GetProfileAsync(Guid id, UserContext user, UserDbContext dbContext)
    {
        if (!MayTouchProfile(user, id))
        {
            return Forbidden();
        }

        var profile = await dbContext.UserProfiles.FindAsync(id);
        if (profile == null)
        {
            return Results.NotFound();
        }

        if (!CanAccess(user, profile))
        {
            return Forbidden();
        }

        return Results.Ok(UserProfileResponse.From(profile));
    }

    public record UpdateProfileRequest(string FirstName, string LastName, string? PhoneNumber, string? AvatarUrl);

    internal static async Task<IResult> UpdateProfileAsync(Guid id, UpdateProfileRequest request, UserContext user, UserDbContext dbContext)
    {
        if (!MayTouchProfile(user, id))
        {
            return Forbidden();
        }

        var profile = await dbContext.UserProfiles.FindAsync(id);
        if (profile == null)
        {
            return Results.NotFound();
        }

        if (!CanAccess(user, profile))
        {
            return Forbidden();
        }

        // The columns are NOT NULL; a body that omits a name would otherwise fail at the database.
        profile.FirstName = request.FirstName ?? string.Empty;
        profile.LastName = request.LastName ?? string.Empty;
        profile.PhoneNumber = request.PhoneNumber;
        profile.AvatarUrl = request.AvatarUrl;

        await dbContext.SaveChangesAsync();

        return Results.Ok(UserProfileResponse.From(profile));
    }

    /// <summary>
    /// Cheap check made before the lookup so a caller who could never see the profile cannot probe which ids exist
    /// (they get 403 whether or not the id is real): only the owner or an administrator gets as far as a 404.
    /// </summary>
    private static bool MayTouchProfile(UserContext caller, Guid profileId) =>
        caller.UserId == profileId || caller.IsInAnyRole(Roles.SystemAdmin, Roles.OrganizationAdmin);

    /// <summary>The owner, a SystemAdmin, or an OrganizationAdmin of the profile's own organization.</summary>
    private static bool CanAccess(UserContext caller, UserProfile profile) =>
        caller.UserId == profile.Id
        || caller.IsInRole(Roles.SystemAdmin)
        || (caller.IsInRole(Roles.OrganizationAdmin) && caller.HasTenant && profile.OrganizationId == caller.TenantId);

    private static IResult Forbidden() => Results.Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Not allowed to access this profile",
        detail: "You can access your own profile; administrators can access profiles in their organization.");
}
