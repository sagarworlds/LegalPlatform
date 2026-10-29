using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ServiceDefaults.Security;

/// <summary>
/// The <c>JwtSettings</c> configuration section, read and validated once. IdentityService signs tokens with it and every
/// other service validates them with it, so both sides are guaranteed to agree on the key, issuer and audience.
/// </summary>
/// <remarks>
/// There are deliberately no built-in fallbacks for the secret, issuer or audience. The services used to fall back to a key
/// that is published in source control, so a missing setting silently produced tokens anyone could forge.
/// </remarks>
public sealed record JwtSettings(string Secret, string Issuer, string Audience, double ExpiryInMinutes)
{
    public const int MinimumSecretLength = 32;
    public const double DefaultExpiryInMinutes = 60;

    /// <exception cref="InvalidOperationException">
    /// A required value is missing, the secret is shorter than <see cref="MinimumSecretLength"/>, or the expiry is not a positive number.
    /// </exception>
    public static JwtSettings FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("JwtSettings");

        var secret = Require(section, "Secret");
        var issuer = Require(section, "Issuer");
        var audience = Require(section, "Audience");

        if (secret.Length < MinimumSecretLength)
        {
            throw new InvalidOperationException(
                $"JwtSettings:Secret must be at least {MinimumSecretLength} characters long (current length: {secret.Length}).");
        }

        return new JwtSettings(secret, issuer, audience, ReadExpiry(section));
    }

    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(Secret));

    /// <summary>
    /// Validation rules for tokens issued by IdentityService. <paramref name="validateLifetime"/> is only turned off to read
    /// the claims of an expired access token during a token refresh; the signature, issuer and audience are always checked.
    /// </summary>
    public TokenValidationParameters CreateValidationParameters(bool validateLifetime = true) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = validateLifetime,
        ValidateIssuerSigningKey = true,
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = SigningKey
    };

    private static string Require(IConfigurationSection section, string key)
    {
        var value = section[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required configuration value 'JwtSettings:{key}'.");
        }

        return value;
    }

    // Unlike the secret, a default for the lifetime is safe: it only decides how long a token lives.
    private static double ReadExpiry(IConfigurationSection section)
    {
        var raw = section["ExpiryInMinutes"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultExpiryInMinutes;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) || minutes <= 0)
        {
            throw new InvalidOperationException($"JwtSettings:ExpiryInMinutes must be a positive number of minutes (was '{raw}').");
        }

        return minutes;
    }
}
