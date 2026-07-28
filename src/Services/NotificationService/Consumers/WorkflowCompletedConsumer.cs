using System;
using System.Threading.Tasks;
using MassTransit;
using Contracts;
using NotificationService.Domain;
using NotificationService.Providers;

namespace NotificationService.Consumers;

public class WorkflowCompletedConsumer : IConsumer<WorkflowCompletedIntegrationEvent>
{
    private readonly INotificationDispatcher _dispatcher;

    public WorkflowCompletedConsumer(INotificationDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task Consume(ConsumeContext<WorkflowCompletedIntegrationEvent> context)
    {
        var @event = context.Message;

        var slackAlert = new Notification(
            Guid.NewGuid(),
            @event.TenantId,
            Guid.Empty,
            channel: "Slack",
            subject: "Workflow Completed",
            message: $"Workflow '{@event.Title}' for {@event.EntityType} {@event.EntityId} has been {@event.Status.ToUpper()}.",
            priority: "Normal"
        );

        await _dispatcher.DispatchAsync(slackAlert);
    }
}
