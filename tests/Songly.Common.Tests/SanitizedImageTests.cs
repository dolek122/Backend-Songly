using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;
using Songly.Common.Media;
using Songly.Tests.Shared;
using static Songly.Tests.Shared.TestImages;

namespace Songly.Common.Tests;

/// <summary>
/// Removing metadata from uploads (original ADR 0047). Ported from the original
/// <c>SanitizedImageTest</c>. Where the original decoded the result to prove it is still an image,
/// these compare it byte for byte with the file it must be — a clean image the stripping is known
/// to leave untouched, plus exactly the block it writes — which is the stronger claim.
/// </summary>
public sealed class SanitizedImageTests
{
    [Fact]
    public void A_clean_jpeg_passes_unchanged()
    {
        var jpeg = Jpeg();

        var image = SanitizedImage.From(jpeg);

        Assert.Same(ImageType.Jpeg, image.Type);
        Assert.Equal(jpeg, image.Content);
    }

    /// <summary>Several scans, with tables between them — the scan reader must find each one's end.</summary>
    [Fact]
    public void A_progressive_jpeg_passes_unchanged() =>
        Assert.Equal(ProgressiveJpeg(), SanitizedImage.From(ProgressiveJpeg()).Content);

    /// <summary>
    /// Metadata between two scans goes too. A reader that took everything after the first scan as
    /// image data would pass the clean file above and let this through.
    /// </summary>
    [Fact]
    public void A_comment_between_progressive_scans_is_removed()
    {
        var jpeg = ProgressiveJpeg();
        var firstScan = IndexOf(jpeg, [0xFF, 0xDA]);
        var secondScan = firstScan + 2 + IndexOf(jpeg[(firstScan + 2)..], [0xFF, 0xDA]);
        byte[] withComment = [.. jpeg[..secondScan], .. Segment(0xFE, Secret), .. jpeg[secondScan..]];

        Assert.Equal(jpeg, SanitizedImage.From(withComment).Content);
    }

    /// <summary>
    /// The point: nothing of the EXIF block gets through except the orientation — and that does,
    /// because without it a portrait from a phone would lie on its side.
    /// </summary>
    [Fact]
    public void Jpeg_exif_is_reduced_to_the_orientation()
    {
        var stripped = SanitizedImage.From(JpegWithExif(6)).Content;

        Assert.False(Contains(stripped, Secret));
        var exif = IndexOf(stripped, "Exif\0\0"u8);
        Assert.Equal(6, ExifOrientation.Read(stripped.AsSpan(exif)));

        var jpeg = Jpeg();
        var payload = ExifOrientation.JpegPayload(6);
        byte[] expected = [.. jpeg[..2], 0xFF, 0xE1, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload, .. jpeg[2..]];
        Assert.Equal(expected, stripped);
    }

    [Fact]
    public void Jpeg_exif_with_the_normal_orientation_disappears_entirely() =>
        Assert.Equal(Jpeg(), SanitizedImage.From(JpegWithExif(1)).Content);

    /// <summary>XMP, IPTC (APP13), a comment, an MPF block and what a phone appends after EOI — each can carry a position or a second picture.</summary>
    [Fact]
    public void Jpeg_metadata_segments_and_the_trailer_are_removed()
    {
        var jpeg = Jpeg();
        byte[] withMetadata =
        [
            .. jpeg[..2],
            .. Segment(0xE1, "http://ns.adobe.com/xap/1.0/\0<x:xmpmeta>" + Secret),
            .. Segment(0xED, "Photoshop 3.0\0" + Secret),
            .. Segment(0xFE, Secret),
            .. Segment(0xE2, "MPF\0" + Secret),
            .. jpeg[2..],
            .. Encoding.ASCII.GetBytes("ftypmp4 " + Secret),
        ];

        Assert.Equal(jpeg, SanitizedImage.From(withMetadata).Content);
    }

    /// <summary>A JFIF header with a thumbnail goes too: a thumbnail of a cropped photo has shown the whole of it.</summary>
    [Fact]
    public void A_jfif_header_with_a_thumbnail_is_removed()
    {
        var jpeg = Jpeg();
        var jfifEnd = 2 + 2 + 16;
        byte[] thumbnail = [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 1, 1, 0xAA, 0xBB, 0xCC];
        byte[] withThumbnail = [.. jpeg[..2], .. Segment(0xE0, thumbnail), .. jpeg[jfifEnd..]];

        Assert.Equal([.. jpeg[..2], .. jpeg[jfifEnd..]], SanitizedImage.From(withThumbnail).Content);
    }

    /// <summary>The colour profile changes how the photo looks, so it stays.</summary>
    [Fact]
    public void The_jpeg_icc_profile_is_kept()
    {
        var jpeg = Jpeg();
        byte[] withIcc = [.. jpeg[..2], .. Segment(0xE2, "ICC_PROFILE\0\u0001\u0001profil"), .. jpeg[2..]];

        Assert.Equal(withIcc, SanitizedImage.From(withIcc).Content);
    }

    /// <summary>
    /// Magic bytes are not enough: a file whose structure cannot be read must not pass unchanged,
    /// because its metadata would pass too.
    /// </summary>
    [Fact]
    public void Malformed_files_are_rejected()
    {
        var jpeg = Jpeg();
        var png = Png();

        Assert.Throws<UnsupportedImageException>(() => SanitizedImage.From([0xFF, 0xD8, 0xFF, (byte)'d', (byte)'a']));
        Assert.Throws<UnsupportedImageException>(() => SanitizedImage.From(jpeg[..^2]));
        Assert.Throws<UnsupportedImageException>(() => SanitizedImage.From(png[..^4]));
        Assert.Throws<UnsupportedImageException>(() => SanitizedImage.From([.. "RIFF"u8, .. Le(1000), .. "WEBPVP8 "u8]));
        Assert.Throws<UnsupportedImageException>(() => SanitizedImage.From(Encoding.ASCII.GetBytes("MZ to nie jest obraz")));
    }

