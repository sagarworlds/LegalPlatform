using System;

namespace WorkflowService.Domain;

public class WorkflowStep
{
    public Guid StepId { get; set; }
    public Guid WorkflowTemplateId { get; set; }
    public virtual WorkflowTemplate WorkflowTemplate { get; set; } = null!;

    public int StepOrder { get; set; }
    public string StepName { get; set; } = string.Empty;
    public string AssignedRole { get; set; } = string.Empty; 
    public int DueInDays { get; set; } = 3;
    public bool IsRequired { get; set; } = true;

    public WorkflowStep(Guid stepId, Guid workflowTemplateId, int stepOrder, string stepName, string assignedRole, int dueInDays, bool isRequired)
    {
        StepId = stepId;
        WorkflowTemplateId = workflowTemplateId;
        StepOrder = stepOrder;
        StepName = stepName;
        AssignedRole = assignedRole;
        DueInDays = dueInDays;
        IsRequired = isRequired;
    }

#pragma warning disable CS8618
    protected WorkflowStep() { }
#pragma warning restore CS8618
}
