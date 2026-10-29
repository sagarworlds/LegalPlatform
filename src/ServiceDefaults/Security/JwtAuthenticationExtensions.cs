using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace ServiceDefaults.Security;

public static class JwtAuthenticationExtensions
{
    private const int MinimumSecretLength = 32;

    /// <summary>
    /// Adds JWT bearer authentication that validates tokens issued by IdentityService.
    /// Unlike the per-service copies it replaces, there is no built-in fallback secret: a missing
    /// <c>JwtSettings</c> value fails startup, so a misconfigured deployment can never silently accept
    /// tokens signed with a key that is published in source control.
    /// </summary>
    /// <exception cref="InvalidOperationException">A required <c>JwtSettings</c> value is missing, or the secret is shorter than 32 characters.</exception>
    public static IServiceCollection AddPlatformJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("JwtSettings");
        var secret = Require(settings, "Secret");
        var issuer = Require(settings, "Issuer");
        var audience = Require(settings, "Audience");

        if (secret.Length < MinimumSecretLength)
        {
            throw new InvalidOperationException(
                $"JwtSettings:Secret must be at least {MinimumSecretLength} characters long (current length: {secret.Length}).");
        }

        // Every default is set explicitly: ASP.NET Core Identity (used by IdentityService) registers its cookie scheme as the
        // default authenticate/challenge scheme, which would otherwise win over DefaultScheme and ignore bearer tokens.
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
                };
            });

        return services.AddPlatformAuthorization();
    }

    private static string Require(IConfigurationSection settings, string key)
    {
        var value = settings[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required configuration value 'JwtSettings:{key}'.");
        }

        return value;
    }
}
