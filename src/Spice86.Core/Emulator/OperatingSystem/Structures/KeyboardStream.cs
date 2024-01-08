namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Devices.Input.Keyboard;

/// <summary>
/// Represents a stream for reading the keyboard input.
/// </summary>
public class KeyboardStream : Stream {
    private readonly KeyboardStreamedInput _keyboardStreamedInput;

    /// <summary>
    /// Creates a new instance of the <see cref="KeyboardStream" /> class.
    /// </summary>
    public KeyboardStream(KeyboardStreamedInput keyboardStreamedInput) {
        _keyboardStreamedInput = keyboardStreamedInput;
    }

    /// <inheritdoc />
    public override bool CanRead => _keyboardStreamedInput.HasInput;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => 1;

    /// <inheritdoc />
    public override long Position { get; set; }

    /// <inheritdoc />
    public override void Flush() {
    }

    private readonly Queue<byte> _keyboardbuffer = new();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) {
        int read = 0;

        for (int i = offset; i < Math.Min(buffer.Length, count); i++) {
            if (i > buffer.Length) {
                return -1;
            }
            if (_keyboardbuffer.TryDequeue(out byte secondByte)) {
                buffer[i] = secondByte;
            } else {
                if (!_keyboardStreamedInput.HasInput) {
                    return -1;
                }
                ushort keyboardInput = _keyboardStreamedInput.GetPendingInput();
                byte[] bytes = BitConverter.GetBytes(keyboardInput);
                buffer[i] = bytes[0];
                _keyboardbuffer.Enqueue(bytes[1]);
            }
            read++;
            Position++;
        }
        return read;
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) {
        throw new NotSupportedException("Cannot seek in the keyboard.");
    }

    /// <inheritdoc />
    public override void SetLength(long value) {
        throw new NotSupportedException("Cannot set the length of the keyboard stream.");
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) {
        throw new NotSupportedException("Cannot write to the keyboard.");
    }
}