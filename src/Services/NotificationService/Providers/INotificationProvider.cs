using System.Threading.Tasks;
using NotificationService.Domain;

namespace NotificationService.Providers;

public interface INotificationProvider
{
    string Channel { get; }
    Task<bool> SendAsync(Notification notification);
}
