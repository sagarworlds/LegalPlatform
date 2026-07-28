using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using IdentityService.Domain;
using IdentityService.Infrastructure;
using IdentityService.Models;
using EventBus.Abstractions;
using Contracts;

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

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        UserManager<ApplicationUser> userManager,
        IEventBus eventBus)
    {
        var userExists = await userManager.FindByEmailAsync(request.Email);
        if (userExists != null)
        {
            return Results.BadRequest(new { Message = "User already exists with this email." });
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            OrganizationId = request.OrganizationId,
            SecurityStamp = Guid.NewGuid().ToString()
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return Results.BadRequest(result.Errors);
        }

        // Add user role by default
        await userManager.AddToRoleAsync(user, "User");

        // Publish integration event for UserService
        await eventBus.PublishAsync(new UserRegisteredIntegrationEvent
        {
            UserId = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            OrganizationId = user.OrganizationId
        });

        return Results.Ok(new { Message = "User registered successfully." });
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            return Results.Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);
        var accessToken = tokenService.GenerateAccessToken(user, roles);
        var refreshToken = tokenService.GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        await userManager.UpdateAsync(user);

        return Results.Ok(new AuthResponse(accessToken, refreshToken, DateTime.UtcNow.AddMinutes(60)));
    }

    private static async Task<IResult> RefreshTokenAsync(
        RefreshTokenRequest request,
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService)
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

            return Results.Ok(new AuthResponse(newAccessToken, newRefreshToken, DateTime.UtcNow.AddMinutes(60)));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { Message = ex.Message });
        }
    }
}
