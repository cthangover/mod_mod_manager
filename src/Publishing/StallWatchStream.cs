using System;
using System.IO;

namespace Mods.ModManager
{
    /// <summary>
    /// Read-only window of <paramref name="length"/> bytes of a file stream that
    /// reports every read. The publisher restarts its stall timer on each report,
    /// so a slow but moving upload is never cancelled; only silence is.
    /// </summary>
    public sealed class StallWatchStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action<int> _onRead;
        private long _remaining;

        public StallWatchStream(Stream inner, long length, Action<int> onRead)
        {
            _inner = inner;
            _remaining = length;
            _onRead = onRead;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0)
                return 0;
            var read = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= read;
            _onRead(read);
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
