namespace MinIOStorageService.Services;

/// <summary>
/// 限制读取长度的流包装器 - 用于实现 Range 请求
/// </summary>
public class RangeStream : Stream
{
    private readonly Stream _innerStream;
    private readonly long _length;
    private long _position;

    public RangeStream(Stream innerStream, long length)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _length = length;
        _position = 0;
    }

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException("RangeStream does not support seeking");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _length)
        {
            return 0; // 已读取完毕
        }

        // 限制读取长度不超过剩余范围
        var remaining = _length - _position;
        var toRead = (int)Math.Min(count, remaining);

        var read = _innerStream.Read(buffer, offset, toRead);
        _position += read;

        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_position >= _length)
        {
            return 0; // 已读取完毕
        }

        // 限制读取长度不超过剩余范围
        var remaining = _length - _position;
        var toRead = (int)Math.Min(count, remaining);

        var read = await _innerStream.ReadAsync(buffer.AsMemory(offset, toRead), cancellationToken);
        _position += read;

        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException("RangeStream does not support seeking");
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException("RangeStream does not support setting length");
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("RangeStream does not support writing");
    }

    public override void Flush()
    {
        _innerStream.Flush();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _innerStream.Dispose();
        }
        base.Dispose(disposing);
    }
}
