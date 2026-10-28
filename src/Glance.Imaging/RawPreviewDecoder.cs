using System.Buffers.Binary;

namespace Glance.Imaging;

/// <summary>
/// RAW fast path: the camera's own JPEG preview, found by scanning for the largest embedded baseline/progressive JPEG
/// (format-agnostic: CR2, CR3, NEF, ARW, DNG, RAF, ORF, RW2, …) and decoded by WIC. Declines when a full decode is
/// asked for, so the router falls through to <see cref="MagickDecoder"/> (LibRaw demosaic).
/// </summary>
public sealed class RawPreviewDecoder(WicDecoder wic) : IImageDecoder
{
    private const int MinPreviewBytes = 64 * 1024;   // skip the 160 px thumbnails

    public int Priority => 10;

    public bool CanDecode(string extension, ReadOnlySpan<byte> header) => ImageFormats.Raw.Contains(extension);

    public async Task<DecodedImage> DecodeAsync(Stream source, DecodeOptions options, CancellationToken ct)
    {
        if (!options.PreferEmbeddedPreview)
            throw new NotSupportedException("Full RAW decode requested.");

        var data = GC.AllocateUninitializedArray<byte>(checked((int)(source.Length - source.Position)));
        await source.ReadExactlyAsync(data, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var (start, length) = FindLargestJpeg(data);
        if (length < MinPreviewBytes)
            throw new NotSupportedException("No usable embedded preview.");

        var jpeg = GC.AllocateUninitializedArray<byte>(length, pinned: true);
        data.AsSpan(start, length).CopyTo(jpeg);
        // The preview carries no EXIF of its own; orientation lives in the RAW's TIFF header (when it has one).
        return wic.Decode(jpeg, options, TiffOrientation(data));
    }

    /// <summary>Largest lossy JPEG stream (SOF0/1/2) in the file; lossless SOF3 streams are raw sensor data, not previews.</summary>
    public static (int Start, int Length) FindLargestJpeg(ReadOnlySpan<byte> d)
    {
        (int Start, int Length) best = default;
        for (var i = 0; i + 4 < d.Length; i++)
        {
            if (d[i] != 0xFF || d[i + 1] != 0xD8 || d[i + 2] != 0xFF)
                continue;
            var end = JpegEnd(d, i, out var lossy);
            if (end > 0 && lossy && end - i > best.Length)
                best = (i, end - i);
            if (end > 0)
                i = end - 1;   // don't rescan inside a stream we've measured
        }
        return best;
    }

    // Walks marker segments to SOS, then scans entropy-coded data for EOI. Returns the end offset (exclusive) or -1.
    private static int JpegEnd(ReadOnlySpan<byte> d, int start, out bool lossy)
    {
        lossy = false;
        var p = start + 2;
        while (p + 4 <= d.Length)
        {
            if (d[p] != 0xFF)
                return -1;
            var marker = d[p + 1];
            if (marker == 0xFF) { p++; continue; }                    // fill byte
            if (marker is 0xC0 or 0xC1 or 0xC2)
                lossy = true;
            if (marker == 0xD9)
                return p + 2;
            var len = BinaryPrimitives.ReadUInt16BigEndian(d[(p + 2)..]);
            if (len < 2)
                return -1;
            p += 2 + len;
            if (marker != 0xDA)
                continue;
            // Entropy data: FF00 is stuffing, FFD0–FFD7 restarts; any other marker ends the scan.
            while (p + 1 < d.Length && !(d[p] == 0xFF && d[p + 1] != 0x00 && d[p + 1] is not (>= 0xD0 and <= 0xD7)))
                p++;
        }
        return -1;
    }

    /// <summary>IFD0 Orientation (tag 0x0112) of a TIFF-based RAW, or 1.</summary>
    public static ushort TiffOrientation(ReadOnlySpan<byte> d)
    {
        if (d.Length < 8)
            return 1;
        var little = d[0] == 'I' && d[1] == 'I';
        if (!little && !(d[0] == 'M' && d[1] == 'M'))
            return 1;

        var ifd = (int)U32(d, 4, little);
        if (ifd <= 0 || ifd + 2 > d.Length)
            return 1;
        int count = U16(d, ifd, little);
        for (var e = 0; e < count; e++)
        {
            var entry = ifd + 2 + e * 12;
            if (entry + 12 > d.Length)
                break;
            if (U16(d, entry, little) == 0x0112)
                return U16(d, entry + 8, little) is var o and >= 1 and <= 8 ? o : (ushort)1;
        }
        return 1;
    }

    private static ushort U16(ReadOnlySpan<byte> d, int o, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(d[o..]) : BinaryPrimitives.ReadUInt16BigEndian(d[o..]);

    private static uint U32(ReadOnlySpan<byte> d, int o, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(d[o..]) : BinaryPrimitives.ReadUInt32BigEndian(d[o..]);
}
