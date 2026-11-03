namespace Songly.Common.Media;

/// <summary>
/// The image formats accepted for upload, recognised by their <b>content</b> — never by the
/// <c>Content-Type</c> header or the file name, both of which come from the client and prove
/// nothing (original ADR 0010).
/// </summary>
public sealed class ImageType
{
    public static readonly ImageType Jpeg = new("image/jpeg", "jpg");
    public static readonly ImageType Png = new("image/png", "png");
    public static readonly ImageType Webp = new("image/webp", "webp");

    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagic = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private ImageType(string contentType, string extension)
    {
        ContentType = contentType;
        Extension = extension;
    }

    public string ContentType { get; }

    /// <summary>The extension of the storage key — taken from the detected type, never from the upload's name.</summary>
    public string Extension { get; }

    public static ImageType? Detect(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(JpegMagic))
        {
            return Jpeg;
        }

        if (content.StartsWith(PngMagic))
        {
            return Png;
        }

        return content.Length >= 12 && content.StartsWith("RIFF"u8) && content[8..].StartsWith("WEBP"u8) ? Webp : null;
    }

    public override string ToString() => ContentType;
}
