using Glance.Imaging;
using ImageMagick;

namespace Glance.Imaging.Tests;

public class OrientationTests
{
    private static readonly DecoderRouter Router = new([new WicDecoder(), new MagickDecoder()]);

    public static TheoryData<MagickFormat, int> Cases()
    {
        var data = new TheoryData<MagickFormat, int>();
        foreach (var format in new[] { MagickFormat.Tiff, MagickFormat.WebP })   // WIC path, Magick path
            for (var o = 1; o <= 8; o++)
                data.Add(format, o);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task All_eight_exif_orientations_match_reference(MagickFormat format, int orientation)
    {
        // 3x2 image, every pixel a different colour, lossless.
        using var source = new MagickImage(MagickColors.Black, 3, 2);
        using (var px = source.GetPixels())
            for (var y = 0; y < 2; y++)
                for (var x = 0; x < 3; x++)
                    px.SetPixel(x, y, [(ushort)(x * 80 * 257), (ushort)(y * 200 * 257), (ushort)((x + y) * 60 * 257)]);
        source.Depth = 8;
        source.Orientation = (OrientationType)orientation;
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, (ushort)orientation);
        source.SetProfile(exif);
        source.Settings.SetDefine(MagickFormat.WebP, "lossless", true);

        using var reference = (MagickImage)source.Clone();
        reference.AutoOrient();
        using var expected = reference.GetPixels();

        using var file = new MemoryStream(source.ToByteArray(format));
        var image = await Router.DecodeAsync(file, "." + format.ToString().ToLowerInvariant(), new DecodeOptions(), TestContext.Current.CancellationToken);

        Assert.Equal(((int)reference.Width, (int)reference.Height), (image.Width, image.Height));
        Assert.Equal(expected.ToByteArray(PixelMapping.BGRA), image.Pixels);
    }
}
