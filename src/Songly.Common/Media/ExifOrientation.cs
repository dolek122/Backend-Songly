using System.Buffers.Binary;

namespace Songly.Common.Media;

/// <summary>
/// The only EXIF tag that survives the removal of metadata (original ADR 0047): the orientation. A
/// phone stores the picture the way the sensor lay and adds "rotate by 90°" — without the tag a
/// portrait would be shown on its side.
/// </summary>
/// <remarks>
/// Only IFD0 and only this one tag are read. An EXIF block that cannot be read simply means "no
/// rotation": the metadata goes anyway, so a damaged block is no reason to reject the photo.
/// </remarks>
internal static class ExifOrientation
{
    public const int Normal = 1;

    private const int OrientationTag = 0x0112;
    private const int TypeShort = 3;

    private static ReadOnlySpan<byte> JpegExifHeader => "Exif\0\0"u8;

    /// <summary>
    /// The orientation stored in a TIFF structure — the content of an EXIF block. The JPEG
    /// <c>Exif\0\0</c> prefix is skipped, because some programs write it into PNG and WebP too, where
    /// it does not belong.
    /// </summary>
    public static int Read(ReadOnlySpan<byte> block)
    {
        if (block.StartsWith(JpegExifHeader))
        {
            block = block[JpegExifHeader.Length..];
        }

        if (block.Length < 8)
        {
            return Normal;
        }

        bool littleEndian;
        if (block[0] == 'I' && block[1] == 'I')
        {
            littleEndian = true;
        }
        else if (block[0] == 'M' && block[1] == 'M')
        {
            littleEndian = false;
        }
        else
        {
            return Normal;
        }

        var tiff = new Tiff(block, littleEndian);
        if (tiff.U16(2) != 42)
        {
            return Normal;
        }

        var ifd = tiff.U32(4);
        if (ifd < 8 || ifd + 2 > block.Length)
        {
            return Normal;
        }

        int entries = tiff.U16((int)ifd);
        for (var i = 0; i < entries; i++)
        {
            var entry = (int)ifd + 2 + (12 * i);
            if (entry + 12 > block.Length)
            {
                return Normal;
            }

            if (tiff.U16(entry) == OrientationTag)
            {
                int value = tiff.U16(entry + 8);
                return tiff.U16(entry + 2) == TypeShort && value is >= 1 and <= 8 ? value : Normal;
            }
        }

        return Normal;
    }

    /// <summary>
    /// The smallest valid TIFF with one entry — the orientation. The original entry is not copied: a
    /// new one is written, so that not one byte of someone else's block gets through except the
    /// number itself.
    /// </summary>
    public static byte[] MinimalTiff(int orientation)
    {
        var tiff = new byte[26];
        tiff[0] = (byte)'M';
        tiff[1] = (byte)'M';
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(10), OrientationTag);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(12), TypeShort);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(14), 1);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(18), (ushort)orientation);

        // Bytes 20–25: the value's padding and "no next IFD" — zero.
        return tiff;
    }

    /// <summary>A JPEG APP1 payload with the orientation alone, behind the <c>Exif\0\0</c> prefix.</summary>
    public static byte[] JpegPayload(int orientation) => [.. JpegExifHeader, .. MinimalTiff(orientation)];

    private readonly ref struct Tiff(ReadOnlySpan<byte> data, bool littleEndian)
    {
        private readonly ReadOnlySpan<byte> _data = data;

        public ushort U16(int at) => littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(_data[at..])
            : BinaryPrimitives.ReadUInt16BigEndian(_data[at..]);

        public uint U32(int at) => littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(_data[at..])
            : BinaryPrimitives.ReadUInt32BigEndian(_data[at..]);
    }
}
