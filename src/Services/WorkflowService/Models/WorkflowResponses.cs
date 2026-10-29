using WorkflowService.Domain;

namespace WorkflowService.Models;

/// <summary>
/// Response shapes for the Workflow API. Endpoints return these instead of EF entities: the entities hold
/// back-references (WorkflowStep.WorkflowTemplate, WorkflowHistory.WorkflowInstance) that make
/// System.Text.Json throw on a cycle.
/// </summary>
public sealed record WorkflowStepResponse(Guid StepId, int StepOrder, string StepName, string AssignedRole, int DueInDays, bool IsRequired)
{
    public static WorkflowStepResponse From(WorkflowStep step) =>
        new(step.StepId, step.StepOrder, step.StepName, step.AssignedRole, step.DueInDays, step.IsRequired);
}

public sealed record WorkflowTemplateResponse(
    Guid Id,
    string Name,
    string Description,
    bool IsActive,
    int Version,
    IReadOnlyList<WorkflowStepResponse> Steps)
{
    public static WorkflowTemplateResponse From(WorkflowTemplate template) =>
        new(template.Id, template.Name, template.Description, template.IsActive, template.Version,
            template.Steps.OrderBy(s => s.StepOrder).Select(WorkflowStepResponse.From).ToList());
}

public sealed record WorkflowHistoryResponse(Guid HistoryId, Guid StepId, string Action, Guid UserId, string Comments, DateTime ActionDate)
{
    public static WorkflowHistoryResponse From(WorkflowHistory history) =>
        new(history.HistoryId, history.StepId, history.Action, history.UserId, history.Comments, history.ActionDate);
}

public sealed record WorkflowInstanceResponse(
    Guid Id,
    Guid WorkflowTemplateId,
    string TemplateName,
    string EntityType,
    Guid EntityId,
    int CurrentStepOrder,
    string Status,
    IReadOnlyList<WorkflowStepResponse> Steps,
    IReadOnlyList<WorkflowHistoryResponse> History)
{
    /// <summary>The instance's <see cref="WorkflowInstance.WorkflowTemplate"/> (with its steps) must be loaded.</summary>
    public static WorkflowInstanceResponse From(WorkflowInstance instance) =>
        new(instance.Id, instance.WorkflowTemplateId, instance.WorkflowTemplate.Name, instance.EntityType,
            instance.EntityId, instance.CurrentStepOrder, instance.Status,
            instance.WorkflowTemplate.Steps.OrderBy(s => s.StepOrder).Select(WorkflowStepResponse.From).ToList(),
            instance.HistoryEntries.OrderBy(h => h.ActionDate).Select(WorkflowHistoryResponse.From).ToList());
}
