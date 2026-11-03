namespace Songly.Common.Storage;

/// <summary>
/// Durable storage of binary files outside the relational database (original Claude.md §4) — S3,
/// Cloudflare R2 in production, MinIO locally and in tests.
/// </summary>
/// <remarks>
/// It knows keys, bytes and content types only — not whether it stores an avatar, a post photo or
/// anything else. A key is an opaque name whose structure the domain module decides.
/// </remarks>
public interface IObjectStorage
{
    /// <exception cref="ObjectStorageException">The storage failed.</exception>
    Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes an object. <b>Idempotent</b> — no object under the key is not an error: callers clean
    /// up after something that has to go anyway.
    /// </summary>
    /// <remarks>
    /// A storage <b>failure</b> is an error, and the caller decides what to do with it (original
    /// ADR 0049). It used to be swallowed, and a cleanup that stops at the first failure then went on
    /// deleting rows and orphaning every file while the storage was down.
    /// </remarks>
    /// <exception cref="ObjectStorageException">The storage failed.</exception>
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// The public address clients load the file from. Separate from where it is uploaded to: behind
    /// a CDN (R2, CloudFront) these are two different domains, and clients must get the one in front
    /// of the cache.
    /// </summary>
    string PublicUrl(string key);
}
