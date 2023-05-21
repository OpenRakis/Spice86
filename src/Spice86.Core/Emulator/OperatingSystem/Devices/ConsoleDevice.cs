namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Represents the console device.
/// </summary>
public class ConsoleDevice : CharacterDevice {
    private readonly Machine _machine;

    /// <summary>
    /// Create a new console device.
    /// </summary>
    public ConsoleDevice(DeviceAttributes attributes, string name, Machine machine, ILoggerService loggerService) : base(attributes, name, loggerService) {
        _machine = machine;
    }

    /// <inheritdoc />
    public override Stream OpenStream(string openMode) {
        switch (openMode) {
            case "w":
                return new ScreenStream(_machine);
            case "r":
                return Console.OpenStandardInput(); // TODO: new KeyboardStream(_machine);
            default:
                Logger.Error("Invalid open mode for console device: {Mode}", openMode);
                return new DeviceStream(Name, openMode, Logger);
        }
    }
}