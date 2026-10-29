using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceDefaults.Security;
using Xunit;

namespace ServiceEndpoints.Tests;

public class JwtSettingsTests
{
    private const string Secret = "unit_test_secret_that_is_well_over_thirty_two_characters";

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>($"JwtSettings:{v.Key}", v.Value)))
            .Build();

    private static IConfiguration Valid(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Secret"] = Secret,
            ["Issuer"] = "LegalPlatform.Identity",
            ["Audience"] = "LegalPlatform.Clients"
        };
        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return Config(values.Select(kv => (kv.Key, kv.Value)).ToArray());
    }

    [Fact]
    public void Reads_secret_issuer_and_audience()
    {
        var settings = JwtSettings.FromConfiguration(Valid());

        settings.Secret.Should().Be(Secret);
        settings.Issuer.Should().Be("LegalPlatform.Identity");
        settings.Audience.Should().Be("LegalPlatform.Clients");
    }

    [Theory]
    [InlineData("Secret")]
    [InlineData("Issuer")]
    [InlineData("Audience")]
    public void A_missing_or_blank_required_value_fails_instead_of_falling_back_to_a_default(string key)
    {
        foreach (var value in new string?[] { null, "", "   " })
        {
            var act = () => JwtSettings.FromConfiguration(Valid((key, value)));

            act.Should().Throw<InvalidOperationException>().WithMessage($"*JwtSettings:{key}*");
        }
    }

    [Fact]
    public void A_secret_shorter_than_32_characters_is_rejected()
    {
        var act = () => JwtSettings.FromConfiguration(Valid(("Secret", new string('x', 31))));

        act.Should().Throw<InvalidOperationException>().WithMessage("*at least 32*");
    }

    [Fact]
    public void A_32_character_secret_is_accepted()
    {
        JwtSettings.FromConfiguration(Valid(("Secret", new string('x', 32)))).Secret.Should().HaveLength(32);
    }

    [Fact]
    public void The_lifetime_defaults_to_an_hour_when_not_configured()
    {
        JwtSettings.FromConfiguration(Valid()).ExpiryInMinutes.Should().Be(60);
    }

    [Theory]
    [InlineData("15", 15)]
    [InlineData("0.5", 0.5)]
    public void A_configured_lifetime_is_used(string configured, double expected)
    {
        JwtSettings.FromConfiguration(Valid(("ExpiryInMinutes", configured))).ExpiryInMinutes.Should().Be(expected);
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("0")]
    [InlineData("-5")]
    public void A_lifetime_that_is_not_a_positive_number_is_rejected(string configured)
    {
        var act = () => JwtSettings.FromConfiguration(Valid(("ExpiryInMinutes", configured)));

        act.Should().Throw<InvalidOperationException>().WithMessage("*ExpiryInMinutes*");
    }

    [Fact]
    public void Lifetime_validation_can_only_be_relaxed_explicitly_and_the_other_checks_stay_on()
    {
        var settings = JwtSettings.FromConfiguration(Valid());

        var strict = settings.CreateValidationParameters();
        var relaxed = settings.CreateValidationParameters(validateLifetime: false);

        strict.ValidateLifetime.Should().BeTrue();
        relaxed.ValidateLifetime.Should().BeFalse();
        new[] { strict, relaxed }.Should().OnlyContain(p =>
            p.ValidateIssuer && p.ValidateAudience && p.ValidateIssuerSigningKey &&
            p.ValidIssuer == "LegalPlatform.Identity" && p.ValidAudience == "LegalPlatform.Clients");
    }

    [Fact]
    public void Registering_authentication_makes_the_validated_settings_injectable_for_services_that_issue_tokens()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlatformJwtAuthentication(Valid());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<JwtSettings>().Secret.Should().Be(Secret);
    }
}
