using AuditService.Domain;
using AuditService.Endpoints;
using AuditService.Infrastructure;
using Contracts;
using FluentAssertions;
using ServiceDefaults.Security;
using ServiceEndpoints.Tests.Support;
using Xunit;

namespace ServiceEndpoints.Tests;

public class AuditEndpointsTests : IDisposable
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly SqliteDatabase<AuditDbContext> _database = new();
    private readonly FakeEventBus _bus = new();

    public AuditEndpointsTests()
    {
        // Failed logins and registrations are recorded with no tenant (Guid.Empty): they must not leak to tenant-scoped readers.
        using var db = _database.NewContext();
        db.AuditLogs.AddRange(
            Log(TenantA, "Create"), Log(TenantA, "Login"), Log(TenantB, "Create"), Log(Guid.Empty, "Login"));
        db.SaveChanges();
    }

    public void Dispose() => _database.Dispose();

    private static AuditLog Log(Guid tenantId, string action) =>
        new(Guid.NewGuid(), tenantId, Guid.NewGuid(), "TestService", "Case", "1", action, "", "", "127.0.0.1", "ua", "c", "t", "Success", "hash");

    private async Task<HttpResponseSnapshot> QueryAsync(UserContext user, string? action = null)
    {
        using var db = _database.NewContext();
        return await TestHttp.ExecuteAsync(await AuditEndpoints.QueryAuditLogsAsync(user, db, action, null, null, null));
    }

    [Fact]
    public async Task A_system_administrator_sees_every_tenants_logs_including_tenantless_ones()
    {
        var response = await QueryAsync(TestUsers.Create(TenantA, Roles.SystemAdmin));

        response.Json.GetArrayLength().Should().Be(4);
    }

    [Theory]
    [InlineData(Roles.ComplianceOfficer)]
    [InlineData(Roles.SecurityAuditor)]
    [InlineData(Roles.OrganizationAdmin)]
    public async Task Other_audit_readers_only_see_their_own_tenant(string role)
    {
        var response = await QueryAsync(TestUsers.Create(TenantA, role));

        response.Json.GetArrayLength().Should().Be(2);
        response.Json.EnumerateArray().Select(l => l.GetProperty("tenantId").GetGuid()).Should().OnlyContain(t => t == TenantA);
    }

    [Fact]
    public async Task Filters_apply_within_the_callers_scope()
    {
        var response = await QueryAsync(TestUsers.Create(TenantA, Roles.ComplianceOfficer), action: "Login");

        response.Json.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task A_reader_without_a_tenant_is_refused_instead_of_being_shown_the_tenantless_logs()
    {
        var response = await QueryAsync(TestUsers.Create(tenantId: null, Roles.ComplianceOfficer));

        response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Exporting_is_tenant_scoped_and_is_itself_audited()
    {
        var user = TestUsers.Create(TenantA, Roles.ComplianceOfficer);

        using var db = _database.NewContext();
        var result = await AuditEndpoints.ExportAuditLogsAsync(
            new AuditEndpoints.ExportRequest("csv", null, null, null, null), user, TestHttp.NewContext(), db, _bus);
        var response = await TestHttp.ExecuteAsync(result);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("Record Count: 2");
        _bus.Of<AuditLogCreatedIntegrationEvent>().Should().ContainSingle(e => e.Action == "Export" && e.TenantId == TenantA);
    }

    [Fact]
    public async Task Exporting_without_a_tenant_is_refused_and_not_audited_as_a_success()
    {
        using var db = _database.NewContext();
        var result = await AuditEndpoints.ExportAuditLogsAsync(
            new AuditEndpoints.ExportRequest("csv", null, null, null, null),
            TestUsers.Create(tenantId: null, Roles.ComplianceOfficer), TestHttp.NewContext(), db, _bus);

        (await TestHttp.ExecuteAsync(result)).StatusCode.Should().Be(403);
        _bus.Published.Should().BeEmpty();
    }
}
