using System.Buffers.Binary;
using System.Text;
using Glance.Imaging;
using ImageMagick;

namespace Glance.Imaging.Tests;

/// <summary>PDF / Illustrator through Windows' PDF renderer, EPS through its embedded TIFF preview (ADR 0009).</summary>
public class DocumentTests
{
    private static readonly WicDecoder Wic = new();
    private static readonly DecoderRouter Router = new([Wic, new PdfDecoder(Wic), new EpsPreviewDecoder(Wic), new MagickDecoder()]);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pdf_first_page_renders_at_300_dpi_on_white()
    {
        using var file = new MemoryStream(Pdf(72, 36));   // 1 × 0.5 inch

        var image = await Router.DecodeAsync(file, ".pdf", new DecodeOptions(), Ct);

        Assert.Equal((300, 150), (image.Width, image.Height));
        Assert.Equal((300, 150), (image.OriginalWidth, image.OriginalHeight));
        Assert.Equal([0, 0, 255, 255], Pixel(image, 150, 75));     // red square in the middle (BGRA)
        Assert.Equal([255, 255, 255, 255], Pixel(image, 5, 5));   // paper
    }

    [Fact]
    public async Task Pdf_renders_at_screen_size_but_reports_its_full_size()
    {
        using var file = new MemoryStream(Pdf(72, 36));

        var image = await Router.DecodeAsync(file, ".pdf", new DecodeOptions(100, 100), Ct);

        Assert.Equal((100, 50), (image.Width, image.Height));
        Assert.Equal((300, 150), (image.OriginalWidth, image.OriginalHeight));   // zooming in re-renders sharp
    }

    [Fact]
    public async Task Pdf_full_size_is_capped_at_8192_pixels()
    {
        using var file = new MemoryStream(Pdf(72 * 40, 72 * 20));   // 40 inches: 12 000 px at 300 dpi

        var image = await Router.DecodeAsync(file, ".pdf", new DecodeOptions(200, 200), Ct);

        Assert.Equal((8192, 4096), (image.OriginalWidth, image.OriginalHeight));
    }

    [Fact]
    public async Task Illustrator_file_saved_pdf_compatible_opens()
    {
        using var file = new MemoryStream(Pdf(72, 36));

        var image = await Router.DecodeAsync(file, ".ai", new DecodeOptions(), Ct);

        Assert.Equal((300, 150), (image.Width, image.Height));
    }

    [Fact]
    public async Task Eps_shows_its_embedded_tiff_preview()
    {
        using var file = new MemoryStream(DosEps(Tiff(40, 30)));

        var image = await Router.DecodeAsync(file, ".eps", new DecodeOptions(), Ct);

        Assert.Equal((40, 30), (image.Width, image.Height));
        Assert.InRange(image.Pixels[2], 190, 210);   // R
        Assert.InRange(image.Pixels[0], 10, 30);     // B
    }

    [Fact]
    public async Task Eps_without_a_preview_cannot_be_opened()
    {
        using var plain = new MemoryStream("%!PS-Adobe-3.0 EPSF-3.0\n%%BoundingBox: 0 0 10 10\n"u8.ToArray());
        await Assert.ThrowsAsync<NotSupportedException>(() => Router.DecodeAsync(plain, ".eps", new DecodeOptions(), Ct));

        using var noTiff = new MemoryStream(DosEps([]));   // DOS EPS whose preview slot is empty
        await Assert.ThrowsAsync<InvalidDataException>(() => Router.DecodeAsync(noTiff, ".eps", new DecodeOptions(), Ct));
    }

    [Fact]
    public void Tiff_preview_must_lie_inside_the_file()
    {
        var eps = DosEps(new byte[100]);
        Assert.Equal((Offset: 30 + Ps.Length, Length: 100), EpsPreviewDecoder.TiffPreview(eps, eps.Length));

        Assert.Equal((0, 0), EpsPreviewDecoder.TiffPreview(eps, eps.Length - 1));      // truncated file
        Assert.Equal((0, 0), EpsPreviewDecoder.TiffPreview(eps.AsSpan(0, 20), eps.Length));   // header cut short
        BinaryPrimitives.WriteUInt32LittleEndian(eps.AsSpan(20), uint.MaxValue);           // offset + length overflows
        Assert.Equal((0, 0), EpsPreviewDecoder.TiffPreview(eps, eps.Length));
        Assert.Equal((0, 0), EpsPreviewDecoder.TiffPreview("%!PS-Adobe-3.0 EPSF-3.0 padding...."u8, 1000));   // not DOS EPS
    }

    [Fact]
    public void Documents_are_listed_but_never_reach_magick()
    {
        foreach (var name in new[] { "a.pdf", "a.ai", "a.eps" })
        {
            Assert.True(ImageFormats.IsSupported(name), name);
            Assert.False(ImageFormats.IsMagickRouted(name), name);   // Ghostscript stays out (ADR 0007)
        }
    }

    // ---------- Sample files ----------

    private static byte[] Pixel(DecodedImage image, int x, int y) => image.Pixels.AsSpan((y * image.Width + x) * 4, 4).ToArray();

    /// <summary>One page of <paramref name="w"/>×<paramref name="h"/> points, white, with a red square over its middle half.</summary>
    private static byte[] Pdf(int w, int h)
    {
        var content = $"1 0 0 rg {w / 4} {h / 4} {w / 2} {h / 2} re f";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} {h}] /Contents 4 0 R >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static readonly byte[] Ps = "%!PS-Adobe-3.0 EPSF-3.0\n%%BoundingBox: 0 0 40 30\n"u8.ToArray();

    private static byte[] Tiff(int w, int h)
    {
        using var img = new MagickImage(MagickColor.FromRgb(200, 60, 20), (uint)w, (uint)h);
        return img.ToByteArray(MagickFormat.Tiff);
    }

    /// <summary>
    /// DOS EPS binary: a 30-byte header (C5 D0 D3 C6, then little-endian PostScript / WMF / TIFF offset and length pairs and
    /// a checksum), the PostScript, then the TIFF preview. An empty <paramref name="tiff"/> means "no TIFF preview".
    /// </summary>
    private static byte[] DosEps(byte[] tiff)
    {
        var eps = new byte[30 + Ps.Length + tiff.Length];
        eps[0] = 0xC5; eps[1] = 0xD0; eps[2] = 0xD3; eps[3] = 0xC6;
        BinaryPrimitives.WriteUInt32LittleEndian(eps.AsSpan(4), 30);
        BinaryPrimitives.WriteUInt32LittleEndian(eps.AsSpan(8), (uint)Ps.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(eps.AsSpan(20), tiff.Length == 0 ? 0u : (uint)(30 + Ps.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(eps.AsSpan(24), (uint)tiff.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(eps.AsSpan(28), 0xFFFF);   // "no checksum"
        Ps.CopyTo(eps, 30);
        tiff.CopyTo(eps, 30 + Ps.Length);
        return eps;
    }
}
