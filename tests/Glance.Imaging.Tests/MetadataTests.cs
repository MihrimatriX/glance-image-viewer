using System.Globalization;
using Glance.Imaging;
using ImageMagick;

namespace Glance.Imaging.Tests;

public class MetadataTests
{
    [Fact]
    public void Reads_camera_exposure_date_and_gps_from_exif()
    {
        using var img = new MagickImage(MagickColors.Gray, 8, 8);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "Canon");
        exif.SetValue(ExifTag.Model, "Canon EOS R5");
        exif.SetValue(ExifTag.LensModel, "RF24-70mm F2.8 L IS USM");
        exif.SetValue(ExifTag.FNumber, new Rational(18, 10));
        exif.SetValue(ExifTag.ExposureTime, new Rational(1, 125));
        exif.SetValue(ExifTag.ISOSpeedRatings, [(ushort)400]);
        exif.SetValue(ExifTag.FocalLength, new Rational(50, 1));
        exif.SetValue(ExifTag.DateTimeOriginal, "2026:07:14 18:30:05");
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(41, 1), new Rational(0, 1), new Rational(0, 1)]);
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, [new Rational(29, 1), new Rational(0, 1), new Rational(0, 1)]);
        img.SetProfile(exif);
        using var file = new MemoryStream(img.ToByteArray(MagickFormat.Jpeg));

        var meta = PhotoMetadata.Read(file, CultureInfo.GetCultureInfo("tr-TR"));

        Assert.Equal("Canon EOS R5", meta.Camera);   // make not repeated
        Assert.Equal("RF24-70mm F2.8 L IS USM", meta.Lens);
        Assert.Equal("f/1,8 · 1/125 s · ISO 400 · 50 mm", meta.Exposure);   // Turkish decimal comma
        Assert.Equal(new DateTime(2026, 7, 14, 18, 30, 5), meta.Taken);
        Assert.Equal((41.0, 29.0), meta.Location);
    }

    [Fact]
    public void Garbage_yields_empty_metadata_not_an_exception()
    {
        using var junk = new MemoryStream([1, 2, 3, 4, 5]);
        Assert.Equal(PhotoMetadata.Empty, PhotoMetadata.Read(junk, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Histogram_counts_channels_and_skips_transparent_pixels()
    {
        byte[] pixels = [10, 20, 30, 255, /* transparent: */ 99, 99, 99, 0, 10, 20, 200, 255];
        var h = Histogram.From(pixels);

        Assert.Equal(2, h.Blue[10]);
        Assert.Equal(2, h.Green[20]);
        Assert.Equal((1, 1), (h.Red[30], h.Red[200]));
        Assert.Equal(0, h.Blue[99]);
        Assert.Equal(2, h.Luma.Sum());
    }
}
