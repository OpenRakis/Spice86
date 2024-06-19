namespace Spice86.MemoryWrappers;

using Spice86.Core.Emulator.Memory;

public class CodeMemoryStream : Stream {
    private readonly IMemory _memory;
    private long _length;
    private readonly bool _canRead;
    private readonly bool _canWrite;
    private readonly bool _canSeek;
    public CodeMemoryStream(IMemory memory) {
        _memory = memory;
        _length = memory.Length;
        _canWrite = false;
        _canSeek = true;
        _canRead = true;
    }

    public override void Flush() {
        //nothing to do
    }

    public override int Read(byte[] buffer, int offset, int count) {
        byte[] ramCopy = _memory.ReadRam((uint)Math.Min(count, _memory.Length - Position), (uint)Position);
        ramCopy.CopyTo(buffer, offset);
        Position += ramCopy.Length;
        return ramCopy.Length;
    }

    public override long Seek(long offset, SeekOrigin origin) {
        switch (origin) {
            case SeekOrigin.Begin:
                Position = offset;
                break;
            case SeekOrigin.Current:
                Position += offset;
                break;
            case SeekOrigin.End:
                Position = Length - offset;
                break;
        }
        return Position;
    }

    public override void SetLength(long value) {
        _length = value;
    }

    public override void Write(byte[] buffer, int offset, int count) {
        throw new NotSupportedException();
    }

    public override bool CanRead => _canRead;
    public override bool CanSeek => _canSeek;
    public override bool CanWrite => _canWrite;
    public override long Length => _length;
    public override long Position { get; set; }
}