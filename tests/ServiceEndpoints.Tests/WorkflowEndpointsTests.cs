using Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceDefaults.Security;
using ServiceEndpoints.Tests.Support;
using WorkflowService.Endpoints;
using WorkflowService.Infrastructure;
using Xunit;

namespace ServiceEndpoints.Tests;

public class WorkflowEndpointsTests : IDisposable
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly SqliteDatabase<WorkflowDbContext> _database = new();
    private readonly FakeEventBus _bus = new();

    private readonly UserContext _admin = TestUsers.Create(TenantA, Roles.OrganizationAdmin);
    private readonly UserContext _attorney = TestUsers.Create(TenantA, Roles.Attorney);
    private readonly UserContext _paralegal = TestUsers.Create(TenantA, Roles.Paralegal);

    public void Dispose() => _database.Dispose();

    private async Task<HttpResponseSnapshot> CreateTemplateAsync(UserContext user, params (string Name, string Role)[] steps)
    {
        using var db = _database.NewContext();
        var request = new WorkflowEndpoints.CreateTemplateRequest(
            "Contract review", "desc",
            steps.Select(s => new WorkflowEndpoints.StepDefinitionRequest(s.Name, s.Role, 3, true)).ToList());
        return await TestHttp.ExecuteAsync(await WorkflowEndpoints.CreateTemplateAsync(request, user, db));
    }

    private async Task<Guid> CreateTwoStepTemplateAsync(UserContext? creator = null)
    {
        var response = await CreateTemplateAsync(creator ?? _admin, ("Attorney review", Roles.Attorney), ("Paralegal filing", Roles.Paralegal));
        response.StatusCode.Should().Be(201, response.Body);
        return response.Json.GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseSnapshot> StartAsync(UserContext user, Guid templateId)
    {
        using var db = _database.NewContext();
        var result = await WorkflowEndpoints.StartWorkflowInstanceAsync(
            new WorkflowEndpoints.StartWorkflowRequest(templateId, "Case", Guid.NewGuid()), user, db, _bus);
        return await TestHttp.ExecuteAsync(result);
    }

    private async Task<HttpResponseSnapshot> ApproveAsync(UserContext user, Guid instanceId)
    {
        using var db = _database.NewContext();
        return await TestHttp.ExecuteAsync(await WorkflowEndpoints.ApproveStepAsync(
            instanceId, new WorkflowEndpoints.StepActionRequest("ok"), user, db, _bus));
    }

    [Fact]
    public async Task Creating_a_template_returns_a_serializable_201()
    {
        var response = await CreateTemplateAsync(_admin, ("Attorney review", Roles.Attorney));

        response.StatusCode.Should().Be(201);
        response.Json.GetProperty("steps").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task A_step_assigned_to_a_role_that_does_not_exist_is_rejected()
    {
        var response = await CreateTemplateAsync(_admin, ("Impossible step", "Wizard"));

        response.StatusCode.Should().Be(400);
        response.Body.Should().Contain("Wizard");
    }

    [Fact]
    public async Task A_template_without_steps_is_rejected()
    {
        (await CreateTemplateAsync(_admin)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Templates_are_only_listed_for_their_own_tenant()
    {
        await CreateTwoStepTemplateAsync();

        using var db = _database.NewContext();
        var own = await TestHttp.ExecuteAsync(await WorkflowEndpoints.GetTemplatesAsync(_admin, db));
        var other = await TestHttp.ExecuteAsync(await WorkflowEndpoints.GetTemplatesAsync(TestUsers.Create(TenantB, Roles.OrganizationAdmin), db));

        own.Json.GetArrayLength().Should().Be(1);
        other.Json.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_workflow_cannot_be_started_from_another_tenants_template()
    {
        var templateId = await CreateTwoStepTemplateAsync();

        var response = await StartAsync(TestUsers.Create(TenantB, Roles.Attorney), templateId);

        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Starting_a_workflow_returns_a_serializable_201_and_announces_the_first_step()
    {
        var templateId = await CreateTwoStepTemplateAsync();

        var response = await StartAsync(_attorney, templateId);

        response.StatusCode.Should().Be(201, response.Body);
        response.Json.GetProperty("status").GetString().Should().Be("Pending");
        response.Json.GetProperty("history").GetArrayLength().Should().Be(1);
        _bus.Of<WorkflowStepAssignedIntegrationEvent>().Should().ContainSingle(e => e.AssignedRole == Roles.Attorney);
    }

    [Fact]
    public async Task Instance_details_are_serializable()
    {
        var instanceId = (await StartAsync(_attorney, await CreateTwoStepTemplateAsync())).Json.GetProperty("id").GetGuid();

        using var db = _database.NewContext();
        var response = await TestHttp.ExecuteAsync(await WorkflowEndpoints.GetWorkflowInstanceDetailsAsync(instanceId, _attorney, db));

        response.StatusCode.Should().Be(200);
        response.Json.GetProperty("steps").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task A_user_without_the_steps_role_cannot_approve_it()
    {
        var instanceId = (await StartAsync(_attorney, await CreateTwoStepTemplateAsync())).Json.GetProperty("id").GetGuid();

        var response = await ApproveAsync(_paralegal, instanceId);

        response.StatusCode.Should().Be(403);

        using var db = _database.NewContext();
        var instance = await db.Instances.Include(i => i.HistoryEntries).SingleAsync();
        instance.CurrentStepOrder.Should().Be(1);
        instance.HistoryEntries.Should().HaveCount(1, "a refused approval must leave no trace in the history");
    }

    [Fact]
    public async Task Approving_every_step_in_turn_completes_the_workflow()
    {
        var instanceId = (await StartAsync(_attorney, await CreateTwoStepTemplateAsync())).Json.GetProperty("id").GetGuid();

        var first = await ApproveAsync(_attorney, instanceId);
        first.StatusCode.Should().Be(200, first.Body);
        first.Json.GetProperty("currentStepOrder").GetInt32().Should().Be(2);
        _bus.Of<WorkflowStepAssignedIntegrationEvent>().Should().Contain(e => e.AssignedRole == Roles.Paralegal);

        var second = await ApproveAsync(_paralegal, instanceId);
        second.StatusCode.Should().Be(200, second.Body);
        second.Json.GetProperty("status").GetString().Should().Be("Approved");
        _bus.Of<WorkflowCompletedIntegrationEvent>().Should().ContainSingle(e => e.Status == "Approved");

        using var db = _database.NewContext();
        (await db.History.CountAsync()).Should().Be(3, "start + two approvals");
    }

    [Fact]
    public async Task The_wrong_role_cannot_approve_the_second_step_either()
    {
        var instanceId = (await StartAsync(_attorney, await CreateTwoStepTemplateAsync())).Json.GetProperty("id").GetGuid();
        await ApproveAsync(_attorney, instanceId);

        (await ApproveAsync(_attorney, instanceId)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Rejecting_persists_the_rejection_and_announces_it()
    {
        var instanceId = (await StartAsync(_attorney, await CreateTwoStepTemplateAsync())).Json.GetProperty("id").GetGuid();

        using (var db = _database.NewContext())
        {
            var result = await WorkflowEndpoints.RejectWorkflowAsync(
                instanceId, new WorkflowEndpoints.StepActionRequest("not acceptable"), _attorney, db, _bus);
            var response = await TestHttp.ExecuteAsync(result);

            response.StatusCode.Should().Be(200, response.Body);
            response.Json.GetProperty("status").GetString().Should().Be("Rejected");
        }

        _bus.Of<WorkflowCompletedIntegrationEvent>().Should().ContainSingle(e => e.Status == "Rejected");
        using var verify = _database.NewContext();
        (await verify.History.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_completed_workflow_cannot_be_approved_again()
    {
        var instanceId = (await StartAsync(_attorney, await CreateTwoStepTemplateAsync())).Json.GetProperty("id").GetGuid();
        await ApproveAsync(_attorney, instanceId);
        await ApproveAsync(_paralegal, instanceId);

        (await ApproveAsync(_paralegal, instanceId)).StatusCode.Should().Be(400);
    }
}
