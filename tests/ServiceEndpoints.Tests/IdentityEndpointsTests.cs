using Contracts;
using FluentAssertions;
using IdentityService.Domain;
using IdentityService.Endpoints;
using IdentityService.Infrastructure;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceDefaults.Security;
using ServiceEndpoints.Tests.Support;
using Xunit;

namespace ServiceEndpoints.Tests;

/// <summary>Real ASP.NET Core Identity (UserManager, RoleManager) over in-memory SQLite, with the same options as the service.</summary>
public sealed class IdentityHarness : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    public ServiceProvider Provider { get; }

    public IdentityHarness(Dictionary<string, string?>? configuration = null)
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration ?? []).Build());
        services.AddDbContext<IdentityDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentity<ApplicationUser, ApplicationRole>(o =>
            {
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.Password.RequiredLength = 6;
            })
            .AddEntityFrameworkStores<IdentityDbContext>();
        services.AddScoped<IdentityDataSeeder>();
        Provider = services.BuildServiceProvider();

        using var scope = Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.EnsureCreated();
    }

    public async Task SeedAsync()
    {
        using var scope = Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>().SeedAsync();
    }

    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Provider.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public void Dispose()
    {
        Provider.Dispose();
        _connection.Dispose();
    }
}

public class IdentityRegistrationTests : IDisposable
{
    private static readonly Guid OrgA = Guid.NewGuid();
    private static readonly Guid OrgB = Guid.NewGuid();

    private readonly IdentityHarness _identity = new();
    private readonly FakeEventBus _bus = new();

    public IdentityRegistrationTests()
    {
        _identity.SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _identity.Dispose();

    private Task<HttpResponseSnapshot> RegisterAsync(RegisterRequest request, UserContext? caller = null) =>
        _identity.WithScopeAsync(async services =>
        {
            var httpContext = TestHttp.NewContext(caller is null ? null : TestUsers.ToPrincipal(caller));
            var result = await IdentityEndpoints.RegisterAsync(
                request, httpContext, services.GetRequiredService<UserManager<ApplicationUser>>(), _bus, NullLoggerFactory.Instance);
            return await TestHttp.ExecuteAsync(result);
        });

    private Task<ApplicationUser?> FindAsync(string email) =>
        _identity.WithScopeAsync(s => s.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email));

    [Fact]
    public async Task Registering_without_names_or_organization_succeeds_and_creates_a_plain_user()
    {
        var response = await RegisterAsync(new RegisterRequest("new@firm.com", "secret1"));

        response.StatusCode.Should().Be(200, response.Body);

        var user = await FindAsync("new@firm.com");
        user!.FirstName.Should().BeEmpty();
        user.LastName.Should().BeEmpty();
        user.OrganizationId.Should().BeNull();
        (await _identity.WithScopeAsync(s => s.GetRequiredService<UserManager<ApplicationUser>>().GetRolesAsync(user)))
            .Should().Equal(Roles.User);
        _bus.Of<UserRegisteredIntegrationEvent>().Should().ContainSingle(e => e.Email == "new@firm.com");
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_register_into_an_organization()
    {
        var response = await RegisterAsync(new RegisterRequest("intruder@evil.com", "secret1", OrganizationId: OrgA));

        response.StatusCode.Should().Be(403);
        (await FindAsync("intruder@evil.com")).Should().BeNull("the account must not be created");
        _bus.Of<AuditLogCreatedIntegrationEvent>().Should().ContainSingle(e => e.Outcome == "Failure" && e.EntityId == "RegistrationDenied");
        _bus.Of<UserRegisteredIntegrationEvent>().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, new[] { Roles.User })]
    [InlineData(false, new[] { Roles.Attorney })]
    [InlineData(true, new[] { Roles.OrganizationAdmin })]
    public async Task Only_an_administrator_of_that_organization_can_register_into_it(bool differentOrganization, string[] roles)
    {
        // OrgB's administrator is not an administrator of OrgA; a non-admin of OrgA is not one either.
        var caller = TestUsers.Create(differentOrganization ? OrgB : OrgA, roles);

        var response = await RegisterAsync(new RegisterRequest("colleague@firm.com", "secret1", OrganizationId: OrgA), caller);

        response.StatusCode.Should().Be(403);
        (await FindAsync("colleague@firm.com")).Should().BeNull();
    }

