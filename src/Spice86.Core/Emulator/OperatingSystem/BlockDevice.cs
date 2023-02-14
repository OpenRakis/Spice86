namespace Spice86.Core.Emulator.OperatingSystem;

/// <summary>
/// Block devices are things like hard drives, floppy drives, etc.
/// </summary>
internal class BlockDevice : VirtualDeviceBase {
    /// <summary>
    /// The number of units (disks) that this device has.
    /// </summary>
    public byte UnitCount { get; }
    /// <summary>
    /// An optional 7-byte field with the signature of the device.
    /// </summary>
    public string Signature { get; }

    /// <summary>
    /// Create a new virtual device.
    /// </summary>
    /// <param name="attributes">The device attributes.</param>
    /// <param name="unitCount">The amount of disks this device has.</param>
    /// <param name="strategy">Optional entrypoint for the strategy routine.</param>
    /// <param name="interrupt">Optional entrypoint for the interrupt routine.</param>
    public BlockDevice(DeviceAttributes attributes, byte unitCount, string signature = "", ushort strategy = 0, ushort interrupt = 0)
        : base(attributes, strategy, interrupt) {
        Attributes &= ~DeviceAttributes.Character;
        UnitCount = unitCount;
        Signature = signature.Length > 7 ? signature[..7] : signature;
    }
}