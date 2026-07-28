using System;
using System.Collections.Generic;
using SharedKernel.Domain;

namespace WorkflowService.Domain;

public class WorkflowTemplate : AggregateRoot<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;

    public virtual ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();

    public WorkflowTemplate(Guid id, string name, string description) : base(id)
    {
        Name = name;
        Description = description;
        IsActive = true;
        Version = 1;
    }

#pragma warning disable CS8618
    protected WorkflowTemplate() { }
#pragma warning restore CS8618
}
