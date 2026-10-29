using CaseService.Domain;

namespace CaseService.Models;

/// <summary>
/// Response shapes for the Case API. Endpoints return these instead of EF entities: the entities hold
/// back-references (CaseTimeline.Case) that make System.Text.Json throw on a cycle, and they would also
/// leak internals such as the aggregate's domain-event list.
/// </summary>
public sealed record TimelineEntryResponse(Guid Id, string EventDescription, string CreatedBy, DateTime CreatedAt)
{
    public static TimelineEntryResponse From(CaseTimeline entry) =>
        new(entry.Id, entry.EventDescription, entry.CreatedBy, entry.CreatedAt);
}

public sealed record CaseSummaryResponse(
    Guid Id,
    string Title,
    string Description,
    string CaseNumber,
    Guid ClientId,
    Guid AssignedLawyerId,
    string Status,
    DateTime StartDate,
    DateTime? Deadline)
{
    public static CaseSummaryResponse From(Case kase) =>
        new(kase.Id, kase.Title, kase.Description, kase.CaseNumber, kase.ClientId, kase.AssignedLawyerId,
            kase.Status, kase.StartDate, kase.Deadline);
}

public sealed record CaseDetailResponse(
    Guid Id,
    string Title,
    string Description,
    string CaseNumber,
    Guid ClientId,
    Guid AssignedLawyerId,
    string Status,
    DateTime StartDate,
    DateTime? Deadline,
    IReadOnlyList<TimelineEntryResponse> TimelineEntries)
{
    public static CaseDetailResponse From(Case kase) =>
        new(kase.Id, kase.Title, kase.Description, kase.CaseNumber, kase.ClientId, kase.AssignedLawyerId,
            kase.Status, kase.StartDate, kase.Deadline,
            kase.TimelineEntries.OrderBy(t => t.CreatedAt).Select(TimelineEntryResponse.From).ToList());
}
