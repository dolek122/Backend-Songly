namespace Songly.Common.Media;

/// <summary>The size limits of an uploaded image, the same as the original's multipart limits.</summary>
public static class UploadLimits
{
    /// <summary>5 MB for the file itself.</summary>
    public const long MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>6 MB for the whole request — room for the multipart framing around the file.</summary>
    public const long MaxRequestBytes = 6 * 1024 * 1024;
}
