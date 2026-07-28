using System;
using System.Threading.Tasks;
using MassTransit;
using Contracts;
using NotificationService.Domain;
using NotificationService.Providers;

namespace NotificationService.Consumers;

public class WorkflowStepAssignedConsumer : IConsumer<WorkflowStepAssignedIntegrationEvent>
{
    private readonly INotificationDispatcher _dispatcher;

    public WorkflowStepAssignedConsumer(INotificationDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task Consume(ConsumeContext<WorkflowStepAssignedIntegrationEvent> context)
    {
        var @event = context.Message;
        var assigneeUserId = Guid.Empty; 

        var slackAlert = new Notification(
            Guid.NewGuid(),
            @event.TenantId,
            assigneeUserId,
            channel: "Slack",
            subject: "Workflow Step Assigned",
            message: $"A new workflow step '{@event.StepName}' is pending review for {@event.EntityType} {@event.EntityId}. Required Role: {@event.AssignedRole}.",
            priority: "High"
        );

        await _dispatcher.DispatchAsync(slackAlert);
    }
}
