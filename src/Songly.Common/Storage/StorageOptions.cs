namespace Songly.Common.Storage;

public sealed class StorageOptions
{
    public const string Section = "Songly:Storage";

    public string Bucket { get; set; } = "songly-media";

    /// <summary>"auto" for Cloudflare R2; MinIO accepts any region.</summary>
    public string Region { get; set; } = "auto";

    /// <summary>
    /// The storage API's address. Empty means real AWS S3 — the SDK builds the endpoint from the
    /// region. Locally and in tests MinIO, in production Cloudflare R2.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// The prefix of public file addresses, without a trailing slash. Deliberately separate from
    /// <see cref="Endpoint"/>: behind R2 or CloudFront clients read through the CDN's domain while we
    /// upload through the API. Deriving one from the other would work only in development and break
    /// exactly where checking it is hardest.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// <c>host/bucket/key</c> instead of virtual hosts (<c>bucket.host/key</c>). MinIO locally has no
    /// wildcard DNS, so without this it cannot be used at all.
    /// </summary>
    public bool PathStyleAccess { get; set; } = true;

    /// <summary>One storage call. The SDK's default is 100 seconds — an upload request would hang that long.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
