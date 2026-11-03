using System.Buffers.Binary;
using System.Text;

namespace Songly.Common.Media;

/// <summary>
/// Removes metadata from a WebP by rewriting its RIFF container chunk by chunk, without decoding the
/// image.
/// </summary>
/// <remarks>
/// An allow-list: the image (<c>VP8 </c>, <c>VP8L</c>, <c>ALPH</c>), animation, the colour profile
/// and the <c>VP8X</c> header. Gone are <c>EXIF</c> (except the orientation), <c>XMP </c> and unknown
/// chunks. The <c>VP8X</c> header declares with flags which chunks the file has, so once they are
/// removed the flags must be corrected — otherwise the file would promise metadata it does not have.
/// </remarks>
internal static class WebpMetadata
{
    private const int FlagXmp = 0x04;
    private const int FlagExif = 0x08;

    private static readonly HashSet<string> Kept = new(StringComparer.Ordinal) { "VP8 ", "VP8L", "VP8X", "ALPH", "ANIM", "ANMF", "ICCP" };

    public static byte[] Strip(byte[] input)
    {
        if (input.Length < 12 || !input.AsSpan().StartsWith("RIFF"u8) || !input.AsSpan(8).StartsWith("WEBP"u8))
        {
            throw new UnsupportedImageException();
        }

        long riffSize = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(4));
        if (riffSize < 4 || riffSize > input.Length - 8)
        {
            throw new UnsupportedImageException();
        }

        // The size in the header marks the end of the file — whatever was appended after it goes.
        var end = 8 + (int)riffSize;
        using var chunks = new MemoryStream(input.Length);
        var vp8xFlagsAt = -1;
        var orientationHandled = false;
        var orientationWritten = false;
        var position = 12;
        while (position < end)
        {
            if (position + 8 > end)
            {
                throw new UnsupportedImageException();
            }

            var fourCc = Encoding.ASCII.GetString(input, position, 4);
            long size = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(position + 4));
            if (size > end - position - 8)
            {
                throw new UnsupportedImageException();
            }

            var payload = position + 8;
            if (fourCc == "VP8X")
            {
                if (position != 12 || size < 10)
                {
                    throw new UnsupportedImageException();
                }

                vp8xFlagsAt = (int)chunks.Length + 8;
            }

            if (fourCc == "EXIF")
            {
                if (!orientationHandled && vp8xFlagsAt >= 0)
                {
                    orientationHandled = true;
                    var orientation = ExifOrientation.Read(input.AsSpan(payload, (int)size));
                    if (orientation != ExifOrientation.Normal)
                    {
                        WriteChunk(chunks, "EXIF", ExifOrientation.MinimalTiff(orientation));
                        orientationWritten = true;
                    }
                }
            }
            else if (Kept.Contains(fourCc))
            {
                WriteChunk(chunks, fourCc, input.AsSpan(payload, (int)size));
            }

            // A chunk of odd size has a padding byte; the last one sometimes lacks it.
            position = (int)Math.Min(end, payload + size + (size & 1));
        }

        var body = chunks.ToArray();
        if (vp8xFlagsAt >= 0)
        {
            var flags = body[vp8xFlagsAt] & ~FlagXmp & ~FlagExif;
            body[vp8xFlagsAt] = (byte)(orientationWritten ? flags | FlagExif : flags);
        }

        var output = new byte[body.Length + 12];
        "RIFF"u8.CopyTo(output);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4), (uint)(body.Length + 4));
        "WEBP"u8.CopyTo(output.AsSpan(8));
        body.CopyTo(output.AsSpan(12));
        return output;
    }

    private static void WriteChunk(MemoryStream output, string fourCc, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        Encoding.ASCII.GetBytes(fourCc, header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)data.Length);
        output.Write(header);
        output.Write(data);
        if ((data.Length & 1) == 1)
        {
            output.WriteByte(0);
        }
    }
}
