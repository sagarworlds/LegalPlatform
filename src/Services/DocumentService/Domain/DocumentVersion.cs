using System;

namespace DocumentService.Domain;

public class DocumentVersion
{
    public Guid VersionId { get; set; }
    public Guid DocumentId { get; set; }
    public virtual Document Document { get; set; } = null!;
    
    public int VersionNumber { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string MIMEType { get; set; } = string.Empty;
    public string SHA256Hash { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string ChangeComment { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }

    public DocumentVersion(Guid versionId, Guid documentId, int versionNumber, string storagePath, string fileName, long fileSize, string mimeType, string sha256Hash, Guid createdBy, string changeComment)
    {
        VersionId = versionId;
        DocumentId = documentId;
        VersionNumber = versionNumber;
        StoragePath = storagePath;
        FileName = fileName;
        FileSize = fileSize;
        MIMEType = mimeType;
        SHA256Hash = sha256Hash;
        CreatedBy = createdBy;
        ChangeComment = changeComment;
        CreatedDate = DateTime.UtcNow;
        IsCurrent = true;
    }

#pragma warning disable CS8618
    protected DocumentVersion() { }
#pragma warning restore CS8618
}
