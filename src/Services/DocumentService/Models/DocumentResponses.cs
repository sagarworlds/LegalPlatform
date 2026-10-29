using DocumentService.Domain;

namespace DocumentService.Models;

/// <summary>
/// Response shapes for the Document API. Endpoints return these instead of EF entities: the entities hold
/// back-references (DocumentVersion.Document) that make System.Text.Json throw on a cycle, and they expose
/// StoragePath, an internal object-store key clients have no use for.
/// </summary>
public sealed record DocumentVersionResponse(
    Guid VersionId,
    int VersionNumber,
    string FileName,
    long FileSize,
    string MIMEType,
    string SHA256Hash,
    Guid CreatedBy,
    DateTime CreatedDate,
    string ChangeComment,
    bool IsCurrent)
{
    public static DocumentVersionResponse From(DocumentVersion v) =>
        new(v.VersionId, v.VersionNumber, v.FileName, v.FileSize, v.MIMEType, v.SHA256Hash,
            v.CreatedBy, v.CreatedDate, v.ChangeComment, v.IsCurrent);
}

public sealed record DocumentResponse(
    Guid Id,
    string Title,
    string Category,
    Guid OwnerId,
    DateTime CreatedDate,
    string Status,
    DocumentVersionResponse? CurrentVersion)
{
    /// <summary>The document's <see cref="Document.Versions"/> must be loaded.</summary>
    public static DocumentResponse From(Document d) =>
        new(d.Id, d.Title, d.Category, d.OwnerId, d.CreatedDate, d.Status,
            d.CurrentVersion is null ? null : DocumentVersionResponse.From(d.CurrentVersion));
}
