using Glance.Imaging;
using ImageMagick;

namespace Glance.Imaging.Tests;

public sealed class EditingTests : IDisposable
{
    private static readonly DecoderRouter Router = new([new WicDecoder(), new MagickDecoder()]);
    private readonly string _dir = Directory.CreateTempSubdirectory("glance-edit-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(1, 1, 6)]
    [InlineData(6, 1, 3)]
    [InlineData(3, 2, 1)]
    [InlineData(8, 1, 1)]
    [InlineData(1, -1, 8)]
    [InlineData(2, 1, 7)]   // mirrored + rotate: stays mirrored
    [InlineData(5, 1, 2)]   // transpose, then 90° clockwise = horizontal mirror
    public void Orientation_composition(ushort from, int turns, ushort expected) =>
        Assert.Equal(expected, Orientation.Rotate(from, turns));

    [Fact]
    public void Four_quarter_turns_are_identity_for_every_orientation()
    {
        for (ushort o = 1; o <= 8; o++)
            Assert.Equal(o, Orientation.Rotate(o, 4));
    }

    [Fact]
    public async Task Jpeg_rotation_in_place_is_lossless_and_shows_rotated()
    {
        var path = Path.Combine(_dir, "photo.jpg");
        using (var img = new MagickImage(MagickColors.Red, 60, 40))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)1);
            img.SetProfile(exif);
            img.Write(path);
        }
        var before = File.ReadAllBytes(path);

        Assert.True(Orientation.TryRotateJpegInPlace(path, 1));

        var after = File.ReadAllBytes(path);
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(1, before.Zip(after).Count(p => p.First != p.Second));   // exactly one byte: the orientation value
        await using var file = File.OpenRead(path);
        var image = await Router.DecodeAsync(file, ".jpg", new DecodeOptions(), TestContext.Current.CancellationToken);
        Assert.Equal((40, 60), (image.Width, image.Height));
    }

    [Fact]
    public void Jpeg_without_exif_is_not_touched()
    {
        var path = Path.Combine(_dir, "plain.jpg");
        using (var img = new MagickImage(MagickColors.Blue, 8, 8))
        {
            img.Strip();
            img.Write(path);
        }
        var before = File.ReadAllBytes(path);
        Assert.False(Orientation.TryRotateJpegInPlace(path, 1));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Save_applies_rotation_then_crop_then_resize_in_view_coordinates()
    {
        // 60x40, left half red, right half blue. Rotated 90° clockwise it becomes 40x60: red on top.
        var source = Path.Combine(_dir, "source.png");
        using (var img = new MagickImage(MagickColors.Blue, 60, 40))
        {
            img.Draw(new ImageMagick.Drawing.Drawables().FillColor(MagickColors.Red).Rectangle(0, 0, 29, 39));
            img.Write(source);
        }
        var dest = Path.Combine(_dir, "out.webp");

        ImageEditor.Save(source, new EditRequest(1, new PixelRect(0, 0, 40, 30), 20, 15, 95), SaveFormat.ForExtension(".webp")!, dest);

        await using var file = File.OpenRead(dest);
        var image = await Router.DecodeAsync(file, ".webp", new DecodeOptions(), TestContext.Current.CancellationToken);
        Assert.Equal((20, 15), (image.Width, image.Height));
        Assert.True(image.Pixels[2] > 200 && image.Pixels[0] < 60, "top crop of the rotated image must be the red half");
    }

    [Fact]
    public void Heic_is_never_a_save_format() =>
        Assert.DoesNotContain(SaveFormat.All, f => f.Extension is ".heic" or ".heif");
}
