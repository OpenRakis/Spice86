namespace Spice86.Core.Emulator.OperatingSystem;

internal class BlockDevice : VirtualDeviceBase {
    public byte UnitCount { get; }
    public string Signature { get; }

    public BlockDevice(DeviceAttributes attributes, byte unitCount, string signature = "", ushort strategy = 0, ushort interrupt = 0)
        : base(attributes, strategy, interrupt) {
        Attributes &= ~DeviceAttributes.Character;
        UnitCount = unitCount;
        Signature = signature.Length > 7 ? signature[..7] : signature;
    }
}