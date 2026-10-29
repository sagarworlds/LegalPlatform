namespace DocumentService.Storage;

/// <summary>
/// Blob storage for document content. Endpoints depend on this abstraction rather than on the S3 SDK, so the
/// storage backend can change, and handlers can be tested, without an S3 server.
/// </summary>
public interface IDocumentStorage
{
    /// <exception cref="DocumentStorageException">The content could not be stored.</exception>
    Task SaveAsync(string storagePath, Stream content, CancellationToken cancellationToken = default);

    /// <exception cref="DocumentStorageException">The content could not be read, for example because it does not exist.</exception>
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);

    /// <exception cref="DocumentStorageException">The content could not be deleted.</exception>
    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
}

/// <summary>A blob storage operation failed. Wraps the backend-specific exception as <see cref="Exception.InnerException"/>.</summary>
public sealed class DocumentStorageException : Exception
{
    public DocumentStorageException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
