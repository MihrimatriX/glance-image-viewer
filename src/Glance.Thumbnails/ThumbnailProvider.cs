using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Glance.Imaging;

namespace Glance.Thumbnails;

/// <summary>
/// IThumbnailProvider for formats Windows can't thumbnail without Store codec extensions. Explorer hands us a stream;
/// we read it fully in Initialize (so no stream lifetime to manage) and decode on GetThumbnail.
/// </summary>
[GeneratedComClass]
internal sealed partial class ThumbnailProvider : IInitializeWithStream, IThumbnailProvider
{
    private const int WTSAT_ARGB = 2;
    private byte[]? _data;
    private string _extension = "";

    public unsafe int Initialize(nint stream, uint grfMode)
    {
        try
        {
            (_data, _extension) = ComStream.ReadAll(stream);
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public int GetThumbnail(uint cx, out nint bitmap, out int alpha)
    {
        bitmap = 0;
        alpha = WTSAT_ARGB;
        if (_data is null)
            return HResult.E_UNEXPECTED;
        try
        {
            var image = Decode(_data, _extension, (int)cx);
            bitmap = Dib.Create(image.Pixels, image.Width, image.Height);
            return bitmap == 0 ? HResult.E_FAIL : 0;
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    private static DecodedImage Decode(byte[] data, string extension, int size)
    {
        Native.EnsureMagickResolvable();
        MagickSetup.Limits = MagickLimits.Thumbnail;   // takes effect on the first decode in this host, ignored after
        var options = new DecodeOptions(size, size) { Extension = extension };
        if (ImageFormats.Raw.Contains(extension))
        {
            // Camera's own JPEG preview: milliseconds instead of a multi-second demosaic.
            var (start, length) = RawPreviewDecoder.FindLargestJpeg(data);
            if (length > 0)
            {
                using var preview = new MemoryStream(data, start, length, writable: false);
                return Run(preview, options with { Extension = ".jpg" } , RawPreviewDecoder.TiffOrientation(data));
            }
        }
        using var stream = new MemoryStream(data, writable: false);
        return Run(stream, options, null);

        static DecodedImage Run(Stream s, DecodeOptions o, ushort? orientation) =>
            new MagickDecoder().DecodeAsync(s, o with { Orientation = orientation }, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static int Fail(Exception ex)
    {
        // The surrogate must never crash Explorer's thumbnailing; Explorer falls back to the icon on failure.
        System.Diagnostics.Trace.WriteLine($"Glance thumbnail failed: {ex}");
        return ex.HResult < 0 ? ex.HResult : HResult.E_FAIL;
    }
}

[GeneratedComInterface]
[Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
internal partial interface IInitializeWithStream
{
    [PreserveSig]
    int Initialize(nint stream, uint grfMode);
}

[GeneratedComInterface]
[Guid("e357fccd-a995-4576-b01f-234630154e96")]
internal partial interface IThumbnailProvider
{
    [PreserveSig]
    int GetThumbnail(uint cx, out nint bitmap, out int alpha);
}

internal static class HResult
{
    public const int E_FAIL = unchecked((int)0x80004005);
    public const int E_UNEXPECTED = unchecked((int)0x8000FFFF);
    public const int E_NOINTERFACE = unchecked((int)0x80004002);
    public const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);
    public const int CLASS_E_CLASSNOTAVAILABLE = unchecked((int)0x80040111);
}
