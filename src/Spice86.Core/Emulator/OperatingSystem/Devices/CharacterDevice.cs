namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Character devices are things like the console, the printer, the clock, etc.
/// </summary>
public abstract class CharacterDevice : VirtualDeviceBase {
    /// <summary>
    /// Create a new character device.
    /// </summary>
    protected CharacterDevice(IByteReaderWriter memory, uint baseAddress,
        string name, DeviceAttributes attributes = DeviceAttributes.Character)
        : base(new(memory, baseAddress) {
            Attributes = attributes | DeviceAttributes.Character,
            Name = name
        }) {
    }
}