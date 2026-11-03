namespace Songly.Common.Media;

/// <summary>
/// An uploaded image after its format is recognised and its metadata removed (original ADR 0047) —
/// the only form in which bytes from a client reach the storage.
/// </summary>
/// <remarks>
/// <para>
/// A photo from a camera carries GPS coordinates, the phone model and the time to the second in
/// its EXIF. Avatars and post photos are public by design, so a photo taken at home told everyone
/// where the user lives. Android re-encodes photos before sending and loses the metadata on the
/// way, but that is a property of one client, not a guarantee of the server.
/// </para>
/// <para>
/// <b>No re-encoding.</b> The container is rewritten (JPEG segments, PNG and WebP chunks) and the
/// image data copied byte for byte: the quality does not change, no WebP decoder is needed, and the
/// file is never decompressed into memory — a 5 MB PNG can be gigabytes of pixels.
/// </para>
/// <para>
/// <b>A file whose structure cannot be read is rejected.</b> Letting it through unchanged would let
/// its metadata through too.
/// </para>
/// </remarks>
public sealed record SanitizedImage(ImageType Type, byte[] Content)
{
    /// <exception cref="UnsupportedImageException">Not a JPEG, PNG or WebP, or not a readable one.</exception>
    public static SanitizedImage From(byte[] upload)
    {
        var type = ImageType.Detect(upload) ?? throw new UnsupportedImageException();
        var stripped = type == ImageType.Jpeg ? JpegMetadata.Strip(upload)
            : type == ImageType.Png ? PngMetadata.Strip(upload)
            : WebpMetadata.Strip(upload);
        return new SanitizedImage(type, stripped);
    }
}
