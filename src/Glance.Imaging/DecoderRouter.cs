namespace Glance.Imaging;

/// <summary>Picks decoders by magic bytes (extension second) and falls back to the next one on failure.</summary>
public sealed class DecoderRouter(IEnumerable<IImageDecoder> decoders)
{
    private const int HeaderLength = 64;
    private readonly IImageDecoder[] _decoders = [.. decoders.OrderBy(d => d.Priority)];

    /// <exception cref="NotSupportedException">No decoder recognizes the file.</exception>
    /// <exception cref="InvalidDataException">Every matching decoder failed (corrupt/truncated file); inner exception holds all failures.</exception>
    public async Task<DecodedImage> DecodeAsync(Stream source, string extension, DecodeOptions options, CancellationToken ct)
    {
        var header = new byte[HeaderLength];
        var read = await source.ReadAtLeastAsync(header, HeaderLength, throwOnEndOfStream: false, ct).ConfigureAwait(false);
        options = options with { Extension = extension };

        List<Exception> failures = [];
        foreach (var decoder in _decoders)
        {
            if (!decoder.CanDecode(extension, header.AsSpan(0, read)))
                continue;

            source.Position = 0;
            try
            {
                return await decoder.DecodeAsync(source, options, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(ex);
            }
        }

        throw failures.Count == 0
            ? new NotSupportedException($"No decoder for '{extension}' files.")
            : new InvalidDataException("The file could not be decoded.", new AggregateException(failures));
    }
}
