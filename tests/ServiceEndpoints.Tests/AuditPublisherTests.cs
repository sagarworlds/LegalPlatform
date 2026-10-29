using System.Net;
using Auditing;
using Contracts;
using EventBus.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceDefaults.Security;
using ServiceEndpoints.Tests.Support;
using Xunit;

namespace ServiceEndpoints.Tests;

public class AuditPublisherTests
{
    private readonly FakeEventBus _bus = new();

    private IAuditPublisher Publisher(string serviceName = "CaseService") => TestAudit.For(_bus, serviceName);

    private AuditLogCreatedIntegrationEvent Published() => _bus.Of<AuditLogCreatedIntegrationEvent>().Should().ContainSingle().Subject;

    [Fact]
    public async Task Maps_the_entry_and_the_request_details_onto_the_integration_event()
    {
        var http = TestHttp.NewContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        http.Request.Headers.UserAgent = "TestBrowser/1.0";
        http.TraceIdentifier = "trace-123";
        var tenant = Guid.NewGuid();
        var actor = Guid.NewGuid();

        await Publisher("CaseService").PublishAsync(http,
            new AuditEntry(tenant, actor, "Case", "case-1", "Update", NewValues: "after", OldValues: "before"));

        var e = Published();
        e.ServiceName.Should().Be("CaseService");
        e.TenantId.Should().Be(tenant);
        e.UserId.Should().Be(actor);
        e.EntityType.Should().Be("Case");
        e.EntityId.Should().Be("case-1");
        e.Action.Should().Be("Update");
        e.OldValues.Should().Be("before");
        e.NewValues.Should().Be("after");
        e.IPAddress.Should().Be("203.0.113.7");
        e.UserAgent.Should().Be("TestBrowser/1.0");
        e.CorrelationId.Should().Be("trace-123");
        e.TraceId.Should().Be("trace-123");
        e.Outcome.Should().Be("Success");
    }

    [Fact]
    public async Task Missing_request_details_are_recorded_as_Unknown_not_left_blank()
    {
        await Publisher().PublishAsync(TestHttp.NewContext(), new AuditEntry(Guid.Empty, Guid.Empty, "User", "x", "Login", "n"));

        var e = Published();
        e.IPAddress.Should().Be("Unknown");
        e.UserAgent.Should().Be("Unknown");
    }

    [Fact]
    public async Task A_failed_attempt_is_recorded_with_a_Failure_outcome()
    {
        await Publisher().PublishAsync(TestHttp.NewContext(),
            new AuditEntry(Guid.Empty, Guid.Empty, "User", "LoginFailed", "Login", "Email: a@b.c", Success: false));

        Published().Outcome.Should().Be("Failure");
    }

    [Fact]
    public async Task An_entry_made_for_a_caller_carries_their_tenant_and_user_id()
    {
        var caller = TestUsers.Create(Guid.NewGuid(), Roles.Attorney);

        await Publisher().PublishAsync(TestHttp.NewContext(), AuditEntry.By(caller, "Case", "1", "Create", "n"));

        var e = Published();
        e.TenantId.Should().Be(caller.TenantId);
        e.UserId.Should().Be(caller.UserId);
        e.Outcome.Should().Be("Success");
        e.OldValues.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_service_is_recorded_under_its_own_name()
    {
        await Publisher("DocumentService").PublishAsync(TestHttp.NewContext(), new AuditEntry(Guid.Empty, Guid.Empty, "Document", "1", "Upload", "n"));

        Published().ServiceName.Should().Be("DocumentService");
    }

    [Fact]
    public async Task Registration_resolves_a_working_scoped_publisher()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEventBus>(_bus);
        services.AddAuditPublisher("IdentityService");
        using var provider = services.BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IAuditPublisher>();

        await publisher.PublishAsync(TestHttp.NewContext(), new AuditEntry(Guid.Empty, Guid.Empty, "User", "1", "Login", "n"));
        Published().ServiceName.Should().Be("IdentityService");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Registration_requires_a_service_name(string name)
    {
        var act = () => new ServiceCollection().AddAuditPublisher(name);

        act.Should().Throw<ArgumentException>();
    }
}
