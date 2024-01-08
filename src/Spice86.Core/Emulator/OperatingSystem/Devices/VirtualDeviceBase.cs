namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// The abstract base class for all DOS virtual devices
/// </summary>
public abstract class VirtualDeviceBase : IVirtualDevice {
    /// <summary>
    /// Create a new virtual device.
    /// </summary>
    /// <param name="attributes">The device attributes</param>
    /// <param name="strategy">Optional entrypoint for the strategy routine.</param>
    /// <param name="interrupt">Optional entrypoint for the interrupt routine</param>
    protected VirtualDeviceBase(DeviceAttributes attributes, ushort strategy = 0, ushort interrupt = 0) {
        Attributes = attributes;
        StrategyEntryPoint = strategy;
        InterruptEntryPoint = interrupt;
    }

    /// <inheritdoc />
    public ushort Segment { get; set; }

    /// <inheritdoc />
    public ushort Offset { get; set; }

    /// <inheritdoc />
    public DeviceAttributes Attributes { get; set; }

    /// <inheritdoc />
    public ushort StrategyEntryPoint { get; set; }

    /// <inheritdoc />
    public ushort InterruptEntryPoint { get; set; }
}