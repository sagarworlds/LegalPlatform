using System;
using System.Threading.Tasks;
using MassTransit;
using Contracts;
using NotificationService.Domain;
using NotificationService.Providers;

namespace NotificationService.Consumers;

public class UserRegisteredConsumer : IConsumer<UserRegisteredIntegrationEvent>
{
    private readonly INotificationDispatcher _dispatcher;

    public UserRegisteredConsumer(INotificationDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task Consume(ConsumeContext<UserRegisteredIntegrationEvent> context)
    {
        var @event = context.Message;

        var welcomeEmail = new Notification(
            Guid.NewGuid(),
            @event.OrganizationId ?? Guid.Empty,
            @event.UserId,
            channel: "Email",
            subject: "Welcome to Legal Platform!",
            message: $"Hello {@event.FirstName},\n\nYour account has been successfully created. Welcome aboard!",
            priority: "Normal"
        );

        await _dispatcher.DispatchAsync(welcomeEmail);
    }
}
