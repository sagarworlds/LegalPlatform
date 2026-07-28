using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NotificationService.Domain;
using NotificationService.Infrastructure;

namespace NotificationService.Providers;

public interface INotificationDispatcher
{
    Task DispatchAsync(Notification notification);
}

public class NotificationDispatcher : INotificationDispatcher
{
    private readonly IEnumerable<INotificationProvider> _providers;
    private readonly NotificationDbContext _dbContext;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IEnumerable<INotificationProvider> providers,
        NotificationDbContext dbContext,
        ILogger<NotificationDispatcher> logger)
    {
        _providers = providers;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task DispatchAsync(Notification notification)
    {
        _logger.LogInformation("Routing notification {NotificationId} for User {UserId} via channel {Channel}", notification.Id, notification.UserId, notification.Channel);

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync();

        var provider = _providers.FirstOrDefault(p => p.Channel.Equals(notification.Channel, System.StringComparison.OrdinalIgnoreCase));
        if (provider != null)
        {
            var success = await provider.SendAsync(notification);
            notification.Status = success ? "Sent" : "Failed";
            notification.SentDate = success ? DateTime.UtcNow : null;
        }
        else
        {
            _logger.LogWarning("No registered provider found for channel: {Channel}. Marking notification as failed.", notification.Channel);
            notification.Status = "Failed";
        }

        await _dbContext.SaveChangesAsync();
    }
}
