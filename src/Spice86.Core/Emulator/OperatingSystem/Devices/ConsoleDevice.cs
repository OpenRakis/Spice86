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
    private readonly IVgaFunctionality _vgaFunctionality;
    private readonly KeyboardStreamedInput _keyboardStreamedInput;
    
    /// <summary>
    /// Create a new console device.
    /// </summary>
    public ConsoleDevice(State state, IVgaFunctionality vgaFunctionality, KeyboardStreamedInput keyboardStreamedInput, DeviceAttributes attributes, string name, ILoggerService loggerService) : base(attributes, name, loggerService) {
        _state = state;
        _vgaFunctionality = vgaFunctionality;
        _keyboardStreamedInput = keyboardStreamedInput;
    }

    /// <inheritdoc />
    public override Stream OpenStream(string openMode) {
        switch (openMode) {
            case "w":
                return new ScreenStream(_state, _vgaFunctionality);
            case "r":
                return new KeyboardStream(_keyboardStreamedInput);
            default:
                Logger.Error("Invalid open mode for console device: {Mode}", openMode);
                return new DeviceStream(Name, openMode, Logger);
        }
    }
}