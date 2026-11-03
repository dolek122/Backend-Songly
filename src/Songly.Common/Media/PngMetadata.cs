using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace Songly.Common.Media;

/// <summary>
/// Removes metadata from a PNG by rewriting it chunk by chunk, without decoding the image.
/// </summary>
/// <remarks>
/// An allow-list: image, transparency, colour and APNG animation chunks. Gone are <c>eXIf</c>
/// (except the orientation, see <see cref="ExifOrientation"/>), the text chunks
/// <c>tEXt</c>/<c>zTXt</c>/<c>iTXt</c> — where programs store XMP, or even a whole EXIF as text —
/// <c>tIME</c>, and every chunk we do not know.
/// </remarks>
internal static class PngMetadata
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly HashSet<string> Kept = new(StringComparer.Ordinal)
    {
        "IHDR", "PLTE", "IDAT", "IEND", "tRNS", "cHRM", "gAMA", "iCCP", "sBIT", "sRGB", "cICP", "mDCV", "cLLI",
        "bKGD", "hIST", "pHYs", "sPLT", "acTL", "fcTL", "fdAT",
    };

    public static byte[] Strip(byte[] input)
    {
        if (!input.AsSpan().StartsWith(Signature))
        {
            throw new UnsupportedImageException();
        }

        using var output = new MemoryStream(input.Length);
        output.Write(Signature);
        var orientationHandled = false;
        var position = Signature.Length;
        while (true)
        {
            if (position + 12 > input.Length)
            {
                throw new UnsupportedImageException();
            }

            long length = BinaryPrimitives.ReadUInt32BigEndian(input.AsSpan(position));
            if (length > input.Length - position - 12)
            {
                throw new UnsupportedImageException();
            }

            var type = Encoding.ASCII.GetString(input, position + 4, 4);
            if (position == Signature.Length && type != "IHDR")
            {
                throw new UnsupportedImageException();
            }

            var total = 12 + (int)length;
            if (type == "eXIf")
            {
                if (!orientationHandled)
                {
                    orientationHandled = true;
                    var orientation = ExifOrientation.Read(input.AsSpan(position + 8, (int)length));
                    if (orientation != ExifOrientation.Normal)
                    {
                        WriteChunk(output, "eXIf", ExifOrientation.MinimalTiff(orientation));
                    }
                }
            }
            else if (Kept.Contains(type))
            {
                output.Write(input, position, total);
            }

            position += total;
            if (type == "IEND")
            {
                return output.ToArray();
            }
        }
    }

    /// <summary>A chunk with its CRC, which covers the type and the data.</summary>
    private static void WriteChunk(MemoryStream output, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var crc = new Crc32();
        crc.Append(typeBytes);
        crc.Append(data);

        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number);
        output.Write(typeBytes);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, crc.GetCurrentHashAsUInt32());
        output.Write(number);
    }
}
