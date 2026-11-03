using System.Buffers.Binary;

namespace Songly.Common.Media;

/// <summary>
/// Removes metadata from a JPEG by rewriting it segment by segment, without decoding the image.
/// </summary>
/// <remarks>
/// <para>
/// <b>An allow-list, not a deny-list.</b> What it takes to draw the image stays (tables, frame,
/// scans), plus three application segments that change how it looks: JFIF without a thumbnail, the
/// ICC colour profile, and the Adobe marker (without it a decoder misreads the colours of a CMYK
/// file). Everything else goes: EXIF with the GPS position, XMP, IPTC in APP13, comments, and the
/// thumbnail too — a known leak is a cropped photo whose EXIF thumbnail still showed the whole of it.
/// </para>
/// <para>
/// <b>Everything after EOI goes.</b> Phones append a second image there (a depth map in MPF) or the
/// video of a "motion photo", each with metadata of its own.
/// </para>
/// </remarks>
internal static class JpegMetadata
{
    private const int Soi = 0xD8;
    private const int Eoi = 0xD9;
    private const int Sos = 0xDA;
    private const int App0 = 0xE0;
    private const int App1 = 0xE1;
    private const int App2 = 0xE2;
    private const int App14 = 0xEE;
    private const int App15 = 0xEF;
    private const int Com = 0xFE;

    private static ReadOnlySpan<byte> Jfif => "JFIF\0"u8;

    private static ReadOnlySpan<byte> Exif => "Exif\0\0"u8;

    private static ReadOnlySpan<byte> IccProfile => "ICC_PROFILE\0"u8;

    private static ReadOnlySpan<byte> Adobe => "Adobe"u8;

    public static byte[] Strip(byte[] input)
    {
        if (input.Length < 4 || input[0] != 0xFF || input[1] != Soi)
        {
            throw new UnsupportedImageException();
        }

        using var output = new MemoryStream(input.Length);
        output.Write([0xFF, Soi]);
        var orientationHandled = false;
        var position = 2;
        while (true)
        {
            if (position >= input.Length || input[position] != 0xFF)
            {
                throw new UnsupportedImageException();
            }

            // Any number of 0xFF bytes before a marker is allowed padding.
            while (position < input.Length && input[position] == 0xFF)
            {
                position++;
            }

            if (position >= input.Length)
            {
                throw new UnsupportedImageException();
            }

            int marker = input[position++];
            if (marker == Eoi)
            {
                output.Write([0xFF, Eoi]);
                return output.ToArray();
            }

            // Markers without a length: TEM and the restart markers.
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                output.Write([0xFF, (byte)marker]);
                continue;
            }

            if (marker == 0x00 || marker == Soi || position + 2 > input.Length)
            {
                throw new UnsupportedImageException();
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(input.AsSpan(position));
            if (length < 2 || position + length > input.Length)
            {
                throw new UnsupportedImageException();
            }

            var payload = input.AsSpan(position + 2, length - 2);
            if (marker == App1 && payload.StartsWith(Exif))
            {
                if (!orientationHandled)
                {
                    orientationHandled = true;
                    var orientation = ExifOrientation.Read(payload);
                    if (orientation != ExifOrientation.Normal)
                    {
                        WriteSegment(output, App1, ExifOrientation.JpegPayload(orientation));
                    }
                }
            }
            else if (Keeps(marker, payload))
            {
                output.Write([0xFF, (byte)marker]);
                output.Write(input, position, length);
            }

            position += length;
            if (marker == Sos)
            {
                var scanEnd = ScanEnd(input, position);
                output.Write(input, position, scanEnd - position);
                position = scanEnd;
            }
        }
    }

    private static bool Keeps(int marker, ReadOnlySpan<byte> payload) => marker switch
    {
        // Bytes 12 and 13 of the JFIF payload are the thumbnail's size; zero means there is none.
        App0 => payload.StartsWith(Jfif) && payload.Length >= 14 && payload[12] == 0 && payload[13] == 0,
        App2 => payload.StartsWith(IccProfile),
        App14 => payload.StartsWith(Adobe),
        _ => marker is not (>= App0 and <= App15) && marker != Com,
    };

    /// <summary>
    /// The end of the scan data: the first marker that is neither the <c>FF 00</c> stuffing nor a
    /// restart marker. A progressive file has several scans with segments between them, so the main
    /// loop goes back to reading segments.
    /// </summary>
    private static int ScanEnd(byte[] input, int position)
    {
        while (position + 1 < input.Length)
        {
            if (input[position] == 0xFF)
            {
                int next = input[position + 1];
                if (next == 0x00 || next is >= 0xD0 and <= 0xD7)
                {
                    position += 2;
                    continue;
                }

                return position;
            }

            position++;
        }

        throw new UnsupportedImageException();
    }

    private static void WriteSegment(MemoryStream output, int marker, byte[] payload)
    {
        var length = payload.Length + 2;
        output.Write([0xFF, (byte)marker, (byte)(length >> 8), (byte)(length & 0xFF)]);
        output.Write(payload);
    }
}
