using System;
using System.Collections.Generic;
using SharedKernel.Domain;

namespace CaseService.Domain;

public class Case : AggregateRoot<Guid>
{
    public Guid TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CaseNumber { get; set; } = string.Empty;
    public Guid ClientId { get; set; }
    public Guid AssignedLawyerId { get; set; }
    public string Status { get; set; } = "Open"; 
    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public DateTime? Deadline { get; set; }

    public virtual ICollection<CaseTimeline> TimelineEntries { get; set; } = new List<CaseTimeline>();

    public Case(Guid id, Guid tenantId, string title, string description, string caseNumber, Guid clientId, Guid assignedLawyerId, DateTime? deadline) : base(id)
    {
        TenantId = tenantId;
        Title = title;
        Description = description;
        CaseNumber = caseNumber;
        ClientId = clientId;
        AssignedLawyerId = assignedLawyerId;
        Deadline = deadline;
        Status = "Open";
        StartDate = DateTime.UtcNow;
    }

#pragma warning disable CS8618
    protected Case() { }
#pragma warning restore CS8618
}
