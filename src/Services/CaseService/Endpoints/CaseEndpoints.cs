using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using CaseService.Domain;
using CaseService.Infrastructure;
using CaseService.Models;
using EventBus.Abstractions;
using Contracts;
using ServiceDefaults.Security;

namespace CaseService.Endpoints;

public static class CaseEndpoints
{
    public static IEndpointRouteBuilder MapCaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TenantMember rejects tokens without an organization claim (403) before any handler runs.
        var group = endpoints.MapGroup("/api/v1/cases").RequireAuthorization(Policies.TenantMember);

        group.MapPost("/", CreateCaseAsync);
        group.MapGet("/", ListCasesAsync);
        group.MapGet("/{id:guid}", GetCaseDetailsAsync);
        group.MapPost("/{id:guid}/notes", AddTimelineNoteAsync);

        return endpoints;
    }

    public record CreateCaseRequest(string Title, string Description, string CaseNumber, Guid ClientId, Guid AssignedLawyerId, DateTime? Deadline);

    internal static async Task<IResult> CreateCaseAsync(
        CreateCaseRequest request,
        UserContext user,
        HttpContext httpContext,
        CaseDbContext dbContext,
        IEventBus eventBus)
    {
        var exists = await dbContext.Cases.AnyAsync(c => c.CaseNumber == request.CaseNumber && c.TenantId == user.TenantId);
        if (exists)
        {
            return Results.BadRequest(new { Message = $"Case number {request.CaseNumber} is already in use." });
        }

        var kase = new Case(
            Guid.NewGuid(),
            user.TenantId,
            request.Title,
            request.Description,
            request.CaseNumber,
            request.ClientId,
            request.AssignedLawyerId,
            request.Deadline
        );

        var timeline = new CaseTimeline(
            Guid.NewGuid(),
            kase.Id,
            eventDescription: "Case created and initialized.",
            createdBy: user.UserId.ToString()
        );

        kase.TimelineEntries.Add(timeline);

        dbContext.Cases.Add(kase);
        await dbContext.SaveChangesAsync();

        await eventBus.PublishAsync(new AuditLogCreatedIntegrationEvent
        {
            TenantId = user.TenantId,
            UserId = user.UserId,
            ServiceName = "CaseService",
            EntityType = "Case",
            EntityId = kase.Id.ToString(),
            Action = "Create",
            OldValues = string.Empty,
            NewValues = $"Title: {kase.Title}, CaseNumber: {kase.CaseNumber}",
            IPAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            UserAgent = httpContext.Request.Headers["User-Agent"].ToString() ?? "Unknown",
            CorrelationId = httpContext.TraceIdentifier,
            TraceId = httpContext.TraceIdentifier,
            Outcome = "Success"
        });

        return Results.Created($"/api/v1/cases/{kase.Id}", CaseDetailResponse.From(kase));
    }

    internal static async Task<IResult> ListCasesAsync(
        UserContext user,
        CaseDbContext dbContext)
    {
        var cases = await dbContext.Cases
            .AsNoTracking()
            .Where(c => c.TenantId == user.TenantId)
            .OrderByDescending(c => c.StartDate)
            .ToListAsync();

        return Results.Ok(cases.Select(CaseSummaryResponse.From));
    }

    internal static async Task<IResult> GetCaseDetailsAsync(
        Guid id,
        UserContext user,
        CaseDbContext dbContext)
    {
        var kase = await dbContext.Cases
            .AsNoTracking()
            .Include(c => c.TimelineEntries)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == user.TenantId);

        if (kase == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(CaseDetailResponse.From(kase));
    }

    public record AddNoteRequest(string EventDescription);

    internal static async Task<IResult> AddTimelineNoteAsync(
        Guid id,
        AddNoteRequest request,
        UserContext user,
        HttpContext httpContext,
        CaseDbContext dbContext,
        IEventBus eventBus)
    {
        var caseExists = await dbContext.Cases.AnyAsync(c => c.Id == id && c.TenantId == user.TenantId);
        if (!caseExists)
        {
            return Results.NotFound();
        }

        var timeline = new CaseTimeline(
            Guid.NewGuid(),
            id,
            eventDescription: request.EventDescription,
            createdBy: user.UserId.ToString()
        );

        // Added through its own DbSet so the insert does not depend on loading and mutating the parent's collection.
        dbContext.CaseTimelines.Add(timeline);
        await dbContext.SaveChangesAsync();

        await eventBus.PublishAsync(new AuditLogCreatedIntegrationEvent
        {
            TenantId = user.TenantId,
            UserId = user.UserId,
            ServiceName = "CaseService",
            EntityType = "CaseTimeline",
            EntityId = timeline.Id.ToString(),
            Action = "AddNote",
            OldValues = string.Empty,
            NewValues = $"CaseId: {id}, Note: {request.EventDescription}",
            IPAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            UserAgent = httpContext.Request.Headers["User-Agent"].ToString() ?? "Unknown",
            CorrelationId = httpContext.TraceIdentifier,
            TraceId = httpContext.TraceIdentifier,
            Outcome = "Success"
        });

        return Results.Ok(TimelineEntryResponse.From(timeline));
    }
}
