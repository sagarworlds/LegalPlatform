using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ReportingService.Infrastructure;
using ServiceDefaults.Security;

namespace ReportingService.Endpoints;

public static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TenantMember rejects tokens without an organization claim (403) before any handler runs.
        endpoints.MapGet("/api/v1/reports/dashboard", GetDashboardStatsAsync).RequireAuthorization(Policies.TenantMember);
        return endpoints;
    }

    internal static async Task<IResult> GetDashboardStatsAsync(
        UserContext user,
        ReportingDbContext dbContext)
    {
        var aggregates = await dbContext.MetricAggregates
            .Where(m => m.TenantId == user.TenantId)
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
}
