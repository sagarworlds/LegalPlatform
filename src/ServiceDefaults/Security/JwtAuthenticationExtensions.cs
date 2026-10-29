using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceDefaults.Security;

public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// Adds JWT bearer authentication that validates tokens issued by IdentityService, and registers the validated
    /// <see cref="JwtSettings"/> so services that issue tokens (IdentityService) can inject the same values.
    /// </summary>
    /// <exception cref="InvalidOperationException">See <see cref="JwtSettings.FromConfiguration"/>; raised at startup.</exception>
    public static IServiceCollection AddPlatformJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = JwtSettings.FromConfiguration(configuration);
        services.AddSingleton(settings);

        // Every default is set explicitly: ASP.NET Core Identity (used by IdentityService) registers its cookie scheme as the
        // default authenticate/challenge scheme, which would otherwise win over DefaultScheme and ignore bearer tokens.
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options => options.TokenValidationParameters = settings.CreateValidationParameters());

        return services.AddPlatformAuthorization();
    }
}
