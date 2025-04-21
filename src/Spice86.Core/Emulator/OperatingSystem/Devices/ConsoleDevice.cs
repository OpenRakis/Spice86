namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

/// <summary>
/// Represents the console device.
/// </summary>
public class ConsoleDevice : CharacterDevice {
    private readonly KeyboardStream _keyboardStream;
    private readonly KeyboardStreamedInput _keyboardStreamedInput;
    private readonly ScreenStream _screenStream;

    /// <summary>
    /// Create a new console device.
    /// </summary>
    public ConsoleDevice(ILoggerService loggerService, State state,
        IVgaFunctionality vgaFunctionality, KeyboardStreamedInput keyboardStreamedInput,
        DeviceAttributes attributes)
        : base(loggerService, attributes, "CON") {
        _keyboardStreamedInput = keyboardStreamedInput;
        _keyboardStream = new KeyboardStream(keyboardStreamedInput);
        _screenStream = new ScreenStream(state, vgaFunctionality);
    }

    public override string Name => "CON";

    public override bool CanSeek => _screenStream.CanSeek;

    public override bool CanRead => _keyboardStream.CanRead;

    public override bool CanWrite => _screenStream.CanWrite;

    public override long Length => _keyboardStream.Length;

    public override long Position {
        get => _keyboardStream.Position;
        set => _keyboardStream.Position = value;
    }

    public override void SetLength(long value) {
        _keyboardStream.SetLength(value);
    }

    public override void Flush() {
        _keyboardStream.Flush();
        _screenStream.Flush();
    }

    public override long Seek(long offset, SeekOrigin origin) {
        return _screenStream.Seek(offset, origin);
    }

    public override int Read(byte[] buffer, int offset, int count) {
        if (!_keyboardStreamedInput.HasInput) {
            return -1;
        }
        return _keyboardStream.Read(buffer, offset, count);
    }

    public override void Write(byte[] buffer, int offset, int count) {
        _screenStream.Write(buffer, offset, count);
    }

    public override ushort Information {
        get {
            if (_keyboardStreamedInput.HasInput) {
                return 0x80D3; // Input available
            } else {
                return 0x8093; // No input available
            }
        }
    }
}