    [Fact]
    public async Task An_organization_administrator_can_register_users_into_their_own_organization()
    {
        var admin = TestUsers.Create(OrgA, Roles.OrganizationAdmin);

        var response = await RegisterAsync(new RegisterRequest("colleague@firm.com", "secret1", "Col", "League", OrgA), admin);

        response.StatusCode.Should().Be(200, response.Body);
        (await FindAsync("colleague@firm.com"))!.OrganizationId.Should().Be(OrgA);
    }

    [Fact]
    public async Task A_system_administrator_can_register_users_into_any_organization()
    {
        var admin = TestUsers.Create(OrgB, Roles.SystemAdmin);

        var response = await RegisterAsync(new RegisterRequest("colleague@firm.com", "secret1", OrganizationId: OrgA), admin);

        response.StatusCode.Should().Be(200, response.Body);
        (await FindAsync("colleague@firm.com"))!.OrganizationId.Should().Be(OrgA);
    }

    [Fact]
    public async Task Registering_an_existing_email_is_rejected()
    {
        await RegisterAsync(new RegisterRequest("dup@firm.com", "secret1"));

        var response = await RegisterAsync(new RegisterRequest("dup@firm.com", "secret1"));

        response.StatusCode.Should().Be(400);
    }
}

public class IdentityAdministrationTests : IDisposable
{
    private readonly IdentityHarness _identity = new();
    private readonly FakeEventBus _bus = new();
    private readonly UserContext _sysAdmin = TestUsers.Create(Guid.NewGuid(), Roles.SystemAdmin);

    public IdentityAdministrationTests()
    {
        _identity.SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _identity.Dispose();

    private async Task<ApplicationUser> CreateUserAsync(string email, params string[] roles)
    {
        return await _identity.WithScopeAsync(async s =>
        {
            var manager = s.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, FirstName = "A", LastName = "B" };
            (await manager.CreateAsync(user, "secret1")).Succeeded.Should().BeTrue();
            if (roles.Length > 0)
            {
                (await manager.AddToRolesAsync(user, roles)).Succeeded.Should().BeTrue();
            }

            return user;
        });
    }

    private Task<HttpResponseSnapshot> AssignRolesAsync(UserContext caller, Guid userId, params string[] roles) =>
        _identity.WithScopeAsync(async s =>
        {
            var result = await UserAdministrationEndpoints.AssignRolesAsync(
                userId, new AssignRolesRequest(roles), caller, TestHttp.NewContext(), s.GetRequiredService<UserManager<ApplicationUser>>(), _bus);
            return await TestHttp.ExecuteAsync(result);
        });

    private Task<IList<string>> RolesOfAsync(ApplicationUser user) =>
        _identity.WithScopeAsync(s => s.GetRequiredService<UserManager<ApplicationUser>>().GetRolesAsync(user));

    [Fact]
    public async Task Assigning_roles_replaces_the_users_roles_with_exactly_the_requested_set()
    {
        var user = await CreateUserAsync("u@firm.com", Roles.User, Roles.Paralegal);

        var response = await AssignRolesAsync(_sysAdmin, user.Id, Roles.User, Roles.Attorney);

        response.StatusCode.Should().Be(200, response.Body);
        (await RolesOfAsync(user)).Should().BeEquivalentTo(Roles.User, Roles.Attorney);
        _bus.Of<AuditLogCreatedIntegrationEvent>().Should().ContainSingle(e => e.Action == "AssignRoles" && e.EntityId == user.Id.ToString());
    }

    [Fact]
    public async Task Assigning_an_unknown_role_is_rejected_and_changes_nothing()
    {
        var user = await CreateUserAsync("u@firm.com", Roles.User);

        var response = await AssignRolesAsync(_sysAdmin, user.Id, Roles.User, "Wizard");

        response.StatusCode.Should().Be(400);
        (await RolesOfAsync(user)).Should().Equal(Roles.User);
    }

