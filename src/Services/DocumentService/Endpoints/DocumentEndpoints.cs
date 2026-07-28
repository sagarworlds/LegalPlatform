using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Amazon.S3;
using Amazon.S3.Model;
using DocumentService.Domain;
using DocumentService.Infrastructure;
using EventBus.Abstractions;
using Contracts;

namespace DocumentService.Endpoints;

public static class DocumentEndpoints
{
    private const string BucketName = "legal-documents";

    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/documents").RequireAuthorization();

        group.MapPost("/", UploadDocumentAsync).DisableAntiforgery();
        group.MapPost("/{id:guid}/versions", UploadNewVersionAsync).DisableAntiforgery();
        group.MapGet("/{id:guid}/versions", GetDocumentVersionsAsync);
        group.MapGet("/{id:guid}/download", DownloadCurrentVersionAsync);
        group.MapGet("/{id:guid}/versions/{versionId:guid}/download", DownloadSpecificVersionAsync);
        group.MapGet("/{id:guid}/metadata", GetMetadataAsync);

        return endpoints;
    }

    private static async Task<IResult> UploadDocumentAsync(
        HttpContext httpContext,
        IFormFile file,
        string title,
        string category,
        DocumentDbContext dbContext,
        IAmazonS3 s3Client,
        IEventBus eventBus)
    {
        var (userId, tenantId) = GetUserContext(httpContext);

        await EnsureBucketExistsAsync(s3Client);

        using var stream = file.OpenReadStream();
        var sha256Hash = await ComputeSha256HashAsync(stream);

        var fileId = Guid.NewGuid();
        var storagePath = $"{tenantId}/{fileId}_{file.FileName}";
        stream.Position = 0;
        
        await s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = storagePath,
            InputStream = stream
        });

        var document = new Document(Guid.NewGuid(), tenantId, title, category, userId);
        var version = new DocumentVersion(
            fileId,
            document.Id,
            versionNumber: 1,
            storagePath: storagePath,
            fileName: file.FileName,
            fileSize: file.Length,
            mimeType: file.ContentType,
            sha256Hash: sha256Hash,
            createdBy: userId,
            changeComment: "Initial Upload"
        );

        document.Versions.Add(version);
        document.CurrentVersionId = version.VersionId;
        document.CurrentVersion = version;

        dbContext.Documents.Add(document);
        await dbContext.SaveChangesAsync();

        var userIdGuid = userId;
        await eventBus.PublishAsync(new AuditLogCreatedIntegrationEvent
        {
            TenantId = tenantId,
            UserId = userIdGuid,
            ServiceName = "DocumentService",
            EntityType = "Document",
            EntityId = document.Id.ToString(),
            Action = "Upload",
            OldValues = string.Empty,
            NewValues = $"Title: {document.Title}, File: {version.FileName}",
            IPAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            UserAgent = httpContext.Request.Headers["User-Agent"].ToString() ?? "Unknown",
            CorrelationId = httpContext.TraceIdentifier,
            TraceId = httpContext.TraceIdentifier,
            Outcome = "Success"
        });

        return Results.Ok(new { DocumentId = document.Id, VersionId = version.VersionId, Message = "Document uploaded successfully." });
    }

    private static async Task<IResult> UploadNewVersionAsync(
        Guid id,
        HttpContext httpContext,
        IFormFile file,
        string changeComment,
        DocumentDbContext dbContext,
        IAmazonS3 s3Client,
        IEventBus eventBus)
    {
        var (userId, tenantId) = GetUserContext(httpContext);

        var document = await dbContext.Documents
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId);

        if (document == null)
        {
            return Results.NotFound();
        }

        await EnsureBucketExistsAsync(s3Client);

        using var stream = file.OpenReadStream();
        var sha256Hash = await ComputeSha256HashAsync(stream);

        var fileId = Guid.NewGuid();
        var storagePath = $"{tenantId}/{fileId}_{file.FileName}";
        stream.Position = 0;

        await s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = storagePath,
            InputStream = stream
        });

        foreach (var oldVer in document.Versions)
        {
            oldVer.IsCurrent = false;
        }

        var nextVersionNumber = document.Versions.Count + 1;
        var version = new DocumentVersion(
            fileId,
            document.Id,
            versionNumber: nextVersionNumber,
            storagePath: storagePath,
            fileName: file.FileName,
            fileSize: file.Length,
            mimeType: file.ContentType,
            sha256Hash: sha256Hash,
            createdBy: userId,
            changeComment: changeComment
        );

        document.Versions.Add(version);
        document.CurrentVersionId = version.VersionId;
        document.CurrentVersion = version;

        await dbContext.SaveChangesAsync();

        var userIdGuid = userId;
        await eventBus.PublishAsync(new AuditLogCreatedIntegrationEvent
        {
            TenantId = tenantId,
            UserId = userIdGuid,
            ServiceName = "DocumentService",
            EntityType = "DocumentVersion",
            EntityId = version.VersionId.ToString(),
            Action = "UploadVersion",
            OldValues = string.Empty,
            NewValues = $"DocumentId: {document.Id}, Version: {version.VersionNumber}, File: {version.FileName}",
            IPAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            UserAgent = httpContext.Request.Headers["User-Agent"].ToString() ?? "Unknown",
            CorrelationId = httpContext.TraceIdentifier,
            TraceId = httpContext.TraceIdentifier,
            Outcome = "Success"
        });

        return Results.Ok(new { DocumentId = document.Id, VersionId = version.VersionId, Message = "New document version uploaded successfully." });
    }

    private static async Task<IResult> GetDocumentVersionsAsync(
        Guid id,
        HttpContext httpContext,
        DocumentDbContext dbContext)
    {
        var (_, tenantId) = GetUserContext(httpContext);

        var versions = await dbContext.DocumentVersions
            .Where(v => v.DocumentId == id && v.Document.TenantId == tenantId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new
            {
                v.VersionId,
                v.VersionNumber,
                v.FileName,
                v.FileSize,
                v.MIMEType,
                v.SHA256Hash,
                v.CreatedBy,
                v.CreatedDate,
                v.ChangeComment,
                v.IsCurrent
            })
            .ToListAsync();

        return Results.Ok(versions);
    }

    private static async Task<IResult> DownloadCurrentVersionAsync(
        Guid id,
        HttpContext httpContext,
        DocumentDbContext dbContext,
        IAmazonS3 s3Client,
        IEventBus eventBus)
    {
        var (_, tenantId) = GetUserContext(httpContext);

        var document = await dbContext.Documents
            .Include(d => d.CurrentVersion)
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId);

        if (document == null || document.CurrentVersion == null)
        {
            return Results.NotFound();
        }

        var response = await s3Client.GetObjectAsync(BucketName, document.CurrentVersion.StoragePath);

        var userIdGuid = Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var u) ? u : Guid.Empty;
        await eventBus.PublishAsync(new AuditLogCreatedIntegrationEvent
        {
            TenantId = tenantId,
            UserId = userIdGuid,
            ServiceName = "DocumentService",
            EntityType = "Document",
            EntityId = document.Id.ToString(),
            Action = "Download",
            OldValues = string.Empty,
            NewValues = $"FileName: {document.CurrentVersion.FileName}",
            IPAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            UserAgent = httpContext.Request.Headers["User-Agent"].ToString() ?? "Unknown",
            CorrelationId = httpContext.TraceIdentifier,
            TraceId = httpContext.TraceIdentifier,
            Outcome = "Success"
        });

        return Results.File(response.ResponseStream, document.CurrentVersion.MIMEType, document.CurrentVersion.FileName);
    }

    private static async Task<IResult> DownloadSpecificVersionAsync(
        Guid id,
        Guid versionId,
        HttpContext httpContext,
        DocumentDbContext dbContext,
        IAmazonS3 s3Client,
        IEventBus eventBus)
    {
        var (_, tenantId) = GetUserContext(httpContext);

        var version = await dbContext.DocumentVersions
            .Include(v => v.Document)
            .FirstOrDefaultAsync(v => v.VersionId == versionId && v.DocumentId == id && v.Document.TenantId == tenantId);

        if (version == null)
        {
            return Results.NotFound();
        }

        var response = await s3Client.GetObjectAsync(BucketName, version.StoragePath);

        var userIdGuid = Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var u) ? u : Guid.Empty;
        await eventBus.PublishAsync(new AuditLogCreatedIntegrationEvent
        {
            TenantId = tenantId,
            UserId = userIdGuid,
            ServiceName = "DocumentService",
            EntityType = "DocumentVersion",
            EntityId = version.VersionId.ToString(),
            Action = "DownloadVersion",
            OldValues = string.Empty,
            NewValues = $"DocumentId: {id}, Version: {version.VersionNumber}, FileName: {version.FileName}",
            IPAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            UserAgent = httpContext.Request.Headers["User-Agent"].ToString() ?? "Unknown",
            CorrelationId = httpContext.TraceIdentifier,
            TraceId = httpContext.TraceIdentifier,
            Outcome = "Success"
        });

        return Results.File(response.ResponseStream, version.MIMEType, version.FileName);
    }

    private static async Task<IResult> GetMetadataAsync(
        Guid id,
        HttpContext httpContext,
        DocumentDbContext dbContext)
    {
        var (_, tenantId) = GetUserContext(httpContext);

        var document = await dbContext.Documents
            .Include(d => d.CurrentVersion)
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId);

        if (document == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(document);
    }

    private static (Guid UserId, Guid TenantId) GetUserContext(HttpContext httpContext)
    {
        var userIdStr = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenantIdStr = httpContext.User.FindFirstValue("organizationId") ?? httpContext.User.FindFirstValue("tenant_id");

        var userId = Guid.TryParse(userIdStr, out var u) ? u : Guid.Empty;
        var tenantId = Guid.TryParse(tenantIdStr, out var t) ? t : Guid.Empty;

        return (userId, tenantId);
    }

    private static async Task EnsureBucketExistsAsync(IAmazonS3 s3Client)
    {
        var bucketExists = await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(s3Client, BucketName);
        if (!bucketExists)
        {
            await s3Client.PutBucketAsync(BucketName);
        }
    }

    private static async Task<string> ComputeSha256HashAsync(Stream stream)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}
