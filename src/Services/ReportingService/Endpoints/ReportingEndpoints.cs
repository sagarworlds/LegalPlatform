using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ReportingService.Infrastructure;

namespace ReportingService.Endpoints;

public static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/reports/dashboard", GetDashboardStatsAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetDashboardStatsAsync(
        HttpContext httpContext,
        ReportingDbContext dbContext)
    {
        var (_, tenantId) = GetUserContext(httpContext);

        var aggregates = await dbContext.MetricAggregates
            .Where(m => m.TenantId == tenantId)
            .ToListAsync();

        var workflowsAssigned = aggregates
            .Where(m => m.MetricName == "WorkflowsAssigned")
            .Sum(m => m.Value);

        var workflowsCompleted = aggregates
            .Where(m => m.MetricName == "WorkflowsCompleted")
            .Sum(m => m.Value);

        var stats = new
        {
            WorkflowsAssigned = workflowsAssigned,
            WorkflowsCompleted = workflowsCompleted,
            CompletionRate = workflowsAssigned > 0 
                ? (double)workflowsCompleted / workflowsAssigned * 100 
                : 0.0,
            LastUpdated = DateTime.UtcNow
        };

        return Results.Ok(stats);
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
