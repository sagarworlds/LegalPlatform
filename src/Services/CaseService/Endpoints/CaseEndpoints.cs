using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using CaseService.Domain;
using CaseService.Infrastructure;

namespace CaseService.Endpoints;

public static class CaseEndpoints
{
    public static IEndpointRouteBuilder MapCaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/cases").RequireAuthorization();

        group.MapPost("/", CreateCaseAsync);
        group.MapGet("/", ListCasesAsync);
        group.MapGet("/{id:guid}", GetCaseDetailsAsync);
        group.MapPost("/{id:guid}/notes", AddTimelineNoteAsync);

        return endpoints;
    }

    public record CreateCaseRequest(string Title, string Description, string CaseNumber, Guid ClientId, Guid AssignedLawyerId, DateTime? Deadline);

    private static async Task<IResult> CreateCaseAsync(
        CreateCaseRequest request,
        HttpContext httpContext,
        CaseDbContext dbContext)
    {
        var (userId, tenantId) = GetUserContext(httpContext);

        var exists = await dbContext.Cases.AnyAsync(c => c.CaseNumber == request.CaseNumber && c.TenantId == tenantId);
        if (exists)
        {
            return Results.BadRequest(new { Message = $"Case number {request.CaseNumber} is already in use." });
        }

        var kase = new Case(
            Guid.NewGuid(),
            tenantId,
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
            createdBy: userId.ToString()
        );

        kase.TimelineEntries.Add(timeline);

        dbContext.Cases.Add(kase);
        await dbContext.SaveChangesAsync();

        return Results.Created($"/api/v1/cases/{kase.Id}", kase);
    }

    private static async Task<IResult> ListCasesAsync(
        HttpContext httpContext,
        CaseDbContext dbContext)
    {
        var (_, tenantId) = GetUserContext(httpContext);

        var cases = await dbContext.Cases
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.StartDate)
            .ToListAsync();

        return Results.Ok(cases);
    }

    private static async Task<IResult> GetCaseDetailsAsync(
        Guid id,
        HttpContext httpContext,
        CaseDbContext dbContext)
    {
        var (_, tenantId) = GetUserContext(httpContext);

        var kase = await dbContext.Cases
            .Include(c => c.TimelineEntries)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);

        if (kase == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(kase);
    }

    public record AddNoteRequest(string EventDescription);

    private static async Task<IResult> AddTimelineNoteAsync(
        Guid id,
        AddNoteRequest request,
        HttpContext httpContext,
        CaseDbContext dbContext)
    {
        var (userId, tenantId) = GetUserContext(httpContext);

        var kase = await dbContext.Cases
            .Include(c => c.TimelineEntries)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);

        if (kase == null)
        {
            return Results.NotFound();
        }

        var timeline = new CaseTimeline(
            Guid.NewGuid(),
            kase.Id,
            eventDescription: request.EventDescription,
            createdBy: userId.ToString()
        );

        kase.TimelineEntries.Add(timeline);
        await dbContext.SaveChangesAsync();

        return Results.Ok(timeline);
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
