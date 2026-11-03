using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;

namespace Songly.Tests.Shared;

/// <summary>
/// Real, minimal images for upload tests — the backend reads the structure of a file (original
/// ADR 0047), so magic bytes alone no longer do — and the metadata a camera adds to them. Ported
/// from the original <c>TestImages</c>; shared by the unit and the integration tests.
/// </summary>
public static class TestImages
{
    /// <summary>Text that must not be in a file after upload; it stands where the GPS position would.</summary>
    public const string Secret = "GPS 52.2297N 21.0122E";

    /// <summary>
    /// A 2×2 baseline JPEG with nothing but what it takes to draw it (JFIF without a thumbnail,
    /// quantisation and Huffman tables, frame, scan). Encoded once with Pillow
    /// (<c>Image.new("RGB", (2, 2)).save(..., "JPEG", quality=90)</c>); .NET has no image encoder.
    /// </summary>
    public static byte[] Jpeg() => Convert.FromBase64String(BaselineJpeg);

    /// <summary>The same picture as a progressive JPEG: ten scans with tables between them.</summary>
    public static byte[] ProgressiveJpeg() => Convert.FromBase64String(ProgressiveJpegBase64);

    /// <summary>A 2×2 RGB PNG with nothing but IHDR, IDAT and IEND, built byte by byte.</summary>
    public static byte[] Png()
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 2);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 2);
        header[8] = 8; // bit depth
        header[9] = 2; // colour type: RGB

        // Two rows, each a filter byte (none) and two RGB pixels; the first pixel red.
        byte[] rows = [0, 255, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(rows);
        }

        return [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, .. PngChunk("IHDR", header), .. PngChunk("IDAT", compressed.ToArray()), .. PngChunk("IEND", [])];
    }

    /// <summary>A JPEG with an EXIF block right after SOI, where a camera writes it.</summary>
    public static byte[] JpegWithExif(int orientation)
    {
        var jpeg = Jpeg();
        byte[] payload = [.. "Exif\0\0"u8, .. Tiff(orientation)];
        var length = payload.Length + 2;
        return [.. jpeg[..2], 0xFF, 0xE1, (byte)(length >> 8), (byte)(length & 0xFF), .. payload, .. jpeg[2..]];
    }

    /// <summary>Inserts a chunk right after IHDR.</summary>
    public static byte[] PngWithChunk(string type, byte[] data)
    {
        var png = Png();
        const int afterIhdr = 8 + 12 + 13;
        return [.. png[..afterIhdr], .. PngChunk(type, data), .. png[afterIhdr..]];
    }

    public static byte[] PngChunk(string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var crc = new Crc32();
        crc.Append(typeBytes);
        crc.Append(data);
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        typeBytes.CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), crc.GetCurrentHashAsUInt32());
        return chunk;
    }

    /// <summary>
    /// A TIFF in Intel byte order (as most phones write it): the orientation and an image
    /// description holding <see cref="Secret"/> — a description instead of a real GPS block, because
    /// the test checks that no byte of the block gets through except the orientation.
    /// </summary>
    public static byte[] Tiff(int orientation)
    {
        var secret = Encoding.ASCII.GetBytes(Secret + "\0");
        const int entries = 2;
        const int stringOffset = 8 + 2 + (12 * entries) + 4;
        var tiff = new byte[stringOffset + secret.Length];
        var span = tiff.AsSpan();
        span[0] = (byte)'I';
        span[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 8);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], entries);

        // ImageDescription (ASCII) pointing at the secret.
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], 0x010E);
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[14..], (uint)secret.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[18..], stringOffset);

        // Orientation (SHORT).
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..], 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(span[24..], 3);
        BinaryPrimitives.WriteUInt32LittleEndian(span[26..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[30..], (ushort)orientation);

        // Bytes 34–37: no next IFD.
        secret.CopyTo(span[stringOffset..]);
        return tiff;
    }

    public static bool Contains(byte[] haystack, string needle) => IndexOf(haystack, Encoding.ASCII.GetBytes(needle)) >= 0;

    public static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle) => haystack.AsSpan().IndexOf(needle);

    private const string BaselineJpeg =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAACAAIDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD8rZ55LmaSaaRpZZGLvI7EszE5JJPUmiiis6fwL0OvF/7xU/xP8z//2Q==";

    private const string ProgressiveJpegBase64 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wgARCAACAAIDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAVAQEBAAAAAAAAAAAAAAAAAAACA//aAAwDAQACEAMQAAABlYGv/8QAFRABAQAAAAAAAAAAAAAAAAAAAQL/2gAIAQEAAQUCVp//xAAYEQACAwAAAAAAAAAAAAAAAAAAAgMzcf/aAAgBAwEBPwGWxtP/xAAWEQADAAAAAAAAAAAAAAAAAAAAAjH/2gAIAQIBAT8BWH//xAAXEAEAAwAAAAAAAAAAAAAAAAABABFB/9oACAEBAAY/AlW12f/EABcQAQADAAAAAAAAAAAAAAAAAAEAESH/2gAIAQEAAT8hcsi1OrP/2gAMAwEAAgADAAAAEAf/xAAWEQADAAAAAAAAAAAAAAAAAAAAobH/2gAIAQMBAT8QcU//xAAWEQADAAAAAAAAAAAAAAAAAAAAMXH/2gAIAQIBAT8QTD//xAAXEAEAAwAAAAAAAAAAAAAAAAABABEx/9oACAEBAAE/EHQMdKG1V1Z//9k=";
}
