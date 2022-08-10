using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public interface IRWOps
    {
        long Size();
        long Seek(long offset, int whence);
        int Read<T>(Span<T> dest) where T : struct;
        int Write<T>(Span<T> dest) where T : struct;
    }

    class StreamRWOps : IRWOps, IDisposable
    {
        readonly System.IO.Stream stream;
        readonly bool close;

        public StreamRWOps(System.IO.Stream stream, bool close)
        {
            this.stream = stream;
            this.close = close;
        }

        public long Size() => stream.Length;
        public long Seek(long offset, int whence) => stream.Seek(offset, (System.IO.SeekOrigin)whence);
        public int Read<T>(Span<T> dest) where T : struct
        {
            Span<byte> b = MemoryMarshal.AsBytes(dest);
            int size = b.Length / dest.Length;
            if (size == 1)
                return stream.Read(b);

            for (int i = 0; i < dest.Length; ++i)
            {
                int read = stream.Read(b.Slice(i * size, size));
                if (read < size)
                    return i - 1;
            }
            return dest.Length;
        }
        public int Write<T>(Span<T> src) where T : struct
        {
            Span<byte> b = MemoryMarshal.AsBytes(src);
            stream.Write(b);
            return src.Length;
        }

        public void Dispose()
        {
            if (close)
                stream.Dispose();
            else
                stream.Flush();
        }
    }

    class LogRWOps : IRWOps, IDisposable
    {
        readonly IRWOps inner;
        public LogRWOps(IRWOps inner)
        {
            this.inner = inner;
        }
        public long Size()
        {
            try
            {
                Console.Write($".Size()");
                long ret = inner.Size();
                Console.WriteLine($"\t: {ret}");
                return ret;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\t{ex}");
                throw;
            }
        }
        public long Seek(long offset, int whence)
        {
            try
            {
                Console.Write($".Seek({offset}, {whence})");
                long ret = inner.Seek(offset, whence);
                Console.WriteLine($"\t{ret}");
                return ret;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\t{ex}");
                throw;
            }
        }
        public int Read<T>(Span<T> dest) where T : struct
        {
            try
            {
                int sz = MemoryMarshal.AsBytes(dest).Length / dest.Length;
                Console.Write($".Read({sz}, {dest.Length})");
                int ret = inner.Read(dest);
                Console.WriteLine($"\t{ret}");
                return ret;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\t{ex}");
                throw;
            }
        }
        public int Write<T>(Span<T> src) where T : struct
        {
            try
            {
                int sz = MemoryMarshal.AsBytes(src).Length / src.Length;
                Console.Write($".Write({sz}, {src.Length})");
                int ret = inner.Write(src);
                Console.WriteLine($"\t{ret}");
                return ret;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\t{ex}");
                throw;
            }
        }
        public void Dispose()
        {
            try
            {
                Console.Write(".Dispose()");
                if (inner is IDisposable d)
                    d.Dispose();
                Console.WriteLine("\tOK");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\t{ex}");
                throw;
            }
        }
    }
}
