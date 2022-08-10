using System;
using System.IO;

namespace SDLSharp
{

    public class RWOpsStream : Stream
    {
        readonly RWOps ops;
        readonly bool close;

        public RWOpsStream(RWOps ops, bool close = false)
        {
            this.ops = ops;
            this.close = close;
        }

        Exception? GetError()
        {
            SDLException? err = NativeMethods.GetError();
            if (err != null)
                return new IOException(err.Message, err);
            else
                return null;
        }

        long SeekResult(long result)
        {
            if (result == -1)
                throw GetError() ?? new NotSupportedException();
            return result;
        }

        public override long Position
        {
            get => SeekResult(ops.Tell());
            set => SeekResult(ops.Seek(value, 0));
        }

        public override long Length => ops.Size();

        public override bool CanWrite => true;
        public override bool CanSeek => ops.Tell() != -1;
        public override bool CanRead => true;

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(new Span<byte>(buffer, offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            return ops.Read(buffer);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return SeekResult(ops.Seek(offset, (int)origin));
        }

        public override void SetLength(long v) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(new Span<byte>(buffer, offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            int res = ops.Write(buffer);
            if (res < buffer.Length)
                throw GetError() ?? new IOException();
        }

        public override void Flush() { }

        protected override void Dispose(bool disposing)
        {
            if (disposing && close)
                ops.Dispose();
        }
    }
}
