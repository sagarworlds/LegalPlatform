using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using WorkflowService.Domain;
using WorkflowService.Infrastructure;
using WorkflowService.Models;
using WorkflowService.Validation;
using EventBus.Abstractions;
using Contracts;
using ServiceDefaults.Security;

namespace WorkflowService.Endpoints;

public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TenantMember rejects tokens without an organization claim (403) before any handler runs.
        var group = endpoints.MapGroup("/api/v1/workflows").RequireAuthorization(Policies.TenantMember);

        // Templates define who may approve what, so only administrators may change them.
        group.MapPost("/templates", CreateTemplateAsync)
            .RequireAuthorization(new AuthorizeAttribute { Roles = Roles.Admins });
        group.MapGet("/templates", GetTemplatesAsync);
        group.MapPost("/instances", StartWorkflowInstanceAsync);
        group.MapGet("/instances/{id:guid}", GetWorkflowInstanceDetailsAsync);
        group.MapPost("/instances/{id:guid}/approve", ApproveStepAsync);
        group.MapPost("/instances/{id:guid}/reject", RejectWorkflowAsync);

        return endpoints;
    }

    public record StepDefinitionRequest(string StepName, string AssignedRole, int DueInDays, bool IsRequired);
    public record CreateTemplateRequest(string Name, string Description, List<StepDefinitionRequest> Steps);

    internal static async Task<IResult> CreateTemplateAsync(
        CreateTemplateRequest request,
        UserContext user,
        WorkflowDbContext dbContext)
    {
        var errors = CreateTemplateValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var template = new WorkflowTemplate(Guid.NewGuid(), user.TenantId, request.Name, request.Description ?? string.Empty);

        var order = 1;
        foreach (var stepReq in request.Steps)
        {
            var step = new WorkflowStep(
                Guid.NewGuid(),
                template.Id,
                order++,
                stepReq.StepName,
                stepReq.AssignedRole,
                stepReq.DueInDays,
                stepReq.IsRequired
            );
            template.Steps.Add(step);
        }

        dbContext.Templates.Add(template);
        await dbContext.SaveChangesAsync();

        return Results.Created($"/api/v1/workflows/templates/{template.Id}", WorkflowTemplateResponse.From(template));
    }

    internal static async Task<IResult> GetTemplatesAsync(UserContext user, WorkflowDbContext dbContext)
    {
        var templates = await dbContext.Templates
            .AsNoTracking()
            .Include(t => t.Steps)
            .Where(t => t.TenantId == user.TenantId)
            .ToListAsync();

        return Results.Ok(templates.Select(WorkflowTemplateResponse.From));
    }

    public record StartWorkflowRequest(Guid TemplateId, string EntityType, Guid EntityId);

    internal static async Task<IResult> StartWorkflowInstanceAsync(
        StartWorkflowRequest request,
        UserContext user,
        WorkflowDbContext dbContext,
        IEventBus eventBus)
    {
        var template = await dbContext.Templates
            .Include(t => t.Steps)
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId && t.TenantId == user.TenantId);
        if (template == null || !template.IsActive)
        {
            return Results.BadRequest(new { Message = "Invalid or inactive workflow template." });
        }

        if (!template.Steps.Any())
        {
            return Results.BadRequest(new { Message = "Cannot start workflow: template has no steps defined." });
        }

        var instance = new WorkflowInstance(Guid.NewGuid(), user.TenantId, template.Id, request.EntityType, request.EntityId)
        {
            WorkflowTemplate = template
        };

        var firstStep = template.Steps.OrderBy(s => s.StepOrder).First();

        var history = new WorkflowHistory(
            Guid.NewGuid(),
            instance.Id,
            firstStep.StepId,
            action: "Start",
            userId: user.UserId,
            comments: "Workflow initialized."
        );
        instance.HistoryEntries.Add(history);

        dbContext.Instances.Add(instance);
        await dbContext.SaveChangesAsync();

        await eventBus.PublishAsync(new WorkflowStepAssignedIntegrationEvent
        {
            WorkflowInstanceId = instance.Id,
            StepId = firstStep.StepId,
            StepName = firstStep.StepName,
            AssignedRole = firstStep.AssignedRole,
            TenantId = user.TenantId,
            EntityType = request.EntityType,
            EntityId = request.EntityId
        });

        return Results.Created($"/api/v1/workflows/instances/{instance.Id}", WorkflowInstanceResponse.From(instance));
    }

    internal static async Task<IResult> GetWorkflowInstanceDetailsAsync(
        Guid id,
        UserContext user,
        WorkflowDbContext dbContext)
    {
        var instance = await dbContext.Instances
            .AsNoTracking()
            .Include(i => i.WorkflowTemplate)
                .ThenInclude(t => t.Steps)
            .Include(i => i.HistoryEntries)
            .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == user.TenantId);

        if (instance == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(WorkflowInstanceResponse.From(instance));
    }

    public record StepActionRequest(string Comments);

    internal static async Task<IResult> ApproveStepAsync(
        Guid id,
        StepActionRequest request,
        UserContext user,
        WorkflowDbContext dbContext,
        IEventBus eventBus)
    {
        var (active, failure) = await TryGetActionableStepAsync(id, user, dbContext);
        if (failure is not null)
        {
            return failure;
        }

        var (instance, currentStep) = active!;

        var history = new WorkflowHistory(
            Guid.NewGuid(),
            instance.Id,
            currentStep.StepId,
            action: "Approve",
            userId: user.UserId,
            comments: request.Comments ?? string.Empty
        );
        dbContext.History.Add(history);

        var nextStep = instance.WorkflowTemplate.Steps
            .OrderBy(s => s.StepOrder)
            .FirstOrDefault(s => s.StepOrder > instance.CurrentStepOrder);

        if (nextStep != null)
        {
            instance.CurrentStepOrder = nextStep.StepOrder;
            await dbContext.SaveChangesAsync();

            await eventBus.PublishAsync(new WorkflowStepAssignedIntegrationEvent
            {
                WorkflowInstanceId = instance.Id,
                StepId = nextStep.StepId,
                StepName = nextStep.StepName,
                AssignedRole = nextStep.AssignedRole,
                TenantId = user.TenantId,
                EntityType = instance.EntityType,
                EntityId = instance.EntityId
            });
        }
        else
        {
            instance.Status = "Approved";
            await dbContext.SaveChangesAsync();

            await eventBus.PublishAsync(new WorkflowCompletedIntegrationEvent
            {
                WorkflowInstanceId = instance.Id,
                Title = instance.WorkflowTemplate.Name,
                Status = "Approved",
                TenantId = user.TenantId,
                EntityType = instance.EntityType,
                EntityId = instance.EntityId
            });
        }

        return Results.Ok(WorkflowInstanceResponse.From(instance));
    }

    internal static async Task<IResult> RejectWorkflowAsync(
        Guid id,
        StepActionRequest request,
        UserContext user,
        WorkflowDbContext dbContext,
        IEventBus eventBus)
    {
        var (active, failure) = await TryGetActionableStepAsync(id, user, dbContext);
        if (failure is not null)
        {
            return failure;
        }

        var (instance, currentStep) = active!;

        var history = new WorkflowHistory(
            Guid.NewGuid(),
            instance.Id,
            currentStep.StepId,
            action: "Reject",
            userId: user.UserId,
            comments: request.Comments ?? string.Empty
        );
        dbContext.History.Add(history);

        instance.Status = "Rejected";
        await dbContext.SaveChangesAsync();

        await eventBus.PublishAsync(new WorkflowCompletedIntegrationEvent
        {
            WorkflowInstanceId = instance.Id,
            Title = instance.WorkflowTemplate.Name,
            Status = "Rejected",
            TenantId = user.TenantId,
            EntityType = instance.EntityType,
            EntityId = instance.EntityId
        });

        return Results.Ok(WorkflowInstanceResponse.From(instance));
    }

    /// <summary>
    /// Loads a pending instance of the caller's tenant and its current step, and verifies the caller holds the
    /// role the step is assigned to. Previously any tenant member could approve or reject any step.
    /// </summary>
    private static async Task<(ActiveStep? Active, IResult? Failure)> TryGetActionableStepAsync(
        Guid id,
        UserContext user,
        WorkflowDbContext dbContext)
    {
        var instance = await dbContext.Instances
            .Include(i => i.WorkflowTemplate)
                .ThenInclude(t => t.Steps)
            .Include(i => i.HistoryEntries)
            .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == user.TenantId);

        if (instance == null || instance.Status != "Pending")
        {
            return (null, Results.BadRequest(new { Message = "Workflow not found or is already completed." }));
        }

        var currentStep = instance.WorkflowTemplate.Steps
            .FirstOrDefault(s => s.StepOrder == instance.CurrentStepOrder);

        if (currentStep == null)
        {
            return (null, Results.BadRequest(new { Message = "Invalid step state." }));
        }

        if (!user.IsInRole(currentStep.AssignedRole))
        {
            return (null, Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not assigned to this step",
                detail: $"Step '{currentStep.StepName}' can only be actioned by users in the '{currentStep.AssignedRole}' role."));
        }

        return (new ActiveStep(instance, currentStep), null);
    }

    private sealed record ActiveStep(WorkflowInstance Instance, WorkflowStep Step);
}
