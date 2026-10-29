using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using IdentityService.Domain;
using IdentityService.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceDefaults.Security;
using ServiceEndpoints.Tests.Support;
using Xunit;

namespace ServiceEndpoints.Tests;

/// <summary>
/// Exercises the shared JWT + policy setup end to end: real tokens from IdentityService's TokenService, validated by the
/// same <c>AddPlatformJwtAuthentication</c> every secured service uses, behind a real (in-process) server.
/// </summary>
public class SecurityPipelineTests : IAsyncLifetime
{
    private const string Secret = "unit_test_secret_that_is_well_over_thirty_two_characters";
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(Settings(Secret));
        builder.Services.AddPlatformJwtAuthentication(builder.Configuration);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapGet("/tenant", (UserContext user) => Results.Ok(new { user.UserId, user.TenantId, Roles = user.Roles.ToArray() }))
            .RequireAuthorization(Policies.TenantMember);
        _app.MapGet("/self", (UserContext user) => Results.Ok(new { user.UserId, user.HasTenant }))
            .RequireAuthorization(Policies.AuthenticatedUser);
        _app.MapGet("/admin", () => Results.Ok("admin"))
            .RequireAuthorization(new AuthorizeAttribute { Roles = Roles.Admins });

        await _app.StartAsync();
        _client = ((TestServer)_app.Services.GetRequiredService<IServer>()).CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private static Dictionary<string, string?> Settings(string? secret) => new()
    {
        ["JwtSettings:Secret"] = secret,
        ["JwtSettings:Issuer"] = "LegalPlatform.Identity",
        ["JwtSettings:Audience"] = "LegalPlatform.Clients"
    };

    private static string Token(Guid? organizationId, string[] roles, string secret = Secret)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(Settings(secret)).Build();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = "user@firm.com", Email = "user@firm.com",
            FirstName = "Test", LastName = "User", OrganizationId = organizationId
        };
        return new TokenService(JwtSettings.FromConfiguration(config)).GenerateAccessToken(user, roles);
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task A_token_with_a_tenant_reaches_a_tenant_endpoint_with_all_of_its_roles()
    {
        var tenant = Guid.NewGuid();

        var response = await GetAsync("/tenant", Token(tenant, [Roles.Attorney, Roles.Paralegal]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("tenantId").GetGuid().Should().Be(tenant);
        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().BeEquivalentTo(Roles.Attorney, Roles.Paralegal);
    }

    [Fact]
    public async Task A_token_without_a_tenant_is_refused_by_a_tenant_endpoint_instead_of_becoming_the_empty_tenant()
    {
        var response = await GetAsync("/tenant", Token(organizationId: null, [Roles.User]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_without_a_tenant_can_still_reach_endpoints_that_do_not_need_one()
    {
        var response = await GetAsync("/self", Token(organizationId: null, [Roles.User]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("hasTenant").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_request_without_a_token_is_unauthorized()
    {
        (await GetAsync("/tenant", token: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_unauthorized()
    {
        var forged = Token(Guid.NewGuid(), [Roles.SystemAdmin], secret: "a_completely_different_secret_that_is_long_enough_123");

        (await GetAsync("/admin", forged)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(new[] { Roles.Attorney }, HttpStatusCode.Forbidden)]
    [InlineData(new[] { Roles.SystemAdmin }, HttpStatusCode.OK)]
    [InlineData(new[] { Roles.OrganizationAdmin }, HttpStatusCode.OK)]
    // The previous "first role claim" lookup made the outcome depend on claim order for users holding several roles.
    [InlineData(new[] { Roles.Attorney, Roles.SystemAdmin }, HttpStatusCode.OK)]
    [InlineData(new[] { Roles.SystemAdmin, Roles.Attorney }, HttpStatusCode.OK)]
    public async Task Role_restricted_endpoints_honour_any_of_the_users_roles(string[] roles, HttpStatusCode expected)
    {
        var response = await GetAsync("/admin", Token(Guid.NewGuid(), roles));

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public void Startup_fails_fast_when_the_signing_secret_is_missing()
    {
        var act = () => new ServiceCollection().AddPlatformJwtAuthentication(
            new ConfigurationBuilder().AddInMemoryCollection(Settings(secret: null)).Build());

        act.Should().Throw<InvalidOperationException>().WithMessage("*JwtSettings:Secret*");
    }

    [Fact]
    public void Startup_fails_fast_when_the_signing_secret_is_too_short()
    {
        var act = () => new ServiceCollection().AddPlatformJwtAuthentication(
            new ConfigurationBuilder().AddInMemoryCollection(Settings(secret: "too-short")).Build());

        act.Should().Throw<InvalidOperationException>().WithMessage("*at least 32*");
    }
}

public class UserContextTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));

    [Fact]
    public void Reads_user_tenant_and_every_role()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var principal = Principal(
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("organizationId", tenantId.ToString()),
            new Claim(ClaimTypes.Role, Roles.Attorney),
            new Claim("role", Roles.Paralegal));

        principal.TryGetUserContext(out var context).Should().BeTrue();

        context!.UserId.Should().Be(userId);
        context.TenantId.Should().Be(tenantId);
        context.HasTenant.Should().BeTrue();
        context.Roles.Should().BeEquivalentTo(Roles.Attorney, Roles.Paralegal);
    }

    [Fact]
    public void Accepts_the_alternate_tenant_claim_name()
    {
        var tenantId = Guid.NewGuid();
        var principal = Principal(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim("tenant_id", tenantId.ToString()));

        principal.TryGetUserContext(out var context).Should().BeTrue();

        context!.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void A_missing_or_malformed_tenant_claim_means_no_tenant_not_the_empty_tenant_by_accident()
    {
        var missing = Principal(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        var malformed = Principal(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim("organizationId", "not-a-guid"));

        missing.TryGetUserContext(out var a).Should().BeTrue();
        malformed.TryGetUserContext(out var b).Should().BeTrue();

        a!.HasTenant.Should().BeFalse();
        b!.HasTenant.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Without_a_valid_user_id_there_is_no_user_context(string? userId)
    {
        var principal = userId is null ? Principal() : Principal(new Claim(ClaimTypes.NameIdentifier, userId));

        principal.TryGetUserContext(out var context).Should().BeFalse();
        context.Should().BeNull();
    }

    [Fact]
    public void An_unauthenticated_principal_has_no_user_context()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        anonymous.TryGetUserContext(out _).Should().BeFalse();
    }

    [Fact]
    public void Every_audit_reader_role_is_a_role_identity_can_actually_issue()
    {
        new[] { Roles.SystemAdmin, Roles.ComplianceOfficer, Roles.SecurityAuditor, Roles.OrganizationAdmin }
            .Should().OnlyContain(r => Roles.IsKnown(r));
    }
}
