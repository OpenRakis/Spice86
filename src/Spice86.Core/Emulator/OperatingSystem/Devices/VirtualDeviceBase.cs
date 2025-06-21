namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The abstract base class for all DOS virtual devices
/// </summary>
public abstract class VirtualDeviceBase : VirtualFileBase, IVirtualDevice {
    protected ILoggerService Logger;

    /// <summary>
    /// Create a new virtual device.
    /// </summary>
    /// <param name="loggerService">The logging implementation.</param>
    /// <param name="attributes">The device attributes</param>
    /// <param name="strategy">Optional entrypoint for the strategy routine.</param>
    /// <param name="interrupt">Optional entrypoint for the interrupt routine</param>
    protected VirtualDeviceBase(ILoggerService loggerService,
        DeviceAttributes attributes,
        ushort strategy = 0, ushort interrupt = 0) {
        Logger = loggerService;
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

    public uint DeviceNumber { get; set; }

    public virtual byte GetStatus(bool inputFlag) => 0;
    public virtual bool TryReadFromControlChannel(uint address, ushort size,
        [NotNullWhen(true)] out ushort? returnCode) {
        returnCode = null;
        return false;
    }

    public virtual bool TryWriteToControlChannel(uint address, ushort size,
        [NotNullWhen(true)] out ushort? returnCode) {
        returnCode = null;
        return false;
    }
}