using System;
using System.Collections.Generic;
using SharedKernel.Domain;

namespace WorkflowService.Domain;

public class WorkflowInstance : AggregateRoot<Guid>
{
    public Guid TenantId { get; set; }
    public Guid WorkflowTemplateId { get; set; }
    public virtual WorkflowTemplate WorkflowTemplate { get; set; } = null!;

    public string EntityType { get; set; } = string.Empty; 
    public Guid EntityId { get; set; }
    public int CurrentStepOrder { get; set; } = 1;
    public string Status { get; set; } = "Pending"; 

    public virtual ICollection<WorkflowHistory> HistoryEntries { get; set; } = new List<WorkflowHistory>();

    public WorkflowInstance(Guid id, Guid tenantId, Guid workflowTemplateId, string entityType, Guid entityId) : base(id)
    {
        TenantId = tenantId;
        WorkflowTemplateId = workflowTemplateId;
        EntityType = entityType;
        EntityId = entityId;
        CurrentStepOrder = 1;
        Status = "Pending";
    }

#pragma warning disable CS8618
    protected WorkflowInstance() { }
#pragma warning restore CS8618
}
