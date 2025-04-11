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
    private readonly State _state;
    private readonly Stream _writeStream;
    private readonly Stream _readStream;
    private readonly KeyboardStreamedInput _keyboardStreamedInput;

    /// <summary>
    /// Create a new console device.
    /// </summary>
    public ConsoleDevice(State state, IVgaFunctionality vgaFunctionality,
        KeyboardStreamedInput keyboardStreamedInput, DeviceAttributes attributes,
        string name, ILoggerService loggerService)
        : base(attributes, name, loggerService) {
        _state = state;
        _keyboardStreamedInput = keyboardStreamedInput;
        _writeStream = new KeyboardStream(keyboardStreamedInput);
        _readStream = new ScreenStream(_state, vgaFunctionality);
    }

    public override int Read(byte[] buffer, int offset, int count) {
        return _readStream.Read(buffer, offset, count);
    }

    public override void Write(byte[] buffer, int offset, int count) {
        _writeStream.Write(buffer, offset, count);
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