using System;
using System.IO;

namespace io.github.toyota32k.BooLib.Server.http;

/// <summary>
/// Keep-Alive 時のリクエストボディ用 Stream。
/// Content-Length 分だけ読み取れるようにし、次のリクエストのデータを読み込まないようにする。
/// Dispose しても内側の Stream (ソケット) は閉じない。
/// </summary>
internal class RequestBodyStream : Stream {
    private readonly Stream _inner;
    private readonly long _length;
    private long _remaining;

    /// <summary>ボディの途中で接続が切れた (EOF) 場合 true</summary>
    public bool IsBroken { get; private set; } = false;
    public long Remaining => _remaining;

    public RequestBodyStream(Stream inner, long contentLength) {
        _inner = inner;
        _length = Math.Max(0, contentLength);
        _remaining = _length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position {
        get => _length - _remaining;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) {
        if (_remaining <= 0 || count <= 0) {
            return 0;
        }
        int n = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
        if (n <= 0) {
            IsBroken = true;
            return 0;
        }
        _remaining -= n;
        return n;
    }

    /// <summary>
    /// 未読のボディを読み捨てる。
    /// </summary>
    /// <param name="maxBytes">読み捨てる上限。これを超える場合は読まずに false を返す。</param>
    /// <returns>読み切れた場合 true (接続を再利用できる)</returns>
    public bool Drain(long maxBytes) {
        if (IsBroken) return false;
        if (_remaining <= 0) return true;
        if (_remaining > maxBytes) return false;
        try {
            var buffer = new byte[(int)Math.Min(_remaining, 64 * 1024)];
            while (_remaining > 0) {
                if (Read(buffer, 0, buffer.Length) == 0) {
                    return false;
                }
            }
            return true;
        }
        catch (IOException) {
            return false;
        }
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// 応答ボディの書き込みバイト数を数えるための Stream。
/// Content-Length と実際の送信バイト数が一致したかを確認するために使う。
/// Dispose しても内側の Stream は閉じない。
/// </summary>
internal class CountingWriteStream : Stream {
    private readonly Stream _inner;
    public long Count { get; private set; } = 0;

    public CountingWriteStream(Stream inner) {
        _inner = inner;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Count;
    public override long Position {
        get => Count;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) {
        _inner.Write(buffer, offset, count);
        Count += count;
    }

    public override void Write(ReadOnlySpan<byte> buffer) {
        _inner.Write(buffer);
        Count += buffer.Length;
    }

    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
