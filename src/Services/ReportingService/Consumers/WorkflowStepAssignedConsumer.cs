using System;
using System.Threading.Tasks;
using MassTransit;
using Contracts;
using ReportingService.Domain;
using ReportingService.Infrastructure;

namespace ReportingService.Consumers;

public class WorkflowStepAssignedConsumer : IConsumer<WorkflowStepAssignedIntegrationEvent>
{
    private readonly ReportingDbContext _dbContext;

    public WorkflowStepAssignedConsumer(ReportingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<WorkflowStepAssignedIntegrationEvent> context)
    {
        var @event = context.Message;

        var aggregate = new MetricAggregate(
            Guid.NewGuid(),
            @event.TenantId,
            metricName: "WorkflowsAssigned",
            value: 1
        );

        _dbContext.MetricAggregates.Add(aggregate);
        await _dbContext.SaveChangesAsync();
    }
}
