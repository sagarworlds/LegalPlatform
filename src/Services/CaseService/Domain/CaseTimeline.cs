using System;

namespace CaseService.Domain;

public class CaseTimeline
{
    public Guid Id { get; set; }
    public Guid CaseId { get; set; }
    public virtual Case Case { get; set; } = null!;
    
    public string EventDescription { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public CaseTimeline(Guid id, Guid caseId, string eventDescription, string createdBy)
    {
        Id = id;
        CaseId = caseId;
        EventDescription = eventDescription;
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
    }

#pragma warning disable CS8618
    protected CaseTimeline() { }
#pragma warning restore CS8618
}
