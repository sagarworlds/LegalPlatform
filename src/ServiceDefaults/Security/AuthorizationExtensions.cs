using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ServiceDefaults.Security;

/// <summary>Names of the shared authorization policies registered by <see cref="AuthorizationExtensions.AddPlatformAuthorization"/>.</summary>
public static class Policies
{
    /// <summary>Authenticated, valid user id, and a valid organization (tenant) claim.</summary>
    public const string TenantMember = nameof(TenantMember);

    /// <summary>Authenticated with a valid user id; the tenant claim is not required.</summary>
    public const string AuthenticatedUser = nameof(AuthenticatedUser);
}

public static class AuthorizationExtensions
{
    /// <summary>
    /// Registers the tenant-aware policies. Enforcing the tenant claim in the authorization pipeline means
    /// a token without an organization is answered with 403 before any handler runs, instead of being
    /// treated as the shared <see cref="Guid.Empty"/> tenant.
    /// </summary>
    public static IServiceCollection AddPlatformAuthorization(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, UserContextAuthorizationHandler>());

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.TenantMember, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new UserContextRequirement(requireTenant: true)))
            .AddPolicy(Policies.AuthenticatedUser, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new UserContextRequirement(requireTenant: false)));

        return services;
    }
}

internal sealed class UserContextRequirement : IAuthorizationRequirement
{
    public UserContextRequirement(bool requireTenant)
    {
        RequireTenant = requireTenant;
    }

    public bool RequireTenant { get; }
}

internal sealed class UserContextAuthorizationHandler : AuthorizationHandler<UserContextRequirement>
{
    private readonly ILogger<UserContextAuthorizationHandler> _logger;

    public UserContextAuthorizationHandler(ILogger<UserContextAuthorizationHandler> logger)
    {
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, UserContextRequirement requirement)
    {
        if (!context.User.TryGetUserContext(out var user))
        {
            _logger.LogWarning("Access denied: token has no valid user id claim.");
            return Task.CompletedTask;
        }

        if (requirement.RequireTenant && !user.HasTenant)
        {
            _logger.LogWarning("Access denied for user {UserId}: token has no organization (tenant) claim.", user.UserId);
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
