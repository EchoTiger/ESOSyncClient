using System;
using System.IO;

namespace RedfurSync
{
    /// <summary>
    /// Wraps a readable stream and fires a progress callback as bytes are read.
    /// Used so HttpClient can report upload progress as it sends the file.
    /// </summary>
    public class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _totalLength;
        private readonly Action<float> _onProgress;
        private long _bytesRead = 0;

        public ProgressStream(Stream inner, long totalLength, Action<float> onProgress)
        {
            _inner = inner;
            _totalLength = totalLength;
            _onProgress = onProgress;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _inner.Read(buffer, offset, count);
            if (n > 0)
            {
                _bytesRead += n;
                if (_totalLength > 0)
                    _onProgress((float)_bytesRead / _totalLength);
            }
            return n;
        }

        public override bool CanRead  => _inner.CanRead;
        public override bool CanSeek  => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length   => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
