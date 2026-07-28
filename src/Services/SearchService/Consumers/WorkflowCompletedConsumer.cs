using System;
using System.Threading.Tasks;
using MassTransit;
using Contracts;
using Elastic.Clients.Elasticsearch;
using SearchService.Domain;

namespace SearchService.Consumers;

public class WorkflowCompletedConsumer : IConsumer<WorkflowCompletedIntegrationEvent>
{
    private readonly ElasticsearchClient _esClient;

    public WorkflowCompletedConsumer(ElasticsearchClient esClient)
    {
        _esClient = esClient;
    }

    public async Task Consume(ConsumeContext<WorkflowCompletedIntegrationEvent> context)
    {
        var @event = context.Message;

        var item = new SearchItem(
            id: @event.WorkflowInstanceId.ToString(),
            tenantId: @event.TenantId,
            title: $"Workflow Completed: {@event.Title}",
            description: $"Workflow instance for {@event.EntityType} {@event.EntityId} completed with status {@event.Status}.",
            type: "Workflow"
        );

        await _esClient.IndexAsync(item, idx => idx.Index("legal-index"));
    }
}
