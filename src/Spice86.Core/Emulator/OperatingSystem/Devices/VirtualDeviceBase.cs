namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.OperatingSystem.Enums;

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

    /// <summary>
    /// The segment where the device driver header is stored. 
    /// </summary>
    public ushort Segment { get; set; }
    /// <summary>
    /// The offset in the segment where the device driver header is stored.
    /// </summary>
    public ushort Offset { get; set; }
    /// <summary>
    /// The device attributes.
    /// <see href="https://github.com/microsoft/MS-DOS/blob/master/v2.0/bin/DEVDRIV.DOC#L125"/>
    /// </summary>
    public DeviceAttributes Attributes { get; set; }
    /// <summary>
    /// This is the entrypoint for the strategy routine.
    /// DOS will give this routine a Device Request Header when it wants the device to do something.
    /// </summary>
    public ushort StrategyEntryPoint { get; set; }
    /// <summary>
    /// This is the entrypoint for the interrupt routine.
    /// DOS will call this routine immediately after calling the strategy endpoint.
    /// </summary>
    public ushort InterruptEntryPoint { get; set; }
}