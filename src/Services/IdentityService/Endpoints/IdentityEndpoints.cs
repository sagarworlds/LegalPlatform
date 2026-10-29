using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using IdentityService.Domain;
using IdentityService.Infrastructure;
using IdentityService.Models;
using EventBus.Abstractions;
using Contracts;
using ServiceDefaults.Security;

namespace IdentityService.Endpoints;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity");

        group.MapPost("/register", RegisterAsync);
        group.MapPost("/login", LoginAsync);
        group.MapPost("/refresh", RefreshTokenAsync);

        return endpoints;
    }

    /// <summary>
    /// Registration is open to anyone, but placing the new account in an organization is not: a client-supplied
    /// <c>OrganizationId</c> used to be trusted, so anyone could join any tenant by knowing its id. Now only a
    /// SystemAdmin, or an OrganizationAdmin of that same organization, may register someone into it. Everyone else
    /// gets an organization-less account, which the tenant-scoped services refuse until an administrator assigns one.
    /// </summary>
    internal static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        IEventBus eventBus,
        ILoggerFactory loggerFactory)
    {
        if (request.OrganizationId.HasValue && !CanPlaceUserInOrganization(httpContext.User, request.OrganizationId.Value))
        {
            await IdentityAudit.PublishAsync(eventBus, httpContext, Guid.Empty, Guid.Empty,
                entityId: "RegistrationDenied", action: "Register", oldValues: string.Empty,
                newValues: $"Email: {request.Email}, Reason: caller may not place users in organization {request.OrganizationId}",
                success: false);

            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Organization membership requires an administrator",
                detail: "Register without an organizationId, or sign in as an administrator of that organization.");
        }

        var userExists = await userManager.FindByEmailAsync(request.Email);
        if (userExists != null)
        {
            await IdentityAudit.PublishAsync(eventBus, httpContext, request.OrganizationId ?? Guid.Empty, Guid.Empty,
                entityId: "RegistrationFailed", action: "Register", oldValues: string.Empty,
                newValues: $"Email: {request.Email}, Reason: User already exists",
                success: false);

            return Results.BadRequest(new { Message = "User already exists with this email." });
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            // The request marks names optional but the columns are NOT NULL; a null reached the database and failed the insert.
            FirstName = request.FirstName ?? string.Empty,
            LastName = request.LastName ?? string.Empty,
            OrganizationId = request.OrganizationId,
            SecurityStamp = Guid.NewGuid().ToString()
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return Results.BadRequest(result.Errors);
        }

        var roleResult = await userManager.AddToRoleAsync(user, Roles.User);
        if (!roleResult.Succeeded)
        {
            // An account without even the base role would be unusable, and a retry would report "already exists".
            var logger = loggerFactory.CreateLogger(typeof(IdentityEndpoints).FullName!);
            logger.LogError("Could not assign role {Role} to new user {UserId}: {Errors}. Removing the account.",
                Roles.User, user.Id, string.Join("; ", roleResult.Errors.Select(e => e.Description)));
            await userManager.DeleteAsync(user);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Registration failed",
                detail: "The account could not be completed. Please try again later.");
        }

        await eventBus.PublishAsync(new UserRegisteredIntegrationEvent
        {
            UserId = user.Id,
            Email = request.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            OrganizationId = user.OrganizationId
        });

        await IdentityAudit.PublishAsync(eventBus, httpContext, user.OrganizationId ?? Guid.Empty, user.Id,
            entityId: user.Id.ToString(), action: "Register", oldValues: string.Empty,
            newValues: $"Email: {user.Email}",
            success: true);

        return Results.Ok(new { Message = "User registered successfully." });
    }

    private static bool CanPlaceUserInOrganization(ClaimsPrincipal caller, Guid organizationId)
    {
        if (!caller.TryGetUserContext(out var context))
        {
            return false;
        }

        return context.IsInRole(Roles.SystemAdmin)
            || (context.IsInRole(Roles.OrganizationAdmin) && context.TenantId == organizationId);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IEventBus eventBus)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            await IdentityAudit.PublishAsync(eventBus, httpContext, Guid.Empty, Guid.Empty,
                entityId: "LoginFailed", action: "Login", oldValues: string.Empty,
                newValues: $"Email: {request.Email}",
                success: false);

            return Results.Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);
        var accessToken = tokenService.GenerateAccessToken(user, roles);
        var refreshToken = tokenService.GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        await userManager.UpdateAsync(user);

        await IdentityAudit.PublishAsync(eventBus, httpContext, user.OrganizationId ?? Guid.Empty, user.Id,
            entityId: user.Id.ToString(), action: "Login", oldValues: string.Empty,
            newValues: $"Email: {user.Email}",
            success: true);

        return Results.Ok(new AuthResponse(accessToken, refreshToken, DateTime.UtcNow.AddMinutes(60)));
    }

    private static async Task<IResult> RefreshTokenAsync(
        RefreshTokenRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IEventBus eventBus)
    {
        try
        {
            var principal = tokenService.GetPrincipalFromExpiredToken(request.AccessToken);
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.BadRequest(new { Message = "Invalid access token." });
            }

            var user = await userManager.FindByIdAsync(userId);
            if (user == null || user.RefreshToken != request.RefreshToken || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
            {
                return Results.BadRequest(new { Message = "Invalid refresh token or session expired." });
            }

            var roles = await userManager.GetRolesAsync(user);
            var newAccessToken = tokenService.GenerateAccessToken(user, roles);
            var newRefreshToken = tokenService.GenerateRefreshToken();

            user.RefreshToken = newRefreshToken;
            await userManager.UpdateAsync(user);

            await IdentityAudit.PublishAsync(eventBus, httpContext, user.OrganizationId ?? Guid.Empty, user.Id,
                entityId: user.Id.ToString(), action: "RefreshToken", oldValues: string.Empty,
                newValues: $"Email: {user.Email}",
                success: true);

            return Results.Ok(new AuthResponse(newAccessToken, newRefreshToken, DateTime.UtcNow.AddMinutes(60)));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { Message = ex.Message });
        }
    }
}
