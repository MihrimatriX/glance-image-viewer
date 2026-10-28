using System.Buffers.Binary;

namespace Glance.Imaging;

/// <summary>
/// EPS without Ghostscript (ADR 0009): the TIFF preview that DOS-format EPS files (Photoshop, Illustrator and most Windows
/// tools write them) carry next to their PostScript, decoded by WIC. Plain-text EPS has no such preview; no decoder claims it.
/// </summary>
public sealed class EpsPreviewDecoder(WicDecoder wic) : IImageDecoder
{
    /// <summary>Magic, then PostScript / WMF / TIFF offset and length pairs (little-endian uint32), then a uint16 checksum.</summary>
    private const int HeaderLength = 30;

    public int Priority => 5;

    private static ReadOnlySpan<byte> Magic => [0xC5, 0xD0, 0xD3, 0xC6];

    public bool CanDecode(string extension, ReadOnlySpan<byte> header) => header.StartsWith(Magic);

    public async Task<DecodedImage> DecodeAsync(Stream source, DecodeOptions options, CancellationToken ct)
    {
        var header = new byte[HeaderLength];
        await source.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        var (offset, length) = TiffPreview(header, source.Length);
        if (length == 0)
            throw new NotSupportedException("EPS without a TIFF preview: only Ghostscript could render it.");

        var tiff = GC.AllocateUninitializedArray<byte>(length, pinned: true);
        source.Position = offset;
        await source.ReadExactlyAsync(tiff, ct).ConfigureAwait(false);
        return wic.Decode(tiff, options);
    }

    /// <summary>
    /// Where the TIFF preview sits in a DOS EPS file of <paramref name="fileLength"/> bytes, read from its header;
    /// (0, 0) when there is none, or when the header can't be trusted.
    /// </summary>
    public static (int Offset, int Length) TiffPreview(ReadOnlySpan<byte> header, long fileLength)
    {
        if (header.Length < HeaderLength || !header.StartsWith(Magic))
            return default;
        long offset = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]), length = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
        // Two uint32s can't overflow a long. The preview must start past the header and end inside the file — and inside
        // what an int offset and one array can hold.
        return length > 0 && offset >= HeaderLength && offset + length <= Math.Min(fileLength, int.MaxValue)
            ? ((int)offset, (int)length)
            : default;
    }
}
