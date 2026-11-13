namespace App.Infrastructure.Files;

/// <summary>
/// Read-only wrapper that caps how many bytes may be read from an inner stream. The blob store
/// copies from this; once the cumulative read would exceed <c>maxBytes</c> it throws
/// <see cref="FileSizeExceededException"/>, so a stream that under-reports or cannot report its
/// length still cannot write an oversize file past the Req 7.3 limit. Memory stays bounded — bytes
/// pass straight through, nothing is buffered whole.
/// </summary>
internal sealed class SizeLimitedReadStream(Stream inner, long maxBytes) : Stream
{
    private long _bytesRead;

    /// <summary>Total bytes successfully read through this wrapper (the stored size).</summary>
    public long BytesRead => _bytesRead;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        Account(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        Account(read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        Account(read);
        return read;
    }

    private void Account(int read)
    {
        if (read <= 0)
        {
            return;
        }

        _bytesRead += read;
        if (_bytesRead > maxBytes)
        {
            throw new FileSizeExceededException();
        }
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    // Do not dispose the inner stream here; its lifetime is owned by the caller (the request).
}

/// <summary>Thrown by <see cref="SizeLimitedReadStream"/> when the cap is exceeded; mapped to PAYLOAD_TOO_LARGE by File_Service.</summary>
internal sealed class FileSizeExceededException() : Exception("The file exceeds the maximum allowed size.");