    [Fact]
    public async Task Assigning_no_roles_is_rejected()
    {
        var user = await CreateUserAsync("u@firm.com", Roles.User);

        (await AssignRolesAsync(_sysAdmin, user.Id)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task A_system_administrator_cannot_remove_their_own_system_admin_role()
    {
        var admin = await CreateUserAsync("admin@firm.com", Roles.SystemAdmin);
        var self = new UserContext(admin.Id, Guid.NewGuid(), new HashSet<string> { Roles.SystemAdmin });

        var response = await AssignRolesAsync(self, admin.Id, Roles.User);

        response.StatusCode.Should().Be(400);
        (await RolesOfAsync(admin)).Should().Contain(Roles.SystemAdmin);
    }

    [Fact]
    public async Task Assigning_roles_to_a_missing_user_is_not_found()
    {
        (await AssignRolesAsync(_sysAdmin, Guid.NewGuid(), Roles.User)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Assigning_an_organization_updates_the_user()
    {
        var user = await CreateUserAsync("u@firm.com", Roles.User);
        var organizationId = Guid.NewGuid();

        var response = await _identity.WithScopeAsync(async s => await TestHttp.ExecuteAsync(
            await UserAdministrationEndpoints.AssignOrganizationAsync(
                user.Id, new AssignOrganizationRequest(organizationId), _sysAdmin, TestHttp.NewContext(),
                s.GetRequiredService<UserManager<ApplicationUser>>(), _bus)));

        response.StatusCode.Should().Be(200, response.Body);
        (await _identity.WithScopeAsync(s => s.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(user.Id.ToString())))!
            .OrganizationId.Should().Be(organizationId);
    }

    [Fact]
    public async Task Assigning_the_empty_organization_is_rejected()
    {
        var user = await CreateUserAsync("u@firm.com", Roles.User);

        var response = await _identity.WithScopeAsync(async s => await TestHttp.ExecuteAsync(
            await UserAdministrationEndpoints.AssignOrganizationAsync(
                user.Id, new AssignOrganizationRequest(Guid.Empty), _sysAdmin, TestHttp.NewContext(),
                s.GetRequiredService<UserManager<ApplicationUser>>(), _bus)));

        response.StatusCode.Should().Be(400);
    }
}

public class IdentityDataSeederTests
{
    [Fact]
    public async Task Seeding_creates_every_platform_role_and_is_safe_to_repeat()
    {
        using var identity = new IdentityHarness();

        await identity.SeedAsync();
        await identity.SeedAsync();

        var roles = await identity.WithScopeAsync(async s =>
            await s.GetRequiredService<RoleManager<ApplicationRole>>().Roles.Select(r => r.Name!).ToListAsync());
        roles.Should().BeEquivalentTo(Roles.All);
    }

    [Fact]
    public async Task A_configured_bootstrap_admin_is_created_once_with_the_system_admin_role_and_its_organization()
    {
        var organizationId = Guid.NewGuid();
        using var identity = new IdentityHarness(new()
        {
            ["BootstrapAdmin:Email"] = "admin@legal.com",
            ["BootstrapAdmin:Password"] = "devpassword123",
            ["BootstrapAdmin:OrganizationId"] = organizationId.ToString()
        });

        await identity.SeedAsync();
        await identity.SeedAsync();

        var (admin, roles, count) = await identity.WithScopeAsync(async s =>
        {
            var manager = s.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await manager.FindByEmailAsync("admin@legal.com");
            return (user, await manager.GetRolesAsync(user!), await manager.Users.CountAsync());
        });

        admin!.OrganizationId.Should().Be(organizationId);
        roles.Should().BeEquivalentTo(Roles.SystemAdmin, Roles.User);
        count.Should().Be(1);
    }

    [Fact]
    public async Task Without_bootstrap_configuration_no_user_is_created()
    {
        using var identity = new IdentityHarness();

        await identity.SeedAsync();

        (await identity.WithScopeAsync(async s => await s.GetRequiredService<UserManager<ApplicationUser>>().Users.CountAsync()))
            .Should().Be(0);
    }

    [Fact]
    public async Task An_invalid_bootstrap_password_fails_startup_instead_of_silently_skipping_the_admin()
    {
        using var identity = new IdentityHarness(new()
        {
            ["BootstrapAdmin:Email"] = "admin@legal.com",
            ["BootstrapAdmin:Password"] = "123"
        });

        var seed = () => identity.SeedAsync();

        await seed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*bootstrap admin*");
    }
}
