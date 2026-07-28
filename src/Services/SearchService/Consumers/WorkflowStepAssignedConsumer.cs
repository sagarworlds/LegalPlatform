using System;
using System.Threading.Tasks;
using MassTransit;
using Contracts;
using Elastic.Clients.Elasticsearch;
using SearchService.Domain;

namespace SearchService.Consumers;

public class WorkflowStepAssignedConsumer : IConsumer<WorkflowStepAssignedIntegrationEvent>
{
    private readonly ElasticsearchClient _esClient;

    public WorkflowStepAssignedConsumer(ElasticsearchClient esClient)
    {
        _esClient = esClient;
    }

    public async Task Consume(ConsumeContext<WorkflowStepAssignedIntegrationEvent> context)
    {
        var @event = context.Message;

        var item = new SearchItem(
            id: @event.WorkflowInstanceId.ToString(),
            tenantId: @event.TenantId,
            title: $"Workflow Step: {@event.StepName}",
            description: $"Workflow instance active for {@event.EntityType} (ID: {@event.EntityId}) assigned to role {@event.AssignedRole}.",
            type: "Workflow"
        );

        await _esClient.IndexAsync(item, idx => idx.Index("legal-index"));
    }
}
