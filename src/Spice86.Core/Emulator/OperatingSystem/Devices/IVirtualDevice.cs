namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// The interface for all DOS virtual devices.
/// </summary>
public interface IVirtualDevice {

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