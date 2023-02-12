namespace Spice86.Core.Emulator.OperatingSystem;

public class CharacterDevice : VirtualDeviceBase {
    public string Name { get; }

    public CharacterDevice(DeviceAttributes attributes, string name, ushort strategy = 0, ushort interrupt = 0)
        : base(attributes, strategy, interrupt) {
        Attributes |= DeviceAttributes.Character;
        Name = name.Length > 8 ? name[..8] : name;
    }
}