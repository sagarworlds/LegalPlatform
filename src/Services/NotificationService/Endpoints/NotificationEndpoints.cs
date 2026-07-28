using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NotificationService.Infrastructure;

namespace NotificationService.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications").RequireAuthorization();

        group.MapGet("/", GetNotificationsAsync);
        group.MapPost("/{id:guid}/read", MarkAsReadAsync);

        return endpoints;
    }

    private static async Task<IResult> GetNotificationsAsync(
        HttpContext httpContext,
        NotificationDbContext dbContext)
    {
        var (userId, tenantId) = GetUserContext(httpContext);

        var notifications = await dbContext.Notifications
            .Where(n => n.TenantId == tenantId && (n.UserId == userId || n.UserId == Guid.Empty))
            .OrderByDescending(n => n.CreatedDate)
            .ToListAsync();

        return Results.Ok(notifications);
    }

    private static async Task<IResult> MarkAsReadAsync(
        Guid id,
        HttpContext httpContext,
        NotificationDbContext dbContext)
    {
        var (userId, tenantId) = GetUserContext(httpContext);

        var notification = await dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.TenantId == tenantId && (n.UserId == userId || n.UserId == Guid.Empty));

        if (notification == null)
        {
            return Results.NotFound();
        }

        notification.IsRead = true;
        await dbContext.SaveChangesAsync();

        return Results.Ok(notification);
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
