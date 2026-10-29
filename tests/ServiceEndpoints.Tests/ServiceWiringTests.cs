using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Auditing;
using FluentAssertions;
using IdentityService.Domain;
using IdentityService.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ServiceDefaults.Security;
using Xunit;

namespace ServiceEndpoints.Tests;

/// <summary>
/// Boots each real service host (its actual Program.cs) in-process. The handler tests build their dependencies by hand, so
/// they cannot notice a service that forgot to register the audit publisher, or that maps endpoints before authentication.
/// The Production environment is used so the Development-only database creation and seeding is skipped; database and broker
/// connections are lazy, so nothing here needs Postgres or RabbitMQ.
/// </summary>
/// <remarks>
/// Tokens are minted from the <see cref="JwtSettings"/> the running host registered, not from test configuration: with minimal
/// hosting, configuration added through WebApplicationFactory is applied after Program.cs has already read its settings.
/// </remarks>
public sealed class ServiceWiringTests : IClassFixture<ServiceHostFixture>
{
    private static readonly Guid SomeId = Guid.NewGuid();
    private static readonly HttpMethod Get = HttpMethod.Get;
    private static readonly HttpMethod Put = HttpMethod.Put;

    private readonly ServiceHostFixture _hosts;

    public ServiceWiringTests(ServiceHostFixture hosts)
    {
        _hosts = hosts;
    }

    private async Task<T> WithServiceAsync<T>(string service, Func<HttpClient, IServiceProvider, Task<T>> action)
    {
        var running = await _hosts.GetAsync(service);
        return await action(running.Client, running.Services);
    }

