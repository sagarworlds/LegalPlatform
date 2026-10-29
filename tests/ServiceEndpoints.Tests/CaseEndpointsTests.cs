using CaseService.Endpoints;
using CaseService.Infrastructure;
using Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceEndpoints.Tests.Support;
using Xunit;

namespace ServiceEndpoints.Tests;

public class CaseEndpointsTests : IDisposable
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly SqliteDatabase<CaseDbContext> _database = new();
    private readonly FakeEventBus _bus = new();

    public void Dispose() => _database.Dispose();

    private async Task<HttpResponseSnapshot> CreateAsync(Guid tenant, string caseNumber)
    {
        using var db = _database.NewContext();
        var result = await CaseEndpoints.CreateCaseAsync(
            new CaseEndpoints.CreateCaseRequest("Title", "Description", caseNumber, Guid.NewGuid(), Guid.NewGuid(), null),
            TestUsers.Create(tenant), TestHttp.NewContext(), db, _bus);
        return await TestHttp.ExecuteAsync(result);
    }

    private async Task<HttpResponseSnapshot> GetDetailAsync(Guid tenant, Guid id)
    {
        using var db = _database.NewContext();
        return await TestHttp.ExecuteAsync(await CaseEndpoints.GetCaseDetailsAsync(id, TestUsers.Create(tenant), db));
    }

    [Fact]
    public async Task Create_returns_a_serializable_201_with_the_initial_timeline_entry()
    {
        var response = await CreateAsync(TenantA, "CASE-1");

        response.StatusCode.Should().Be(201);
        response.Json.GetProperty("caseNumber").GetString().Should().Be("CASE-1");
        response.Json.GetProperty("timelineEntries").GetArrayLength().Should().Be(1);
        _bus.Of<AuditLogCreatedIntegrationEvent>().Should().ContainSingle(e => e.Action == "Create");
    }

    [Fact]
    public async Task Detail_returns_the_case_with_its_timeline_instead_of_failing_on_the_entity_cycle()
    {
        var id = (await CreateAsync(TenantA, "CASE-1")).Json.GetProperty("id").GetGuid();

        var response = await GetDetailAsync(TenantA, id);

        response.StatusCode.Should().Be(200);
        response.Json.GetProperty("timelineEntries").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Adding_a_note_inserts_a_timeline_entry()
    {
        var id = (await CreateAsync(TenantA, "CASE-1")).Json.GetProperty("id").GetGuid();

        using (var db = _database.NewContext())
        {
            var result = await CaseEndpoints.AddTimelineNoteAsync(
                id, new CaseEndpoints.AddNoteRequest("Filed motion"), TestUsers.Create(TenantA), TestHttp.NewContext(), db, _bus);
            var note = await TestHttp.ExecuteAsync(result);

            note.StatusCode.Should().Be(200);
            note.Json.GetProperty("eventDescription").GetString().Should().Be("Filed motion");
        }

        var detail = await GetDetailAsync(TenantA, id);
        detail.Json.GetProperty("timelineEntries").GetArrayLength().Should().Be(2);
        _bus.Of<AuditLogCreatedIntegrationEvent>().Should().Contain(e => e.Action == "AddNote");
    }

    [Fact]
    public async Task A_case_number_can_be_reused_by_another_tenant()
    {
        (await CreateAsync(TenantA, "CASE-1")).StatusCode.Should().Be(201);

        var other = await CreateAsync(TenantB, "CASE-1");

        other.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task A_case_number_cannot_be_reused_within_a_tenant()
    {
        (await CreateAsync(TenantA, "CASE-1")).StatusCode.Should().Be(201);

        var duplicate = await CreateAsync(TenantA, "CASE-1");

        duplicate.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Another_tenants_case_is_not_found()
    {
        var id = (await CreateAsync(TenantA, "CASE-1")).Json.GetProperty("id").GetGuid();

        (await GetDetailAsync(TenantB, id)).StatusCode.Should().Be(404);

        using var db = _database.NewContext();
        var note = await CaseEndpoints.AddTimelineNoteAsync(
            id, new CaseEndpoints.AddNoteRequest("x"), TestUsers.Create(TenantB), TestHttp.NewContext(), db, _bus);
        (await TestHttp.ExecuteAsync(note)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task List_only_returns_the_callers_tenant()
    {
        await CreateAsync(TenantA, "A-1");
        await CreateAsync(TenantB, "B-1");

        using var db = _database.NewContext();
        var response = await TestHttp.ExecuteAsync(await CaseEndpoints.ListCasesAsync(TestUsers.Create(TenantA), db));

        response.Json.GetArrayLength().Should().Be(1);
        response.Json[0].GetProperty("caseNumber").GetString().Should().Be("A-1");
    }

    [Fact]
    public void The_case_number_index_is_scoped_to_the_tenant()
    {
        using var db = _database.NewContext();
        var index = db.Model.FindEntityType(typeof(CaseService.Domain.Case))!
            .GetIndexes().Single(i => i.IsUnique);

        index.Properties.Select(p => p.Name).Should().Equal(nameof(CaseService.Domain.Case.TenantId), nameof(CaseService.Domain.Case.CaseNumber));
    }
}
