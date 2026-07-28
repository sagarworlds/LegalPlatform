using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NotificationService.Domain;

namespace NotificationService.Providers;

public class SlackNotificationProvider : INotificationProvider
{
    private readonly ILogger<SlackNotificationProvider> _logger;

    public string Channel => "Slack";

    public SlackNotificationProvider(ILogger<SlackNotificationProvider> logger)
    {
        _logger = logger;
    }

    public Task<bool> SendAsync(Notification notification)
    {
        _logger.LogInformation("[SLACK WEBHOOK MOCK] Dispatching to Channel: #legal-alerts. Text: {Subject} - {Message}", notification.Subject, notification.Message);
        return Task.FromResult(true);
    }
}
