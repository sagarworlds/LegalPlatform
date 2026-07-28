using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using AuditService.Infrastructure;
using AuditService.Domain;
using EventBus.Abstractions;
using Contracts;

namespace AuditService.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/audit").RequireAuthorization();

        group.MapGet("/", QueryAuditLogsAsync);
        group.MapPost("/export", ExportAuditLogsAsync);

        return endpoints;
    }

    private static async Task<IResult> QueryAuditLogsAsync(
        HttpContext httpContext,
        AuditDbContext dbContext,
        string? action,
        string? entityType,
        string? entityId,
        Guid? userIdFilter)
    {
        var (userId, tenantId, role, isAuthorized) = GetSecurityContext(httpContext);
        if (!isAuthorized)
        {
            return Results.Forbid();
        }

        var query = dbContext.AuditLogs.AsQueryable();

        if (role != "SystemAdmin")
        {
            query = query.Where(a => a.TenantId == tenantId);
        }

        if (!string.IsNullOrEmpty(action))
        {
            query = query.Where(a => a.Action == action);
        }

        if (!string.IsNullOrEmpty(entityType))
        {
            query = query.Where(a => a.EntityType == entityType);
        }

        if (!string.IsNullOrEmpty(entityId))
        {
            query = query.Where(a => a.EntityId == entityId);
        }

        if (userIdFilter.HasValue)
        {
            query = query.Where(a => a.UserId == userIdFilter.Value);
        }

        var logs = await query.OrderByDescending(a => a.Timestamp).ToListAsync();
        return Results.Ok(logs);
    }

    public record ExportRequest(string Format, string? Action, string? EntityType, string? EntityId, Guid? UserIdFilter);

    private static async Task<IResult> ExportAuditLogsAsync(
        ExportRequest request,
        HttpContext httpContext,
        AuditDbContext dbContext,
        IEventBus eventBus)
    {
        var (userId, tenantId, role, isAuthorized) = GetSecurityContext(httpContext);
        if (!isAuthorized)
        {
            return Results.Forbid();
        }

        var query = dbContext.AuditLogs.AsQueryable();

        if (role != "SystemAdmin")
        {
            query = query.Where(a => a.TenantId == tenantId);
        }

        if (!string.IsNullOrEmpty(request.Action))
        {
            query = query.Where(a => a.Action == request.Action);
        }

        if (!string.IsNullOrEmpty(request.EntityType))
        {
            query = query.Where(a => a.EntityType == request.EntityType);
        }

        if (!string.IsNullOrEmpty(request.EntityId))
        {
            query = query.Where(a => a.EntityId == request.EntityId);
        }

        if (request.UserIdFilter.HasValue)
        {
            query = query.Where(a => a.UserId == request.UserIdFilter.Value);
        }

        var logs = await query.OrderByDescending(a => a.Timestamp).ToListAsync();

        var connection = httpContext.Connection;
        await eventBus.PublishAsync(new AuditLogCreatedIntegrationEvent
        {
            TenantId = tenantId,
            UserId = userId,
            ServiceName = "AuditService",
            EntityType = "AuditLog",
            EntityId = "All",
            Action = "Export",
            OldValues = string.Empty,
            NewValues = $"Format: {request.Format}, Count: {logs.Count}",
            IPAddress = connection.RemoteIpAddress?.ToString() ?? "Unknown",
            UserAgent = httpContext.Request.Headers["User-Agent"].ToString() ?? "Unknown",
            CorrelationId = httpContext.TraceIdentifier,
            TraceId = httpContext.TraceIdentifier,
            Outcome = "Success"
        });

        var exportContent = $"Audit Log Export ({request.Format})\nGenerated: {DateTime.UtcNow:O}\nRecord Count: {logs.Count}\n";
        return Results.Text(exportContent);
    }

    private static (Guid UserId, Guid TenantId, string Role, bool IsAuthorized) GetSecurityContext(HttpContext httpContext)
    {
        var userIdStr = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenantIdStr = httpContext.User.FindFirstValue("organizationId") ?? httpContext.User.FindFirstValue("tenant_id");
        var role = httpContext.User.FindFirstValue(ClaimTypes.Role) ?? httpContext.User.FindFirstValue("role") ?? string.Empty;

        var userId = Guid.TryParse(userIdStr, out var u) ? u : Guid.Empty;
        var tenantId = Guid.TryParse(tenantIdStr, out var t) ? t : Guid.Empty;

        var allowedRoles = new[] { "SystemAdmin", "ComplianceOfficer", "SecurityAuditor", "OrganizationAdmin" };
        var isAuthorized = allowedRoles.Contains(role);

        return (userId, tenantId, role, isAuthorized);
    }
}
