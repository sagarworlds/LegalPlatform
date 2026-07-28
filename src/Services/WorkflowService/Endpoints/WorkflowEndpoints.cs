using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using WorkflowService.Domain;
using WorkflowService.Infrastructure;
using EventBus.Abstractions;
using Contracts;

namespace WorkflowService.Endpoints;

public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/workflows").RequireAuthorization();

        group.MapPost("/templates", CreateTemplateAsync);
        group.MapGet("/templates", GetTemplatesAsync);
        group.MapPost("/instances", StartWorkflowInstanceAsync);
        group.MapGet("/instances/{id:guid}", GetWorkflowInstanceDetailsAsync);
        group.MapPost("/instances/{id:guid}/approve", ApproveStepAsync);
        group.MapPost("/instances/{id:guid}/reject", RejectWorkflowAsync);

        return endpoints;
    }

    public record StepDefinitionRequest(string StepName, string AssignedRole, int DueInDays, bool IsRequired);
    public record CreateTemplateRequest(string Name, string Description, List<StepDefinitionRequest> Steps);

    private static async Task<IResult> CreateTemplateAsync(
        CreateTemplateRequest request,
        WorkflowDbContext dbContext)
    {
        var template = new WorkflowTemplate(Guid.NewGuid(), request.Name, request.Description);
        
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

        return Results.Created($"/api/v1/workflows/templates/{template.Id}", template);
    }

    private static async Task<IResult> GetTemplatesAsync(WorkflowDbContext dbContext)
    {
        var templates = await dbContext.Templates.Include(t => t.Steps).ToListAsync();
        return Results.Ok(templates);
    }

    public record StartWorkflowRequest(Guid TemplateId, string EntityType, Guid EntityId);

    private static async Task<IResult> StartWorkflowInstanceAsync(
        StartWorkflowRequest request,
        HttpContext httpContext,
        WorkflowDbContext dbContext,
        IEventBus eventBus)
    {
        var (userId, tenantId) = GetUserContext(httpContext);

        var template = await dbContext.Templates.Include(t => t.Steps).FirstOrDefaultAsync(t => t.Id == request.TemplateId);
        if (template == null || !template.IsActive)
        {
            return Results.BadRequest(new { Message = "Invalid or inactive workflow template." });
        }

        if (!template.Steps.Any())
        {
            return Results.BadRequest(new { Message = "Cannot start workflow: template has no steps defined." });
        }

        var instance = new WorkflowInstance(Guid.NewGuid(), tenantId, template.Id, request.EntityType, request.EntityId);
        
        var firstStep = template.Steps.OrderBy(s => s.StepOrder).First();
        
        var history = new WorkflowHistory(
            Guid.NewGuid(),
            instance.Id,
            firstStep.StepId,
            action: "Start",
            userId: userId,
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
            TenantId = tenantId,
            EntityType = request.EntityType,
            EntityId = request.EntityId
        });

        return Results.Created($"/api/v1/workflows/instances/{instance.Id}", instance);
    }

    private static async Task<IResult> GetWorkflowInstanceDetailsAsync(
        Guid id,
        HttpContext httpContext,
        WorkflowDbContext dbContext)
    {
        var (_, tenantId) = GetUserContext(httpContext);

        var instance = await dbContext.Instances
            .Include(i => i.WorkflowTemplate)
                .ThenInclude(t => t.Steps)
            .Include(i => i.HistoryEntries)
            .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId);

        if (instance == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(instance);
    }

    public record StepActionRequest(string Comments);

    private static async Task<IResult> ApproveStepAsync(
        Guid id,
        StepActionRequest request,
        HttpContext httpContext,
        WorkflowDbContext dbContext,
        IEventBus eventBus)
    {
        var (userId, tenantId) = GetUserContext(httpContext);

        var instance = await dbContext.Instances
            .Include(i => i.WorkflowTemplate)
                .ThenInclude(t => t.Steps)
            .Include(i => i.HistoryEntries)
            .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId);

        if (instance == null || instance.Status != "Pending")
        {
            return Results.BadRequest(new { Message = "Workflow not found or is already completed." });
        }

        var currentStep = instance.WorkflowTemplate.Steps
            .FirstOrDefault(s => s.StepOrder == instance.CurrentStepOrder);

        if (currentStep == null)
        {
            return Results.BadRequest(new { Message = "Invalid step state." });
        }

        var history = new WorkflowHistory(
            Guid.NewGuid(),
            instance.Id,
            currentStep.StepId,
            action: "Approve",
            userId: userId,
            comments: request.Comments
        );
        instance.HistoryEntries.Add(history);

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
                TenantId = tenantId,
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
                TenantId = tenantId,
                EntityType = instance.EntityType,
                EntityId = instance.EntityId
            });
        }

        return Results.Ok(instance);
    }

    private static async Task<IResult> RejectWorkflowAsync(
        Guid id,
        StepActionRequest request,
        HttpContext httpContext,
        WorkflowDbContext dbContext,
        IEventBus eventBus)
    {
        var (userId, tenantId) = GetUserContext(httpContext);

        var instance = await dbContext.Instances
            .Include(i => i.WorkflowTemplate)
                .ThenInclude(t => t.Steps)
            .Include(i => i.HistoryEntries)
            .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId);

        if (instance == null || instance.Status != "Pending")
        {
            return Results.BadRequest(new { Message = "Workflow not found or is already completed." });
        }

        var currentStep = instance.WorkflowTemplate.Steps
            .FirstOrDefault(s => s.StepOrder == instance.CurrentStepOrder);

        if (currentStep == null)
        {
            return Results.BadRequest(new { Message = "Invalid step state." });
        }

        var history = new WorkflowHistory(
            Guid.NewGuid(),
            instance.Id,
            currentStep.StepId,
            action: "Reject",
            userId: userId,
            comments: request.Comments
        );
        instance.HistoryEntries.Add(history);

        instance.Status = "Rejected";
        await dbContext.SaveChangesAsync();

        await eventBus.PublishAsync(new WorkflowCompletedIntegrationEvent
        {
            WorkflowInstanceId = instance.Id,
            Title = instance.WorkflowTemplate.Name,
            Status = "Rejected",
            TenantId = tenantId,
            EntityType = instance.EntityType,
            EntityId = instance.EntityId
        });

        return Results.Ok(instance);
    }

    private static (Guid UserId, Guid TenantId) GetUserContext(HttpContext httpContext)
    {
        var userIdStr = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenantIdStr = httpContext.User.FindFirstValue("organizationId") ?? httpContext.User.FindFirstValue("tenant_id");

        var userId = Guid.TryParse(userIdStr, out var u) ? u : Guid.Empty;
        var tenantId = Guid.TryParse(tenantIdStr, out var t) ? t : Guid.Empty;

        return (userId, tenantId);
    }
}
