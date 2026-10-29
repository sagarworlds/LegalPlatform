using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NotificationService.Infrastructure;
using ServiceDefaults.Security;

namespace NotificationService.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TenantMember rejects tokens without an organization claim (403) before any handler runs.
        var group = endpoints.MapGroup("/api/v1/notifications").RequireAuthorization(Policies.TenantMember);

        group.MapGet("/", GetNotificationsAsync);
        group.MapPost("/{id:guid}/read", MarkAsReadAsync);

        return endpoints;
    }

    internal static async Task<IResult> GetNotificationsAsync(
        UserContext user,
        NotificationDbContext dbContext)
    {
        var notifications = await dbContext.Notifications
            .Where(n => n.TenantId == user.TenantId && (n.UserId == user.UserId || n.UserId == Guid.Empty))
            .OrderByDescending(n => n.CreatedDate)
            .ToListAsync();

        return Results.Ok(notifications);
    }

    internal static async Task<IResult> MarkAsReadAsync(
        Guid id,
        UserContext user,
        NotificationDbContext dbContext)
    {
        var notification = await dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.TenantId == user.TenantId && (n.UserId == user.UserId || n.UserId == Guid.Empty));

        if (notification == null)
        {
            return Results.NotFound();
        }

        notification.IsRead = true;
        await dbContext.SaveChangesAsync();

        return Results.Ok(notification);
    }
}
