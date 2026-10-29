using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DocumentService.Domain;
using DocumentService.Infrastructure;
using DocumentService.Models;
using DocumentService.Storage;
using Auditing;
using ServiceDefaults.Security;

namespace DocumentService.Endpoints;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TenantMember rejects tokens without an organization claim (403) before any handler runs.
        var group = endpoints.MapGroup("/api/v1/documents").RequireAuthorization(Policies.TenantMember);

        group.MapPost("/", UploadDocumentAsync).DisableAntiforgery();
        group.MapPost("/{id:guid}/versions", UploadNewVersionAsync).DisableAntiforgery();
        group.MapGet("/{id:guid}/versions", GetDocumentVersionsAsync);
        group.MapGet("/{id:guid}/download", DownloadCurrentVersionAsync);
        group.MapGet("/{id:guid}/versions/{versionId:guid}/download", DownloadSpecificVersionAsync);
        group.MapGet("/{id:guid}/metadata", GetMetadataAsync);

        return endpoints;
    }

    // title/category/changeComment are bound from the multipart body ([FromForm]). Without the attribute, minimal APIs
    // read plain string parameters from the query string, so the portal's multipart upload was rejected with 400.
    internal static async Task<IResult> UploadDocumentAsync(
        UserContext user,
        HttpContext httpContext,
        IFormFile file,
        [FromForm] string title,
        [FromForm] string category,
        DocumentDbContext dbContext,
        IDocumentStorage storage,
        IAuditPublisher audit,
        ILoggerFactory loggerFactory)
    {
        var fileName = SafeFileName(file.FileName);
        var fileId = Guid.NewGuid();
        var storagePath = $"{user.TenantId}/{fileId}_{fileName}";

        await using var stream = file.OpenReadStream();
        var sha256Hash = await ComputeSha256HashAsync(stream);
        stream.Position = 0;

        await storage.SaveAsync(storagePath, stream);

        var document = new Document(Guid.NewGuid(), user.TenantId, title, category, user.UserId);
        var version = document.AddVersion(
            fileId, storagePath, fileName, file.Length, file.ContentType, sha256Hash, user.UserId, "Initial Upload");

        dbContext.Documents.Add(document);
        await SaveOrDiscardBlobAsync(dbContext, storage, storagePath, loggerFactory);

        await audit.PublishAsync(httpContext, AuditEntry.By(user,
            entityType: "Document", entityId: document.Id.ToString(), action: "Upload",
            newValues: $"Title: {document.Title}, File: {version.FileName}"));

        return Results.Ok(new { DocumentId = document.Id, VersionId = version.VersionId, Message = "Document uploaded successfully." });
    }

    internal static async Task<IResult> UploadNewVersionAsync(
        Guid id,
        UserContext user,
        HttpContext httpContext,
        IFormFile file,
        [FromForm] string changeComment,
        DocumentDbContext dbContext,
        IDocumentStorage storage,
        IAuditPublisher audit,
        ILoggerFactory loggerFactory)
    {
        var document = await dbContext.Documents
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == user.TenantId);

        if (document == null)
        {
            return Results.NotFound();
        }

        var fileName = SafeFileName(file.FileName);
        var fileId = Guid.NewGuid();
        var storagePath = $"{user.TenantId}/{fileId}_{fileName}";

        await using var stream = file.OpenReadStream();
        var sha256Hash = await ComputeSha256HashAsync(stream);
        stream.Position = 0;

        await storage.SaveAsync(storagePath, stream);

        var version = document.AddVersion(
            fileId, storagePath, fileName, file.Length, file.ContentType, sha256Hash, user.UserId, changeComment ?? string.Empty);

        await SaveOrDiscardBlobAsync(dbContext, storage, storagePath, loggerFactory);

        await audit.PublishAsync(httpContext, AuditEntry.By(user,
            entityType: "DocumentVersion", entityId: version.VersionId.ToString(), action: "UploadVersion",
            newValues: $"DocumentId: {document.Id}, Version: {version.VersionNumber}, File: {version.FileName}"));

        return Results.Ok(new { DocumentId = document.Id, VersionId = version.VersionId, Message = "New document version uploaded successfully." });
    }

    internal static async Task<IResult> GetDocumentVersionsAsync(
        Guid id,
        UserContext user,
        DocumentDbContext dbContext)
    {
        var versions = await dbContext.DocumentVersions
            .AsNoTracking()
            .Where(v => v.DocumentId == id && v.Document.TenantId == user.TenantId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync();

        return Results.Ok(versions.Select(DocumentVersionResponse.From));
    }

    internal static async Task<IResult> DownloadCurrentVersionAsync(
        Guid id,
        UserContext user,
        HttpContext httpContext,
        DocumentDbContext dbContext,
        IDocumentStorage storage,
        IAuditPublisher audit)
    {
        var version = await dbContext.DocumentVersions
            .AsNoTracking()
            .Where(v => v.DocumentId == id && v.IsCurrent && v.Document.TenantId == user.TenantId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync();

        if (version == null)
        {
            return Results.NotFound();
        }

        var content = await storage.OpenReadAsync(version.StoragePath);

        await audit.PublishAsync(httpContext, AuditEntry.By(user,
            entityType: "Document", entityId: id.ToString(), action: "Download",
            newValues: $"FileName: {version.FileName}"));

        return Results.File(content, version.MIMEType, version.FileName);
    }

    internal static async Task<IResult> DownloadSpecificVersionAsync(
        Guid id,
        Guid versionId,
        UserContext user,
        HttpContext httpContext,
        DocumentDbContext dbContext,
        IDocumentStorage storage,
        IAuditPublisher audit)
    {
        var version = await dbContext.DocumentVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.VersionId == versionId && v.DocumentId == id && v.Document.TenantId == user.TenantId);

        if (version == null)
        {
            return Results.NotFound();
        }

        var content = await storage.OpenReadAsync(version.StoragePath);

        await audit.PublishAsync(httpContext, AuditEntry.By(user,
            entityType: "DocumentVersion", entityId: version.VersionId.ToString(), action: "DownloadVersion",
            newValues: $"DocumentId: {id}, Version: {version.VersionNumber}, FileName: {version.FileName}"));

        return Results.File(content, version.MIMEType, version.FileName);
    }

    internal static async Task<IResult> GetMetadataAsync(
        Guid id,
        UserContext user,
        DocumentDbContext dbContext)
    {
        var document = await dbContext.Documents
            .AsNoTracking()
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == user.TenantId);

        if (document == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(DocumentResponse.From(document));
    }

    /// <summary>
    /// The blob is written before the database row, so a failed save would leave an object nothing references.
    /// On <see cref="DbUpdateException"/> the blob is deleted (best effort, logged if that also fails) and the
    /// original exception is rethrown.
    /// </summary>
    private static async Task SaveOrDiscardBlobAsync(
        DocumentDbContext dbContext,
        IDocumentStorage storage,
        string storagePath,
        ILoggerFactory loggerFactory)
    {
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            var logger = loggerFactory.CreateLogger(typeof(DocumentEndpoints).FullName!);
            try
            {
                await storage.DeleteAsync(storagePath);
            }
            catch (DocumentStorageException cleanupFailure)
            {
                logger.LogError(cleanupFailure, "Saving the document row failed and the uploaded blob {StoragePath} could not be removed; it is orphaned.", storagePath);
            }

            throw;
        }
    }

    /// <summary>Strips any directory part a client put in the file name so it cannot shape the object key.</summary>
    private static string SafeFileName(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }

    private static async Task<string> ComputeSha256HashAsync(Stream stream)
    {
        var hashBytes = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hashBytes);
    }
}