    [Fact]
    public void A_clean_png_passes_unchanged()
    {
        var png = Png();

        var image = SanitizedImage.From(png);

        Assert.Same(ImageType.Png, image.Type);
        Assert.Equal(png, image.Content);
    }

    [Fact]
    public void Png_text_and_time_chunks_and_the_trailer_are_removed()
    {
        byte[][] files =
        [
            PngWithChunk("tEXt", Encoding.ASCII.GetBytes("Comment\0" + Secret)),
            PngWithChunk("iTXt", Encoding.ASCII.GetBytes("XML:com.adobe.xmp\0\0\0\0\0" + Secret)),
            PngWithChunk("tIME", [0x07, 0xEA, 10, 21, 12, 0, 0]),
            [.. Png(), .. Encoding.ASCII.GetBytes(Secret)],
        ];

        Assert.All(files, file => Assert.Equal(Png(), SanitizedImage.From(file).Content));
    }

    [Fact]
    public void Png_exif_is_reduced_to_the_orientation_with_a_valid_checksum()
    {
        var stripped = SanitizedImage.From(PngWithChunk("eXIf", Tiff(8))).Content;

        Assert.False(Contains(stripped, Secret));
        var type = IndexOf(stripped, "eXIf"u8);
        var length = BinaryPrimitives.ReadInt32BigEndian(stripped.AsSpan(type - 4));
        Assert.Equal(8, ExifOrientation.Read(stripped.AsSpan(type + 4, length)));
        Assert.Equal(Crc32.HashToUInt32(stripped.AsSpan(type, 4 + length)), BinaryPrimitives.ReadUInt32BigEndian(stripped.AsSpan(type + 4 + length)));
        Assert.Equal(PngWithChunk("eXIf", ExifOrientation.MinimalTiff(8)), stripped);
    }

    /// <summary>A simple WebP has nowhere to keep metadata, so nothing in it changes.</summary>
    [Fact]
    public void A_simple_webp_passes_unchanged()
    {
        var webp = Webp(Chunk("VP8L", [0x2F, 1, 2, 3, 4]));

        var image = SanitizedImage.From(webp);

        Assert.Same(ImageType.Webp, image.Type);
        Assert.Equal(webp, image.Content);
    }

    /// <summary>
    /// The VP8X flags must follow the chunks: a file without XMP must not declare it, and an EXIF
    /// chunk with the orientation alone — yes.
    /// </summary>
    [Fact]
    public void An_extended_webp_loses_xmp_and_exif_except_the_orientation_and_the_flags_follow()
    {
        var vp8x = new byte[10];
        vp8x[0] = 0x20 | 0x08 | 0x04;
        var webp = Webp(
            Chunk("VP8X", vp8x),
            Chunk("ICCP", Encoding.ASCII.GetBytes("profil")),
            Chunk("VP8L", [0x2F, 1, 2]),
            Chunk("EXIF", Tiff(3)),
            Chunk("XMP ", Encoding.ASCII.GetBytes(Secret)),
            Chunk("ABCD", Encoding.ASCII.GetBytes(Secret)));

        var stripped = SanitizedImage.From(webp).Content;

        Assert.False(Contains(stripped, Secret));
        Assert.Equal(0x20 | 0x08, stripped[20]);
        Assert.Equal(stripped.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(stripped.AsSpan(4)));
        var exif = IndexOf(stripped, "EXIF"u8);
        var length = BinaryPrimitives.ReadInt32LittleEndian(stripped.AsSpan(exif + 4));
        Assert.Equal(3, ExifOrientation.Read(stripped.AsSpan(exif + 8, length)));
        Assert.True(IndexOf(stripped, "ICCP"u8) > 0);
    }

    [Fact]
    public void An_extended_webp_with_the_normal_orientation_drops_the_exif_flag()
    {
        var vp8x = new byte[10];
        vp8x[0] = 0x08;

        var stripped = SanitizedImage.From(Webp(Chunk("VP8X", vp8x), Chunk("VP8L", [0x2F, 1]), Chunk("EXIF", Tiff(1)))).Content;

        Assert.Equal(0, stripped[20]);
        Assert.True(IndexOf(stripped, "EXIF"u8) < 0);
    }

    /// <summary>Whatever follows the size declared in the RIFF header is cut off.</summary>
    [Fact]
    public void Bytes_after_the_webp_container_are_removed()
    {
        var webp = Webp(Chunk("VP8L", [0x2F, 1, 2, 3, 4]));

        Assert.Equal(webp, SanitizedImage.From([.. webp, .. Encoding.ASCII.GetBytes(Secret)]).Content);
    }

    private static byte[] Segment(int marker, string payload) => Segment(marker, Encoding.Latin1.GetBytes(payload));

    private static byte[] Segment(int marker, byte[] payload) =>
        [0xFF, (byte)marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];

    private static byte[] Webp(params byte[][] chunks)
    {
        byte[] body = [.. chunks.SelectMany(chunk => chunk)];
        return [.. "RIFF"u8, .. Le(body.Length + 4), .. "WEBP"u8, .. body];
    }

    private static byte[] Chunk(string fourCc, byte[] data) =>
        [.. Encoding.ASCII.GetBytes(fourCc), .. Le(data.Length), .. data, .. (data.Length % 2 == 1 ? new byte[1] : [])];

    private static byte[] Le(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }
}
