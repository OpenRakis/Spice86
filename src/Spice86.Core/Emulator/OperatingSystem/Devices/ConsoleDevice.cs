namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

/// <summary>
/// Represents the console device.
/// </summary>
public class ConsoleDevice : CharacterDevice {
    private readonly KeyboardInt16Handler _keyboardInt16Handler;
    private readonly ScreenStream _screenStream;
    private readonly Queue<byte> _keyboardbuffer = new();

    /// <summary>
    /// Create a new console device.
    /// </summary>
    public ConsoleDevice(ILoggerService loggerService, State state,
        IVgaFunctionality vgaFunctionality, KeyboardInt16Handler keyboardInt16Handler,
        DeviceAttributes attributes)
        : base(loggerService, attributes, "CON") {
        _keyboardInt16Handler = keyboardInt16Handler;
        _screenStream = new ScreenStream(state, vgaFunctionality);
    }

    /// <summary>
    /// Gets whether the keyboard buffer has pending keycode data.
    /// </summary>
    /// <returns><c>True</c> if the keyboard has pending input, <c>False</c> otherwise.</returns>
    private bool HasInput => _keyboardInt16Handler.HasKeyCodePending();

    /// <summary>
    /// Returns the next pending key code.
    /// </summary>
    /// <returns>The next pending keycode.</returns>
    private ushort GetPendingInput() {
        return _keyboardInt16Handler.GetNextKeyCode() ?? 0;
    }

    public override string Name => "CON";

    public override bool CanSeek => _screenStream.CanSeek;

    public override bool CanRead => HasInput;

    public override bool CanWrite => _screenStream.CanWrite;

    public override long Length => 1;

    public override long Position { get; set; }

    public override void SetLength(long value) {
        //NOP
    }

    public override void Flush() {
        //NOP
    }

    public override long Seek(long offset, SeekOrigin origin) {
        return _screenStream.Seek(offset, origin);
    }

    public override int Read(byte[] buffer, int offset, int count) {
        if (!HasInput) {
            return -1;
        }
        int read = 0;

        for (int i = offset; i < Math.Min(buffer.Length, count); i++) {
            if (i > buffer.Length) {
                return -1;
            }
            if (_keyboardbuffer.TryDequeue(out byte secondByte)) {
                buffer[i] = secondByte;
            } else {
                if (!HasInput) {
                    return -1;
                }
                ushort keyboardInput = GetPendingInput();
                byte[] bytes = BitConverter.GetBytes(keyboardInput);
                buffer[i] = bytes[0];
                _keyboardbuffer.Enqueue(bytes[1]);
            }
            read++;
            Position++;
        }
        return read;
    }

    public override void Write(byte[] buffer, int offset, int count) {
        _screenStream.Write(buffer, offset, count);
    }

    public override ushort Information {
        get {
            if (HasInput) {
                return 0x80D3; // Input available
            } else {
                return 0x8093; // No input available
            }
        }
    }
}