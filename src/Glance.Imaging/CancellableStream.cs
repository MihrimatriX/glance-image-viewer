namespace Glance.Imaging;

/// <summary>
/// Read-only view of a stream that throws once <paramref name="ct"/> is cancelled. Lets a decoder that reads synchronously
/// through callbacks (ImageMagick) stop at its next read instead of finishing a file nobody wants any more.
/// </summary>
internal sealed class CancellableStream(Stream inner, CancellationToken ct) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ct.ThrowIfCancellationRequested();
        return inner.Read(buffer);
    }

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    // The caller owns the inner stream.
}