    private static string Token(JwtSettings settings, Guid? organizationId, params string[] roles)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = "u@firm.com", Email = "u@firm.com", FirstName = "T", LastName = "U",
            OrganizationId = organizationId
        };
        return new TokenService(settings).GenerateAccessToken(user, roles);
    }

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, HttpMethod method, string path, string? token)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // PUT routes need a body to get past binding; the authorization checks under test run before the handler.
        if (method == Put)
        {
            request.Content = JsonContent.Create(new { roles = new[] { Roles.User } });
        }

        return (await client.SendAsync(request)).StatusCode;
    }

    private Task<HttpStatusCode> SendWithTokenAsync(string service, HttpMethod method, string path, Func<JwtSettings, string?> token) =>
        WithServiceAsync(service, (client, services) =>
            SendAsync(client, method, path, token(services.GetRequiredService<JwtSettings>())));

    /// <summary>Routes guarded by the shared TenantMember policy.</summary>
    public static TheoryData<string, string> TenantScopedRoutes => new()
    {
        { "Case", "/api/v1/cases" },
        { "Document", $"/api/v1/documents/{SomeId}/metadata" },
        { "Workflow", "/api/v1/workflows/templates" },
        { "Notification", "/api/v1/notifications" },
        { "Reporting", "/api/v1/reports/dashboard" },
        { "Search", "/api/v1/search?q=anything" },
    };

    /// <summary>Every secured service with one of its protected routes and the HTTP method that route uses.</summary>
    public static TheoryData<string, string, string> AllSecuredRoutes
    {
        get
        {
            var data = new TheoryData<string, string, string>
            {
                { "Audit", "GET", "/api/v1/audit" },
                { "Organization", "GET", $"/api/v1/organizations/{SomeId}" },
                { "User", "GET", $"/api/v1/users/{SomeId}" },
                { "Identity", "PUT", $"/api/v1/identity/users/{SomeId}/roles" },
            };
            foreach (var row in TenantScopedRoutes)
            {
                data.Add((string)row[0], "GET", (string)row[1]);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllSecuredRoutes))]
    public async Task The_real_host_starts_and_rejects_requests_without_a_token(string service, string method, string path)
    {
        var status = await SendWithTokenAsync(service, new HttpMethod(method), path, _ => null);

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(AllSecuredRoutes))]
    public async Task A_token_signed_with_another_key_is_rejected_by_every_service(string service, string method, string path)
    {
        var status = await SendWithTokenAsync(service, new HttpMethod(method), path, real =>
        {
            var other = real with { Secret = real.Secret + "_not_the_real_key" };
            return Token(other, Guid.NewGuid(), Roles.SystemAdmin);
        });

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(TenantScopedRoutes))]
    public async Task A_valid_token_without_a_tenant_is_refused_by_every_tenant_scoped_service(string service, string path)
    {
        var status = await SendWithTokenAsync(service, Get, path, real => Token(real, organizationId: null, Roles.Attorney));

        status.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Audit", "GET", "/api/v1/audit")]
    [InlineData("Identity", "PUT", "/api/v1/identity/users/00000000-0000-0000-0000-000000000001/roles")]
    public async Task Admin_only_routes_refuse_a_valid_token_that_lacks_the_role(string service, string method, string path)
    {
        var status = await SendWithTokenAsync(service, new HttpMethod(method), path, real => Token(real, Guid.NewGuid(), Roles.Attorney));

        status.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Organization", "/api/v1/organizations/00000000-0000-0000-0000-000000000009")]
    [InlineData("User", "/api/v1/users/00000000-0000-0000-0000-000000000009")]
    public async Task Organization_and_user_endpoints_are_no_longer_anonymous_and_enforce_their_rules(string service, string path)
    {
        // A member of some other organization / a user who is not the profile owner: refused before any database access.
        var status = await SendWithTokenAsync(service, Get, path, real => Token(real, Guid.NewGuid(), Roles.Attorney));

        status.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Case")]
    [InlineData("Document")]
    [InlineData("Audit")]
    [InlineData("Identity")]
    public async Task Services_that_record_audit_events_register_the_audit_publisher(string service)
    {
        var resolved = await WithServiceAsync(service, (_, services) =>
        {
            using var scope = services.CreateScope();
            return Task.FromResult(scope.ServiceProvider.GetService<IAuditPublisher>() is not null);
        });

        resolved.Should().BeTrue();
    }

    [Fact]
    public async Task A_token_issued_by_the_real_identity_host_is_accepted_by_other_real_hosts()
    {
        // IdentityService signs with the JwtSettings it registered; the other services validate with their own. They only
        // agree because both read the same JwtSettings configuration, which is exactly what this guards.
        var token = await WithServiceAsync("Identity", (_, services) =>
        {
            using var scope = services.CreateScope();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = "u@firm.com", Email = "u@firm.com", FirstName = "T", LastName = "U",
                OrganizationId = Guid.NewGuid()
            };
            return Task.FromResult(scope.ServiceProvider.GetRequiredService<ITokenService>()
                .GenerateAccessToken(user, [Roles.Attorney]));
        });

        // These two handlers refuse a caller who is not a member / the owner (403) before touching any database. A 403 rather
        // than a 401 therefore proves the token was authenticated; a request that reached a database would spend a minute in
        // EF's connection retries against the dead port used here.
        var organization = await WithServiceAsync("Organization", (client, _) =>
            SendAsync(client, Get, $"/api/v1/organizations/{SomeId}", token));
        var profile = await WithServiceAsync("User", (client, _) =>
            SendAsync(client, Get, $"/api/v1/users/{SomeId}", token));

        organization.Should().Be(HttpStatusCode.Forbidden);
        profile.Should().Be(HttpStatusCode.Forbidden);
    }
}

/// <summary>
/// Starts each real service host at most once and shares it between the tests of the class; booting a host (DI container,
/// MassTransit, health checks) costs far more than the requests the tests make against it.
/// </summary>
public sealed class ServiceHostFixture : IAsyncLifetime
{
    private readonly ConcurrentDictionary<string, Lazy<Task<RunningService>>> _hosts = new();

    // Connection strings are read by Program.cs while it is still building the host, which is before anything added through
    // WebApplicationFactory is applied, but environment variables are already part of the builder. They point at a port
    // nothing listens on, so any code that actually touches the database or broker fails fast instead of hanging.
    static ServiceHostFixture()
    {
        foreach (var key in new[]
                 {
                     "identity-db", "user-db", "organization-db", "document-db", "case-db", "workflow-db",
                     "notification-db", "reporting-db", "audit-db"
                 })
        {
            Environment.SetEnvironmentVariable($"ConnectionStrings__{key}",
                "Host=localhost;Port=1;Database=wiring_test;Username=x;Password=x;Timeout=1");
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__rabbitmq", "amqp://guest:guest@localhost:1");
        Environment.SetEnvironmentVariable("ConnectionStrings__elasticsearch", "http://localhost:1");
    }

    public Task<RunningService> GetAsync(string service) =>
        _hosts.GetOrAdd(service, name => new Lazy<Task<RunningService>>(() => Task.Run(() => Start(name)))).Value;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts.Values.Where(h => h.IsValueCreated))
        {
            // A host that failed to start has nothing to dispose, and the tests that asked for it already failed with its
            // exception; rethrowing it here would make xUnit blame every test in the class for one broken service.
            if (host.Value is { IsFaulted: true } or { IsCanceled: true })
            {
                continue;
            }

            var running = await host.Value;
            running.Client.Dispose();
            await running.Factory.DisposeAsync();
        }
    }

    private static RunningService Start(string service) => service switch
    {
        "Case" => Create<CaseService.Domain.Case>(),
        "Document" => Create<DocumentService.Domain.Document>(),
        "Workflow" => Create<WorkflowService.Domain.WorkflowTemplate>(),
        "Notification" => Create<NotificationService.Domain.Notification>(),
        "Reporting" => Create<ReportingService.Domain.MetricAggregate>(),
        "Search" => Create<SearchService.Domain.SearchItem>(),
        "Audit" => Create<AuditService.Domain.AuditLog>(),
        "Organization" => Create<OrganizationService.Domain.Organization>(),
        "User" => Create<UserService.Domain.UserProfile>(),
        "Identity" => Create<ApplicationUser>(),
        _ => throw new ArgumentOutOfRangeException(nameof(service), service, "Unknown service")
    };

    // WebApplicationFactory finds a service's entry point from any public type in its assembly, which avoids the
    // ambiguity of eleven internal 'Program' classes.
    private static RunningService Create<TEntry>() where TEntry : class
    {
        var factory = new WebApplicationFactory<TEntry>().WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var started = false;
        try
        {
            var running = new RunningService(factory.CreateClient(), factory.Services, factory);
            started = true;
            return running;
        }
        finally
        {
            if (!started)
            {
                factory.Dispose();
            }
        }
    }
}

public sealed record RunningService(HttpClient Client, IServiceProvider Services, IAsyncDisposable Factory);
