namespace Spice86.MemoryWrappers;

using Spice86.Core.Emulator.Memory;

public class EmulatedMemoryStream : Stream {
    private readonly IMemory _memory;
    private long _length;
    private readonly bool _canRead;
    private readonly bool _canWrite;
    private readonly bool _canSeek;
    public EmulatedMemoryStream(IMemory memory) {
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
        int bytesRead = 0;
        for (int i = 0; i < buffer.Length; i++) {
            if (i + offset > buffer.Length || Position > _memory.Length) {
                break;
            }
            buffer[i + offset] = _memory[(uint)Position];
            bytesRead++;
            Position++;
        }
        return bytesRead;
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
        for (int i = 0; i < count; i++) {
            if (i + offset > buffer.Length || Position > _memory.Length) {
                break;
            }
            _memory[(uint)Position] = buffer[i + offset];
            Position++;
        }
    }

    public override bool CanRead => _canRead;
    public override bool CanSeek => _canSeek;
    public override bool CanWrite => _canWrite;
    public override long Length => _length;
    public override long Position { get; set; }
}