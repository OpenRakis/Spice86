namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

/// <summary>
/// Character devices are things like the console, the printer, the clock, etc.
/// </summary>
public class CharacterDevice : VirtualDeviceBase {
    protected readonly ILoggerService Logger;

    /// <summary>
    /// 8-byte field with the name of the device.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Create a new character device.
    /// </summary>
    /// <param name="attributes">The device attributes.</param>
    /// <param name="name">The name of the device.</param>
    /// <param name="loggerService"></param>
    /// <param name="strategy">Optional entrypoint for the strategy routine.</param>
    /// <param name="interrupt">Optional entrypoint for the interrupt routine.</param>
    public CharacterDevice(DeviceAttributes attributes, string name, ILoggerService loggerService, ushort strategy = 0, ushort interrupt = 0)
        : base(attributes, strategy, interrupt) {
        Attributes |= DeviceAttributes.Character;
        Name = name.Length > 8 ? name[..8] : name;
        Logger = loggerService;
    }

    /// <summary>
    /// Open a stream to the device.
    /// </summary>
    public virtual Stream OpenStream(string openMode) {
        return new DeviceStream(Name, openMode, Logger);
    }
}