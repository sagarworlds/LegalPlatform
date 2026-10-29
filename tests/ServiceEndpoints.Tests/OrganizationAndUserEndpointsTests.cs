using FluentAssertions;
using OrganizationService.Endpoints;
using OrganizationService.Infrastructure;
using ServiceDefaults.Security;
using ServiceEndpoints.Tests.Support;
using UserService.Domain;
using UserService.Endpoints;
using UserService.Infrastructure;
using Xunit;

namespace ServiceEndpoints.Tests;

public class OrganizationEndpointsTests : IDisposable
{
    private readonly SqliteDatabase<OrganizationDbContext> _database = new();
    private readonly FakeEventBus _bus = new();

    public void Dispose() => _database.Dispose();

    private async Task<Guid> CreateOrganizationAsync(string tenantId = "acme")
    {
        using var db = _database.NewContext();
        var result = await OrganizationEndpoints.CreateOrganizationAsync(
            new OrganizationEndpoints.CreateOrganizationRequest("Acme LLP", tenantId, "acme.com"), db, _bus);
        var response = await TestHttp.ExecuteAsync(result);
        response.StatusCode.Should().Be(201, response.Body);
        return response.Json.GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseSnapshot> GetAsync(UserContext user, Guid id)
    {
        using var db = _database.NewContext();
        return await TestHttp.ExecuteAsync(await OrganizationEndpoints.GetOrganizationAsync(id, user, db));
    }

    [Fact]
    public async Task Creating_an_organization_returns_a_response_without_aggregate_internals()
    {
        using var db = _database.NewContext();
        var result = await OrganizationEndpoints.CreateOrganizationAsync(
            new OrganizationEndpoints.CreateOrganizationRequest("Acme LLP", "acme", "acme.com"), db, _bus);
        var response = await TestHttp.ExecuteAsync(result);

        response.StatusCode.Should().Be(201);
        response.Body.Should().NotContainEquivalentOf("domainEvents");
        _bus.Of<Contracts.OrganizationCreatedIntegrationEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task A_member_can_read_their_own_organization()
    {
        var id = await CreateOrganizationAsync();

        (await GetAsync(TestUsers.Create(id, Roles.Attorney), id)).StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task A_member_of_another_organization_cannot_read_it()
    {
        var id = await CreateOrganizationAsync();

        (await GetAsync(TestUsers.Create(Guid.NewGuid(), Roles.OrganizationAdmin), id)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task A_user_without_an_organization_cannot_read_one()
    {
        var id = await CreateOrganizationAsync();

        (await GetAsync(TestUsers.Create(tenantId: null, Roles.User), id)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task A_system_administrator_can_read_any_organization_and_gets_404_for_a_missing_one()
    {
        var id = await CreateOrganizationAsync();
        var sysAdmin = TestUsers.Create(Guid.NewGuid(), Roles.SystemAdmin);

        (await GetAsync(sysAdmin, id)).StatusCode.Should().Be(200);
        (await GetAsync(sysAdmin, Guid.NewGuid())).StatusCode.Should().Be(404);
    }
}

public class UserEndpointsTests : IDisposable
{
    private static readonly Guid OrgA = Guid.NewGuid();
    private static readonly Guid OrgB = Guid.NewGuid();

    private readonly SqliteDatabase<UserDbContext> _database = new();

    public void Dispose() => _database.Dispose();

    private Guid AddProfile(Guid? organizationId)
    {
        using var db = _database.NewContext();
        var profile = new UserProfile(Guid.NewGuid(), "person@firm.com", "Pat", "Person", organizationId);
        db.UserProfiles.Add(profile);
        db.SaveChanges();
        return profile.Id;
    }

    private async Task<HttpResponseSnapshot> GetAsync(UserContext caller, Guid id)
    {
        using var db = _database.NewContext();
        return await TestHttp.ExecuteAsync(await UserEndpoints.GetProfileAsync(id, caller, db));
    }

    private async Task<HttpResponseSnapshot> UpdateAsync(UserContext caller, Guid id)
    {
        using var db = _database.NewContext();
        return await TestHttp.ExecuteAsync(await UserEndpoints.UpdateProfileAsync(
            id, new UserEndpoints.UpdateProfileRequest("New", "Name", "555-0100", null), caller, db));
    }

    private static UserContext Self(Guid id, Guid? tenant, params string[] roles) =>
        new(id, tenant ?? Guid.Empty, roles.ToHashSet(StringComparer.Ordinal));

    [Fact]
    public async Task Users_can_read_and_update_their_own_profile_even_without_an_organization()
    {
        var id = AddProfile(organizationId: null);
        var self = Self(id, tenant: null, Roles.User);

        var read = await GetAsync(self, id);
        read.StatusCode.Should().Be(200);
        read.Body.Should().NotContainEquivalentOf("domainEvents");

        (await UpdateAsync(self, id)).StatusCode.Should().Be(200);
        (await GetAsync(self, id)).Json.GetProperty("firstName").GetString().Should().Be("New");
    }

    [Fact]
    public async Task Another_user_cannot_read_or_overwrite_the_profile()
    {
        var id = AddProfile(OrgA);
        var other = TestUsers.Create(OrgA, Roles.Attorney);

        (await GetAsync(other, id)).StatusCode.Should().Be(403);
        (await UpdateAsync(other, id)).StatusCode.Should().Be(403);

        (await GetAsync(Self(id, OrgA, Roles.User), id)).Json.GetProperty("firstName").GetString().Should().Be("Pat");
    }

    [Fact]
    public async Task A_non_admin_gets_403_whether_or_not_the_id_exists()
    {
        var other = TestUsers.Create(OrgA, Roles.Attorney);

        (await GetAsync(other, Guid.NewGuid())).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task An_organization_administrator_can_manage_profiles_in_their_organization_only()
    {
        var inOrg = AddProfile(OrgA);
        var elsewhere = AddProfile(OrgB);
        var admin = TestUsers.Create(OrgA, Roles.OrganizationAdmin);

        (await GetAsync(admin, inOrg)).StatusCode.Should().Be(200);
        (await UpdateAsync(admin, inOrg)).StatusCode.Should().Be(200);
        (await GetAsync(admin, elsewhere)).StatusCode.Should().Be(403);
        (await UpdateAsync(admin, elsewhere)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task A_system_administrator_can_manage_any_profile_and_gets_404_for_a_missing_one()
    {
        var id = AddProfile(OrgB);
        var sysAdmin = TestUsers.Create(OrgA, Roles.SystemAdmin);

        (await GetAsync(sysAdmin, id)).StatusCode.Should().Be(200);
        (await UpdateAsync(sysAdmin, id)).StatusCode.Should().Be(200);
        (await GetAsync(sysAdmin, Guid.NewGuid())).StatusCode.Should().Be(404);
    }
}
