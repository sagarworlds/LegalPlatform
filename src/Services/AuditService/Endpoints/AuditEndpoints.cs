using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using AuditService.Infrastructure;
using AuditService.Domain;
using Auditing;
using ServiceDefaults.Security;

namespace AuditService.Endpoints;

public static class AuditEndpoints
{
    /// <summary>
    /// Roles allowed to read the audit trail. They are the same constants Identity seeds and assigns; the previous
    /// hard-coded list named roles that nothing could ever grant, so every caller received 403.
    /// </summary>
    private const string AuditReaderRoles =
        Roles.SystemAdmin + "," + Roles.ComplianceOfficer + "," + Roles.SecurityAuditor + "," + Roles.OrganizationAdmin;

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The tenant claim is not required by the group because SystemAdmin reads across tenants; every other
        // role is checked for a tenant in ScopeToCaller.
        var group = endpoints.MapGroup("/api/v1/audit")
            .RequireAuthorization(Policies.AuthenticatedUser)
            .RequireAuthorization(new AuthorizeAttribute { Roles = AuditReaderRoles });

        group.MapGet("/", QueryAuditLogsAsync);
        group.MapPost("/export", ExportAuditLogsAsync);

        return endpoints;
    }

    internal static async Task<IResult> QueryAuditLogsAsync(
        UserContext user,
        AuditDbContext dbContext,
        string? action,
        string? entityType,
        string? entityId,
        Guid? userIdFilter)
    {
        if (!TryScopeToCaller(dbContext.AuditLogs, user, out var scoped, out var denied))
        {
            return denied;
        }

        var query = ApplyFilters(scoped, action, entityType, entityId, userIdFilter);

        var logs = await query.OrderByDescending(a => a.Timestamp).ToListAsync();
        return Results.Ok(logs);
    }

    public record ExportRequest(string Format, string? Action, string? EntityType, string? EntityId, Guid? UserIdFilter);

    internal static async Task<IResult> ExportAuditLogsAsync(
        ExportRequest request,
        UserContext user,
        HttpContext httpContext,
        AuditDbContext dbContext,
        IAuditPublisher audit)
    {
        if (!TryScopeToCaller(dbContext.AuditLogs, user, out var scoped, out var denied))
        {
            return denied;
        }

        var query = ApplyFilters(scoped, request.Action, request.EntityType, request.EntityId, request.UserIdFilter);

        var logs = await query.OrderByDescending(a => a.Timestamp).ToListAsync();

        await audit.PublishAsync(httpContext, AuditEntry.By(user,
            entityType: "AuditLog", entityId: "All", action: "Export",
            newValues: $"Format: {request.Format}, Count: {logs.Count}"));

        var exportContent = $"Audit Log Export ({request.Format})\nGenerated: {DateTime.UtcNow:O}\nRecord Count: {logs.Count}\n";
        return Results.Text(exportContent);
    }

    /// <summary>
    /// SystemAdmin sees every tenant. Anyone else is limited to their own tenant, and a caller without a tenant is
    /// refused: filtering on <see cref="Guid.Empty"/> would have exposed the tenant-less records (such as other
    /// people's failed logins) to every such caller.
    /// </summary>
    private static bool TryScopeToCaller(
        IQueryable<AuditLog> logs,
        UserContext user,
        out IQueryable<AuditLog> scoped,
        out IResult denied)
    {
        scoped = logs;
        denied = Results.Empty;

        if (user.IsInRole(Roles.SystemAdmin))
        {
            return true;
        }

        if (!user.HasTenant)
        {
            denied = Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Missing tenant context",
                detail: "The access token has no organization (tenant) claim, so tenant-scoped audit logs cannot be selected.");
            return false;
        }

        scoped = logs.Where(a => a.TenantId == user.TenantId);
        return true;
    }

    private static IQueryable<AuditLog> ApplyFilters(
        IQueryable<AuditLog> query,
        string? action,
        string? entityType,
        string? entityId,
        Guid? userIdFilter)
    {
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

        return query;
    }
}
