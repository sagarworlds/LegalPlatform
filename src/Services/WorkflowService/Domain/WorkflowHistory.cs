using System;

namespace WorkflowService.Domain;

public class WorkflowHistory
{
    public Guid HistoryId { get; set; }
    public Guid WorkflowInstanceId { get; set; }
    public virtual WorkflowInstance WorkflowInstance { get; set; } = null!;

    public Guid StepId { get; set; }
    public string Action { get; set; } = string.Empty; 
    public Guid UserId { get; set; }
    public string Comments { get; set; } = string.Empty;
    public DateTime ActionDate { get; set; } = DateTime.UtcNow;

    public WorkflowHistory(Guid historyId, Guid workflowInstanceId, Guid stepId, string action, Guid userId, string comments)
    {
        HistoryId = historyId;
        WorkflowInstanceId = workflowInstanceId;
        StepId = stepId;
        Action = action;
        UserId = userId;
        Comments = comments;
        ActionDate = DateTime.UtcNow;
    }

#pragma warning disable CS8618
    protected WorkflowHistory() { }
#pragma warning restore CS8618
}
