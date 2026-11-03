using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Songly.Common.Storage;

/// <summary>One implementation for S3, R2 and MinIO — they speak the same protocol.</summary>
internal sealed class S3ObjectStorage(IAmazonS3 client, IOptions<StorageOptions> options) : IObjectStorage
{
    private readonly StorageOptions _options = options.Value;

    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        try
        {
            await client.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = _options.Bucket,
                    Key = key,
                    ContentType = contentType,
                    InputStream = new MemoryStream(content, writable: false),

                    // A plain signed body, not the SDK's chunked upload: not every S3-compatible
                    // store accepts the streaming signature, and the file is in memory anyway.
                    UseChunkEncoding = false,
                },
                cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ObjectStorageException("Nie udało się zapisać pliku", exception);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            // S3 answers 204 for a key that does not exist, so this is idempotent by itself.
            await client.DeleteObjectAsync(_options.Bucket, key, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ObjectStorageException("Nie udało się skasować pliku", exception);
        }
    }

    public string PublicUrl(string key) => $"{_options.PublicBaseUrl}/{key}";
}
