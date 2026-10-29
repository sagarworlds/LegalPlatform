using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;

namespace DocumentService.Storage;

/// <summary>S3-compatible (MinIO) implementation of <see cref="IDocumentStorage"/>.</summary>
public sealed class S3DocumentStorage : IDocumentStorage
{
    private const string BucketName = "legal-documents";

    private readonly IAmazonS3 _s3Client;

    public S3DocumentStorage(IAmazonS3 s3Client)
    {
        _s3Client = s3Client;
    }

    public async Task SaveAsync(string storagePath, Stream content, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureBucketExistsAsync();
            await _s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = BucketName,
                Key = storagePath,
                InputStream = content
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex)
        {
            throw new DocumentStorageException($"Failed to store '{storagePath}' in bucket '{BucketName}'.", ex);
        }
    }

    public async Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _s3Client.GetObjectAsync(BucketName, storagePath, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex)
        {
            throw new DocumentStorageException($"Failed to read '{storagePath}' from bucket '{BucketName}'.", ex);
        }
    }

    public async Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await _s3Client.DeleteObjectAsync(BucketName, storagePath, cancellationToken);
        }
        catch (AmazonS3Exception ex)
        {
            throw new DocumentStorageException($"Failed to delete '{storagePath}' from bucket '{BucketName}'.", ex);
        }
    }

    private async Task EnsureBucketExistsAsync()
    {
        if (!await AmazonS3Util.DoesS3BucketExistV2Async(_s3Client, BucketName))
        {
            await _s3Client.PutBucketAsync(BucketName);
        }
    }
}
