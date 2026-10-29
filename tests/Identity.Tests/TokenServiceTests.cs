using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using FluentAssertions;
using IdentityService.Domain;
using IdentityService.Infrastructure;
using ServiceDefaults.Security;

namespace Identity.Tests;

public class TokenServiceTests
{
    private const string Secret = "test_secret_key_which_is_at_least_32_characters_long";
    private const string Issuer = "LegalPlatform.Identity";
    private const string Audience = "LegalPlatform.Clients";

    private static readonly JwtSettings Settings = new(Secret, Issuer, Audience, ExpiryInMinutes: 60);

    private static ApplicationUser NewUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "attorney@firm.com",
        UserName = "attorney@firm.com",
        FirstName = "Jane",
        LastName = "Doe",
        OrganizationId = Guid.NewGuid()
    };

    // TokenService cannot mint an already-expired token (its NotBefore is "now"), so build one the way an old token would look.
    // It goes through the handler's CreateToken, as TokenService does, so the header carries the same "HS256" algorithm name.
    private static string ExpiredToken(ApplicationUser user)
    {
        var now = DateTime.UtcNow;
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()) }),
            NotBefore = now.AddMinutes(-70),
            Expires = now.AddMinutes(-10),
            Issuer = Issuer,
            Audience = Audience,
            SigningCredentials = new SigningCredentials(Settings.SigningKey, SecurityAlgorithms.HmacSha256Signature)
        });
        return handler.WriteToken(token);
    }

    [Fact]
    public void GenerateAccessToken_ShouldReturnValidJwtToken()
    {
        // Arrange
        var tokenService = new TokenService(Settings);
        var user = NewUser();
        var roles = new List<string> { "Attorney" };

        // Act
        var token = tokenService.GenerateAccessToken(user, roles);

        // Assert
        token.Should().NotBeNullOrEmpty();
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Issuer.Should().Be("LegalPlatform.Identity");
        jwtToken.Audiences.Should().Contain("LegalPlatform.Clients");
        jwtToken.Claims.Should().Contain(c => c.Type == "nameid" && c.Value == user.Id.ToString());
        jwtToken.Claims.Should().Contain(c => c.Type == "email" && c.Value == user.Email);
        jwtToken.Claims.Should().Contain(c => c.Type == "role" && c.Value == "Attorney");
        jwtToken.Claims.Should().Contain(c => c.Type == "organizationId" && c.Value == user.OrganizationId.ToString());
    }

    [Fact]
    public void GenerateAccessToken_UsesTheConfiguredLifetime()
    {
        var tokenService = new TokenService(Settings with { ExpiryInMinutes = 5 });

        var token = new JwtSecurityTokenHandler().ReadJwtToken(tokenService.GenerateAccessToken(NewUser(), new List<string>()));

        token.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void GenerateAccessToken_IsAcceptedByTheValidationRulesEveryServiceUses()
    {
        var token = new TokenService(Settings).GenerateAccessToken(NewUser(), new List<string> { "Attorney" });

        var principal = new JwtSecurityTokenHandler().ValidateToken(token, Settings.CreateValidationParameters(), out _);

        principal.Identity!.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_ReadsTheClaimsOfAnExpiredToken()
    {
        var user = NewUser();
        var expired = ExpiredToken(user);

        var principal = new TokenService(Settings).GetPrincipalFromExpiredToken(expired);

        principal.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(user.Id.ToString());

        // The same token must still be refused by normal request authentication.
        var act = () => new JwtSecurityTokenHandler().ValidateToken(expired, Settings.CreateValidationParameters(), out _);
        act.Should().Throw<SecurityTokenExpiredException>();
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_StillRejectsATokenSignedWithAnotherKey()
    {
        var forged = new TokenService(Settings with { Secret = "another_secret_key_which_is_at_least_32_characters_long" })
            .GenerateAccessToken(NewUser(), new List<string>());

        var act = () => new TokenService(Settings).GetPrincipalFromExpiredToken(forged);

        act.Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_StillRejectsAnotherIssuerOrAudience()
    {
        var service = new TokenService(Settings);

        var wrongIssuer = new TokenService(Settings with { Issuer = "Somebody.Else" }).GenerateAccessToken(NewUser(), new List<string>());
        var wrongAudience = new TokenService(Settings with { Audience = "Other.Clients" }).GenerateAccessToken(NewUser(), new List<string>());

        ((Action)(() => service.GetPrincipalFromExpiredToken(wrongIssuer))).Should().Throw<SecurityTokenException>();
        ((Action)(() => service.GetPrincipalFromExpiredToken(wrongAudience))).Should().Throw<SecurityTokenException>();
    }
}
