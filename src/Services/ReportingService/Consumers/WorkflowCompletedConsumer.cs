using System;
using System.Threading.Tasks;
using MassTransit;
using Contracts;
using ReportingService.Domain;
using ReportingService.Infrastructure;

namespace ReportingService.Consumers;

public class WorkflowCompletedConsumer : IConsumer<WorkflowCompletedIntegrationEvent>
{
    private readonly ReportingDbContext _dbContext;

    public WorkflowCompletedConsumer(ReportingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<WorkflowCompletedIntegrationEvent> context)
    {
        var @event = context.Message;

        var aggregate = new MetricAggregate(
            Guid.NewGuid(),
            @event.TenantId,
            metricName: "WorkflowsCompleted",
            value: 1
        );

        _dbContext.MetricAggregates.Add(aggregate);
        await _dbContext.SaveChangesAsync();
    }
}
