using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;
using FluentAssertions;
using IdentityService.Domain;
using IdentityService.Infrastructure;

namespace Identity.Tests;

public class TokenServiceTests
{
    private readonly Mock<IConfiguration> _configMock;
    private readonly TokenService _tokenService;

    public TokenServiceTests()
    {
        _configMock = new Mock<IConfiguration>();
        
        _configMock.Setup(c => c["JwtSettings:Secret"]).Returns("super_secret_key_which_is_at_least_32_characters_long_for_security");
        _configMock.Setup(c => c["JwtSettings:ExpiryInMinutes"]).Returns("60");
        _configMock.Setup(c => c["JwtSettings:Issuer"]).Returns("LegalPlatform.Identity");
        _configMock.Setup(c => c["JwtSettings:Audience"]).Returns("LegalPlatform.Clients");

        _tokenService = new TokenService(_configMock.Object);
    }

    [Fact]
    public void GenerateAccessToken_ShouldReturnValidJwtToken()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "attorney@firm.com",
            UserName = "attorney@firm.com",
            FirstName = "Jane",
            LastName = "Doe",
            OrganizationId = Guid.NewGuid()
        };
        var roles = new List<string> { "Attorney" };

        // Act
        var token = _tokenService.GenerateAccessToken(user, roles);

        // Assert
        token.Should().NotBeNullOrEmpty();
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Issuer.Should().Be("LegalPlatform.Identity");
        jwtToken.Audiences.Should().Contain("LegalPlatform.Clients");
        jwtToken.Claims.Should().Contain(c => c.Type == "nameid" && c.Value == user.Id.ToString());
        jwtToken.Claims.Should().Contain(c => c.Type == "email" && c.Value == user.Email);
        jwtToken.Claims.Should().Contain(c => c.Type == "role" && c.Value == "Attorney");
    }
}
